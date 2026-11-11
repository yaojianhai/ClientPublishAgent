param(
	[ValidateSet("linux-arm64", "linux-x64")]
	[string]$RuntimeIdentifier,
	[string]$RootDirectory,
	[string]$PublishDirectory,
	[string]$OutputDirectory,
	[string]$ClientVersion,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[ValidatePattern('^[A-Za-z][A-Za-z0-9.-]*$')]
	[string]$BundleIdentifier = "com.gildata.warrenqf1"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "linux-common.ps1")
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
$RuntimeIdentifier = Get-LinuxRuntimeIdentifier -RuntimeIdentifier $RuntimeIdentifier
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Split-Path -Parent $scriptsDirectory } else { Split-Path -Parent ([System.IO.Path]::GetFullPath($SourceDirectory)) }
$RootDirectory = Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $RuntimeIdentifier -RepositoryRoot $repoRoot -RootDirectory $RootDirectory
if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
	$PSBoundParameters["RuntimeIdentifier"] = $RuntimeIdentifier
	$PSBoundParameters["RootDirectory"] = $RootDirectory
	Invoke-LinuxScriptInWsl -ScriptPath $PSCommandPath -Parameters $PSBoundParameters
	return
}
foreach ($command in "dpkg-deb", "readelf", "find", "chmod", "ln") {
	if (-not (Get-Command $command -CommandType Application -ErrorAction SilentlyContinue)) { throw "未找到 Linux 打包所需命令：$command" }
}

$srcDirectory = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Join-Path $repoRoot "src" } else { [System.IO.Path]::GetFullPath($SourceDirectory) }
$iconFile = Join-Path $srcDirectory "WarrenqF1/Assets/Images/Logos/warrenq.png"
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { $PublishDirectory = Join-Path $RootDirectory "StagingRelease" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $RootDirectory "output" }
$PublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
	throw "未找到暂存发布目录：$PublishDirectory。请先执行 publish-linux.ps1，或执行 linux-publish&upgrade&package.ps1 完成发布、升级包和安装包流程。"
}
$publishDirectoryPrefix = $PublishDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($OutputDirectory -eq $PublishDirectory -or $OutputDirectory.StartsWith($publishDirectoryPrefix, [System.StringComparison]::Ordinal)) {
	throw "安装包输出目录不能位于发布目录内：$OutputDirectory"
}
if (-not (Test-Path -LiteralPath $iconFile -PathType Leaf)) { throw "未找到 Linux 应用图标：$iconFile" }

$manifestFile = Join-Path $PublishDirectory "generalupdate.manifest.json"
if (-not [string]::IsNullOrWhiteSpace($ClientVersion)) {
	$manifest = [ordered]@{ mainAppName = "WarrenqF1.Desktop"; clientVersion = $ClientVersion; appType = "OssClient"; updateAppName = "WarrenqF1.Updater"; updatePath = "update/" }
	[System.IO.File]::WriteAllText($manifestFile, ($manifest | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
}
if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "未找到发布清单：$manifestFile，请先执行 upgrade-linux.ps1。" }
$publishedManifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json -ErrorAction Stop
$clientVersionProperty = $publishedManifest.PSObject.Properties["clientVersion"]
if ($null -eq $clientVersionProperty -or [string]::IsNullOrWhiteSpace([string]$clientVersionProperty.Value)) {
	throw "发布清单未包含有效的 clientVersion：$manifestFile"
}
$Version = ([string]$clientVersionProperty.Value).Trim()
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw "发布清单版本号格式无效：$Version" }

$relativeExecutablePaths = @(
	"WarrenqF1.Desktop",
	"update/WarrenqF1.Updater",
	"WarrenqAIEngine/Launcher/WarrenqAILauncher",
	"WarrenqAIEngine/7zz",
	"WarrenqAIEngine/node/node"
)
foreach ($relativeExecutablePath in $relativeExecutablePaths) {
	Test-LinuxExecutable -Path (Join-Path $PublishDirectory $relativeExecutablePath) -RuntimeIdentifier $RuntimeIdentifier
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$packageIdentifier = [guid]::NewGuid().ToString("N")
# WSL 的 Windows 挂载目录可能无法保存 Unix 权限，包目录必须在 Linux 临时目录构建。
$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (".linux-package-" + $packageIdentifier)
$debianArchitecture = switch ($RuntimeIdentifier) { "linux-arm64" { "arm64"; break } "linux-x64" { "amd64"; break } }
$packageDirectory = Join-Path $stagingDirectory "package"
$applicationDirectory = Join-Path $packageDirectory "opt/WarrenqF1"
$controlDirectory = Join-Path $packageDirectory "DEBIAN"
$commandDirectory = Join-Path $packageDirectory "usr/bin"
$desktopDirectory = Join-Path $packageDirectory "usr/share/applications"
$iconDirectory = Join-Path $packageDirectory "usr/share/pixmaps"
$packageFile = Join-Path $OutputDirectory "warrenqf1_${Version}_${debianArchitecture}.deb"
$temporaryPackageFile = Join-Path $stagingDirectory "warrenqf1.deb"
$temporaryOutputFile = Join-Path $OutputDirectory (".linux-package-" + $packageIdentifier + ".deb")
$utf8 = [System.Text.UTF8Encoding]::new($false)

try {
	New-Item -ItemType Directory -Path $applicationDirectory, $controlDirectory, $commandDirectory, $desktopDirectory, $iconDirectory -Force | Out-Null
	Get-ChildItem -LiteralPath $PublishDirectory -Force | Copy-Item -Destination $applicationDirectory -Recurse -Force
	Copy-Item -LiteralPath $iconFile -Destination (Join-Path $iconDirectory "$BundleIdentifier.png") -Force

	$launcher = @'
#!/bin/sh
set -eu
source_dir=/opt/WarrenqF1
package_version="{{VERSION}}"
runtime_parent="${XDG_DATA_HOME:-$HOME/.local/share}/WarrenqF1"
application_dir="$runtime_parent/application"
package_marker="$runtime_parent/.deb-version"
mkdir -p -- "$runtime_parent"
exec 9>"$runtime_parent/.install.lock"
flock 9
installed_package_version=""
if [ -f "$package_marker" ]; then
	installed_package_version=$(cat "$package_marker")
fi
refresh=0
if [ ! -x "$application_dir/WarrenqF1.Desktop" ]; then
	refresh=1
elif [ "$installed_package_version" != "$package_version" ]; then
	current_version=""
	if [ -f "$application_dir/generalupdate.manifest.json" ]; then
		current_version=$(sed -n 's/.*"clientVersion"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$application_dir/generalupdate.manifest.json")
	fi
	if [ -z "$current_version" ] || ! dpkg --compare-versions "$current_version" ge "$package_version" 2>/dev/null; then
		refresh=1
	fi
fi
if [ "$refresh" -eq 1 ]; then
	temporary_dir=$(mktemp -d "$runtime_parent/.install.XXXXXX")
	trap 'if [ -e "$temporary_dir/old" ] && [ ! -e "$application_dir" ]; then mv -- "$temporary_dir/old" "$application_dir"; fi; rm -rf -- "$temporary_dir"' EXIT
	cp -R -- "$source_dir" "$temporary_dir/new"
	if [ -e "$application_dir" ]; then
		mv -- "$application_dir" "$temporary_dir/old"
	fi
	mv -- "$temporary_dir/new" "$application_dir"
	rm -rf -- "$temporary_dir"
	trap - EXIT
fi
printf '%s\n' "$package_version" > "$package_marker"
flock -u 9
exec 9>&-
# Keep package identity after copying to the writable runtime, including updater and AI children.
DEB_PACKAGE_NAME=warrenqf1
GIO_LAUNCHED_DESKTOP_FILE=/usr/share/applications/{{BUNDLE_IDENTIFIER}}.desktop
export DEB_PACKAGE_NAME GIO_LAUNCHED_DESKTOP_FILE
cd -- "$application_dir"
exec "$application_dir/WarrenqF1.Desktop" "$@"
'@
	$launcherPath = Join-Path $applicationDirectory "warrenqf1"
	[System.IO.File]::WriteAllText($launcherPath, ($launcher.Replace("{{VERSION}}", $Version).Replace("{{BUNDLE_IDENTIFIER}}", $BundleIdentifier).Replace("`r`n", "`n") + "`n"), $utf8)
	& ln -s /opt/WarrenqF1/warrenqf1 (Join-Path $commandDirectory "warrenqf1")
	if ($LASTEXITCODE -ne 0) { throw "无法创建 Warrenq F1 启动命令链接。" }
	$desktopEntry = @"
[Desktop Entry]
Type=Application
Name=Warrenq F1
Comment=Warrenq F1 桌面终端
Exec=/opt/WarrenqF1/warrenqf1
TryExec=/opt/WarrenqF1/WarrenqF1.Desktop
Icon=$BundleIdentifier
Terminal=false
Categories=Office;Finance;
StartupNotify=true
StartupWMClass=WarrenqF1.Desktop
X-WarrenqF1-Package=warrenqf1
"@
	[System.IO.File]::WriteAllText((Join-Path $desktopDirectory "$BundleIdentifier.desktop"), ($desktopEntry.Replace("`r`n", "`n") + "`n"), $utf8)

	# 保留更新器窗口匹配信息；手动启动时走主程序更新流程，避免直接更新只读安装副本。
	$updaterDesktopEntry = @"
[Desktop Entry]
Type=Application
Name=Warrenq F1 更新器
Exec=/opt/WarrenqF1/warrenqf1
TryExec=/opt/WarrenqF1/WarrenqF1.Desktop
Icon=$BundleIdentifier
NoDisplay=true
Terminal=false
StartupWMClass=WarrenqF1.Updater
X-WarrenqF1-Package=warrenqf1
"@
	[System.IO.File]::WriteAllText((Join-Path $desktopDirectory "WarrenqF1.Updater.desktop"), ($updaterDesktopEntry.Replace("`r`n", "`n") + "`n"), $utf8)

	$postinst = @'
#!/bin/sh
set -eu
if [ "$1" = configure ]; then
	if command -v update-desktop-database >/dev/null 2>&1; then
		update-desktop-database /usr/share/applications
	else
		printf '%s\n' 'warrenqf1: desktop-file-utils is unavailable; desktop cache was not refreshed.' >&2
	fi
	application_entry='/usr/share/applications/{{BUNDLE_IDENTIFIER}}.desktop'
	shortcut_name='{{BUNDLE_IDENTIFIER}}.desktop'
	shortcut_marker='X-WarrenqF1-Package=warrenqf1'
	find_desktop_directory() {
		user_name=$1
		user_home=$2
		desktop_directory=''
		if command -v xdg-user-dir >/dev/null 2>&1 && command -v runuser >/dev/null 2>&1; then
			if desktop_directory=$(runuser -u "$user_name" -- env HOME="$user_home" xdg-user-dir DESKTOP 2>/dev/null); then
				case "$desktop_directory" in ''|/|"$user_home") desktop_directory='' ;; /*) ;; *) desktop_directory='' ;; esac
				if [ -n "$desktop_directory" ] && { [ -d "$desktop_directory" ] || runuser -u "$user_name" -- env HOME="$user_home" install -d -m 755 -- "$desktop_directory"; }; then
					printf '%s\n' "$desktop_directory"
					return 0
				fi
			fi
		fi
		for candidate in "$user_home/Desktop" "$user_home/桌面"; do
			if [ -d "$candidate" ]; then printf '%s\n' "$candidate"; return 0; fi
		done
		candidate="$user_home/Desktop"
		if runuser -u "$user_name" -- env HOME="$user_home" install -d -m 755 -- "$candidate"; then
			printf '%s\n' "$candidate"
			return 0
		fi
		return 1
	}
	if [ "$(id -u)" -eq 0 ]; then
		getent passwd | while IFS=: read -r user_name _ user_id group_id _ user_home login_shell; do
			case "$user_id" in ''|*[!0-9]*) continue ;; esac
			if [ "$user_id" -lt 1000 ] || [ ! -d "$user_home" ]; then continue; fi
			case "$login_shell" in */nologin|*/false|'') continue ;; esac
			if ! desktop_directory=$(find_desktop_directory "$user_name" "$user_home"); then
				printf 'warrenqf1: no Desktop directory found for user %s; shortcut was not created.\n' "$user_name" >&2
				continue
			fi
			shortcut="$desktop_directory/$shortcut_name"
			if [ -L "$shortcut" ]; then
				printf 'warrenqf1: preserving existing Desktop link %s.\n' "$shortcut" >&2
				continue
			fi
			if [ -e "$shortcut" ] && ! grep -Fxq "$shortcut_marker" "$shortcut"; then
				printf 'warrenqf1: preserving existing Desktop file %s.\n' "$shortcut" >&2
				continue
			fi
			runuser -u "$user_name" -- env HOME="$user_home" install -m 755 -- "$application_entry" "$shortcut"
			printf 'warrenqf1: created Desktop shortcut for user %s.\n' "$user_name"
		done
	else
		printf '%s\n' 'warrenqf1: desktop shortcuts can only be created by the package installer running as root.' >&2
	fi
	printf '%s\n' 'warrenqf1: application registered; network access follows the system security policy. Existing deny rules are preserved.'
fi
'@
	$postrm = @'
#!/bin/sh
set -eu
case "$1" in
	remove|purge)
		if [ "$(id -u)" -eq 0 ]; then
			getent passwd | while IFS=: read -r user_name _ user_id _ _ user_home login_shell; do
				case "$user_id" in ''|*[!0-9]*) continue ;; esac
				if [ "$user_id" -lt 1000 ] || [ ! -d "$user_home" ]; then continue; fi
				case "$login_shell" in */nologin|*/false|'') continue ;; esac
				desktop_directory=''
				if command -v xdg-user-dir >/dev/null 2>&1 && command -v runuser >/dev/null 2>&1; then
					if desktop_directory=$(runuser -u "$user_name" -- env HOME="$user_home" xdg-user-dir DESKTOP 2>/dev/null) && [ ! -d "$desktop_directory" ]; then desktop_directory=''; fi
				fi
				if [ -z "$desktop_directory" ]; then
					for candidate in "$user_home/Desktop" "$user_home/桌面"; do
						if [ -d "$candidate" ]; then desktop_directory=$candidate; break; fi
					done
				fi
				[ -n "$desktop_directory" ] || continue
				shortcut="$desktop_directory/{{BUNDLE_IDENTIFIER}}.desktop"
				if [ -f "$shortcut" ] && [ ! -L "$shortcut" ] && grep -Fxq 'X-WarrenqF1-Package=warrenqf1' "$shortcut"; then
					rm -f -- "$shortcut"
				fi
			done
		fi
		if command -v update-desktop-database >/dev/null 2>&1; then
			update-desktop-database /usr/share/applications
		else
			printf '%s\n' 'warrenqf1: desktop-file-utils is unavailable; desktop cache was not refreshed.' >&2
		fi
		;;
esac
'@
	foreach ($maintenanceScript in @{ postinst = $postinst; postrm = $postrm }.GetEnumerator()) {
		$contents = $maintenanceScript.Value.Replace("{{BUNDLE_IDENTIFIER}}", $BundleIdentifier)
		[System.IO.File]::WriteAllText((Join-Path $controlDirectory $maintenanceScript.Key), ($contents.Replace("`r`n", "`n") + "`n"), $utf8)
	}

	$minimumGlibc = [version]"2.28"
	$nativeFiles = [System.Collections.Generic.List[string]]::new()
	foreach ($file in Get-ChildItem -LiteralPath $applicationDirectory -Recurse -File -Force) {
		if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
		$header = [byte[]]::new(4)
		$stream = [System.IO.File]::OpenRead($file.FullName)
		try { $bytesRead = $stream.Read($header, 0, $header.Length) } finally { $stream.Dispose() }
		if ($bytesRead -ne 4 -or $header[0] -ne 0x7F -or $header[1] -ne 0x45 -or $header[2] -ne 0x4C -or $header[3] -ne 0x46) { continue }
		$nativeFiles.Add($file.FullName)
		$versionInfo = & readelf --version-info --wide $file.FullName
		if ($LASTEXITCODE -ne 0) { throw "无法读取 ELF 依赖版本：$($file.FullName)" }
		foreach ($match in [regex]::Matches(($versionInfo -join "`n"), 'GLIBC_(\d+\.\d+(?:\.\d+)?)')) {
			$requiredVersion = [version]$match.Groups[1].Value
			if ($requiredVersion -gt $minimumGlibc) { $minimumGlibc = $requiredVersion }
		}
		if (($versionInfo -join "`n") -match 'GLIBC_ABI_DT_RELR' -and $minimumGlibc -lt [version]"2.36") { $minimumGlibc = [version]"2.36" }
	}
	$installedBytes = (Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum
	$installedSize = [long][Math]::Ceiling($installedBytes / 1024)
	$control = @"
Package: warrenqf1
Version: $Version
Architecture: $debianArchitecture
Maintainer: Warrenq F1
Section: utils
Priority: optional
Installed-Size: $installedSize
Depends: libc6 (>= $minimumGlibc), libgcc-s1 | libgcc1, libstdc++6, zlib1g, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6, libxext6, libxrandr2, libxi6, libxcursor1, libgl1, libssl3 | libssl3t64 | libssl1.1 (>= 1.1.1), util-linux
Description: Warrenq F1 桌面终端
 面向 Debian 系麒麟、统信桌面系统的 Warrenq F1 终端。
 包含主程序、更新器和 Warrenq AI 运行依赖。
"@
	[System.IO.File]::WriteAllText((Join-Path $controlDirectory "control"), ($control.Replace("`r`n", "`n") + "`n"), $utf8)
	& find $packageDirectory -type d -exec chmod 755 "{}" "+"
	if ($LASTEXITCODE -ne 0) { throw "无法设置 Debian 包目录权限。" }
	& find $packageDirectory -type f -exec chmod 644 "{}" "+"
	if ($LASTEXITCODE -ne 0) { throw "无法设置 Debian 包文件权限。" }
	foreach ($nativeFile in $nativeFiles) { Set-LinuxExecutablePermission -Path $nativeFile }
	Set-LinuxExecutablePermission -Path $launcherPath
	foreach ($name in "postinst", "postrm") { Set-LinuxExecutablePermission -Path (Join-Path $controlDirectory $name) }
	foreach ($scriptFile in Get-ChildItem -LiteralPath $applicationDirectory -Recurse -File -Filter "*.sh") { Set-LinuxExecutablePermission -Path $scriptFile.FullName }

	$buildOutput = & dpkg-deb --build --root-owner-group --uniform-compression -Zxz -z6 $packageDirectory $temporaryPackageFile 2>&1
	$buildExitCode = $LASTEXITCODE
	if ($buildExitCode -ne 0) {
		$buildDetails = ($buildOutput | Out-String).Trim()
		throw "Debian 安装包生成失败：$temporaryPackageFile（退出代码：$buildExitCode）`n$buildDetails"
	}
	$buildOutput | ForEach-Object { Write-Host $_ }
	& dpkg-deb --info $temporaryPackageFile | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "Debian 安装包控制信息校验失败：$temporaryPackageFile" }
	& dpkg-deb --contents $temporaryPackageFile | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "Debian 安装包内容校验失败：$temporaryPackageFile" }
	if (-not (Test-Path -LiteralPath $temporaryPackageFile -PathType Leaf) -or (Get-Item -LiteralPath $temporaryPackageFile).Length -le 0) {
		throw "未生成有效的 Debian 安装包：$temporaryPackageFile"
	}
	Copy-Item -LiteralPath $temporaryPackageFile -Destination $temporaryOutputFile -Force
	Move-Item -LiteralPath $temporaryOutputFile -Destination $packageFile -Force
}
finally {
	if (Test-Path -LiteralPath $temporaryOutputFile) { Remove-Item -LiteralPath $temporaryOutputFile -Force }
	if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
}
Write-Host "Debian 安装包生成完成：$packageFile（架构：$debianArchitecture；最低 glibc：$minimumGlibc）。请使用系统软件安装器或 sudo apt install 安装。"
