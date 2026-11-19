param(
	[ValidateSet("osx-arm64", "osx-x64")]
	[string]$RuntimeIdentifier,
	[string]$RootDirectory,

	[string]$PublishDirectory,

	[string]$OutputDirectory,

	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,

	[string]$SigningIdentity = $env:APPLE_SIGNING_IDENTITY,

	[string]$NotaryKeychainProfile = $env:APPLE_NOTARY_PROFILE,

	[string]$BundleIdentifier = "com.gildata.warrenqf1"
)

$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
. (Join-Path $PSScriptRoot "mac-office-resources.ps1")

function Test-RequiredCommand {
	param(
		[Parameter(Mandatory)]
		[string]$Name
	)

	if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
		throw "未找到 macOS 打包所需命令：$Name"
	}
}

function Submit-ForNotarization {
	param(
		[Parameter(Mandatory)]
		[string]$Path,

		[Parameter(Mandatory)]
		[string]$Description
	)

	Write-Host "提交 $Description 到 Apple 公证服务并等待结果。"
	$output = & xcrun notarytool submit $Path --keychain-profile $NotaryKeychainProfile --wait --output-format json 2>&1
	$exitCode = $LASTEXITCODE
	$outputText = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
	if (-not [string]::IsNullOrWhiteSpace($outputText)) {
		Write-Host $outputText
	}
	if ($exitCode -ne 0) {
		throw "Apple 公证提交失败（退出代码：$exitCode）：$Description"
	}

	try {
		$submission = ConvertFrom-Json -InputObject $outputText -ErrorAction Stop
	}
	catch {
		throw "无法解析 Apple 公证结果：$Description。原始输出：$outputText"
	}
	if ($submission.status -ne "Accepted") {
		$logHint = if ([string]::IsNullOrWhiteSpace([string]$submission.id)) { "" } else { " 可用 xcrun notarytool log $($submission.id) --keychain-profile `"$NotaryKeychainProfile`" 查询详情。" }
		throw "Apple 未接受 $Description 公证，状态：$($submission.status)。$logHint"
	}
	Write-Host "Apple 已接受 $Description 公证（提交 ID：$($submission.id)）。"
}

function Invoke-NativeCommand {
	param(
		[Parameter(Mandatory)]
		[string]$Name,

		[Parameter(Mandatory)]
		[string[]]$Arguments
	)

	& $Name @Arguments
	if ($LASTEXITCODE -ne 0) {
		throw "命令执行失败：$Name（退出代码：$LASTEXITCODE）"
	}
}

function Set-ExecutablePermission {
	param(
		[Parameter(Mandatory)]
		[string]$Path
	)

	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
		throw "未找到应用程序可执行文件：$Path"
	}

	Invoke-NativeCommand -Name "chmod" -Arguments @("+x", $Path)
}

if (-not $IsMacOS) {
	throw "package-mac.ps1 必须在 macOS 上通过 PowerShell 运行。"
}

$hasSigningIdentity = -not [string]::IsNullOrWhiteSpace($SigningIdentity)
$hasNotaryKeychainProfile = -not [string]::IsNullOrWhiteSpace($NotaryKeychainProfile)
if ($hasSigningIdentity -ne $hasNotaryKeychainProfile) {
	throw "macOS 完整分发要求同时提供 Developer ID 签名身份和 notarytool 钥匙串配置名称；两项都留空时生成未签名测试包。"
}

foreach ($command in "uname", "chmod", "hdiutil", "lipo", "xattr", "ln", "SetFile", "ditto") {
	Test-RequiredCommand -Name $command
}
if ($hasSigningIdentity) {
	foreach ($command in "codesign", "xcrun") {
		Test-RequiredCommand -Name $command
	}
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$machineArchitecture = (& uname -m).Trim()
	$RuntimeIdentifier = switch ($machineArchitecture) {
		"arm64" { "osx-arm64"; break }
		"x86_64" { "osx-x64"; break }
		default { throw "不支持的 macOS 处理器架构：$machineArchitecture" }
	}
}

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Split-Path -Parent $scriptsDirectory } else { Split-Path -Parent ([System.IO.Path]::GetFullPath($SourceDirectory)) }
$RootDirectory = if ([string]::IsNullOrWhiteSpace($RootDirectory)) { Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $RuntimeIdentifier -RepositoryRoot $repoRoot } else { $RootDirectory }
$iconFile = Join-Path $PSScriptRoot "warrenq.icns"

$usingDefaultPublishDirectory = [string]::IsNullOrWhiteSpace($PublishDirectory)
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
	$PublishDirectory = Join-Path $RootDirectory "StagingRelease"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
	$OutputDirectory = Join-Path $RootDirectory "output"
}

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
	if ($usingDefaultPublishDirectory) {
		throw "未找到暂存发布目录：$PublishDirectory。请先执行 ./publish-mac.ps1，或执行 ./mac-publish&upgrade&package.ps1 完成发布、升级包和安装包流程。"
	}
	throw "未找到 macOS 发布目录：$PublishDirectory"
}
if (-not (Test-Path -LiteralPath $iconFile -PathType Leaf)) {
	throw "未找到 macOS 应用图标：$iconFile"
}

$mainExecutableName = "WarrenqF1.Desktop"
$applicationName = "Warrenq F1"
$mainExecutable = Join-Path $PublishDirectory $mainExecutableName
if (-not (Test-Path -LiteralPath $mainExecutable -PathType Leaf)) {
	throw "发布目录中未找到主程序：$mainExecutable"
}

$manifestFile = Join-Path $PublishDirectory "generalupdate.manifest.json"
if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) {
	throw "未找到发布清单：$manifestFile"
}
$publishedManifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json -ErrorAction Stop
$clientVersionProperty = $publishedManifest.PSObject.Properties["clientVersion"]
if ($null -eq $clientVersionProperty -or [string]::IsNullOrWhiteSpace([string]$clientVersionProperty.Value)) {
	throw "发布清单未包含有效的 clientVersion：$manifestFile"
}
$Version = ([string]$clientVersionProperty.Value).Trim()

$expectedArchitecture = switch ($RuntimeIdentifier) {
	"osx-arm64" { "arm64"; break }
	"osx-x64" { "x86_64"; break }
}
$architectures = (& lipo -archs $mainExecutable).Trim().Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)
if ($LASTEXITCODE -ne 0 -or $architectures -notcontains $expectedArchitecture) {
	throw "主程序不支持目标架构 $expectedArchitecture：$mainExecutable"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stagingDirectory = Join-Path $OutputDirectory (".mac-package-" + [guid]::NewGuid().ToString("N"))
$appBundle = Join-Path $stagingDirectory "$applicationName.app"
$macOsDirectory = Join-Path $appBundle "Contents/MacOS"
$resourcesDirectory = Join-Path $appBundle "Contents/Resources"
$dmgContentsDirectory = Join-Path $stagingDirectory "dmg-contents"
$dmgFile = Join-Path $OutputDirectory ("WarrenqF1-$Version-$RuntimeIdentifier.dmg")
$writableDmgFile = Join-Path $stagingDirectory "WarrenqF1.rw.dmg"
$mountPoint = Join-Path $stagingDirectory "mount"
$dmgAttached = $false

try {
	New-Item -ItemType Directory -Path $macOsDirectory, $resourcesDirectory, $dmgContentsDirectory -Force | Out-Null
	Get-ChildItem -LiteralPath $PublishDirectory -Force |
		Copy-Item -Destination $macOsDirectory -Recurse -Force
	Move-MacOfficeResources -MacOsDirectory $macOsDirectory -ResourcesDirectory $resourcesDirectory
	Copy-Item -LiteralPath $iconFile -Destination (Join-Path $resourcesDirectory "warrenq.icns") -Force

	$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleDevelopmentRegion</key>
	<string>zh_CN</string>
	<key>CFBundleDisplayName</key>
	<string>$applicationName</string>
	<key>CFBundleExecutable</key>
	<string>$mainExecutableName</string>
	<key>CFBundleIconFile</key>
	<string>warrenq.icns</string>
	<key>CFBundleIdentifier</key>
	<string>$BundleIdentifier</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>CFBundleName</key>
	<string>$applicationName</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleShortVersionString</key>
	<string>$Version</string>
	<key>CFBundleVersion</key>
	<string>$Version</string>
	<key>LSMinimumSystemVersion</key>
	<string>12.0</string>
	<key>NSAppTransportSecurity</key>
	<dict>
		<key>NSAllowsArbitraryLoadsInWebContent</key>
		<true/>
	</dict>
	<key>NSHighResolutionCapable</key>
	<true/>
</dict>
</plist>
"@
	[System.IO.File]::WriteAllText(
		(Join-Path $appBundle "Contents/Info.plist"),
		$infoPlist,
		[System.Text.UTF8Encoding]::new($false))

	foreach ($relativeExecutablePath in @(
		$mainExecutableName,
		"update/WarrenqF1.Updater",
		"WarrenqAIEngine/Launcher/WarrenqAILauncher",
		"WarrenqAIEngine/7zz",
		"WarrenqAIEngine/node/node"
	)) {
		Set-ExecutablePermission -Path (Join-Path $macOsDirectory $relativeExecutablePath)
	}

	Invoke-NativeCommand -Name "xattr" -Arguments @("-cr", $appBundle)

	if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) {
		Test-RequiredCommand -Name "codesign"
		Write-Host "使用 Developer ID 身份签名 macOS 应用：$SigningIdentity"
		Invoke-NativeCommand -Name "codesign" -Arguments @("--force", "--deep", "--options", "runtime", "--timestamp", "--sign", $SigningIdentity, $appBundle)
		Invoke-NativeCommand -Name "codesign" -Arguments @("--verify", "--deep", "--strict", $appBundle)

		$notarizationArchive = Join-Path $stagingDirectory "${applicationName}-notarization.zip"
		Invoke-NativeCommand -Name "ditto" -Arguments @("-c", "-k", "--keepParent", $appBundle, $notarizationArchive)
		Submit-ForNotarization -Path $notarizationArchive -Description "macOS 应用"
		Invoke-NativeCommand -Name "xcrun" -Arguments @("stapler", "staple", $appBundle)
		Invoke-NativeCommand -Name "xcrun" -Arguments @("stapler", "validate", $appBundle)
		Invoke-NativeCommand -Name "codesign" -Arguments @("--verify", "--deep", "--strict", $appBundle)
	}

	Invoke-NativeCommand -Name "ditto" -Arguments @($appBundle, (Join-Path $dmgContentsDirectory "$applicationName.app"))
	Invoke-NativeCommand -Name "ln" -Arguments @("-s", "/Applications", (Join-Path $dmgContentsDirectory "Applications"))
	Copy-Item -LiteralPath $iconFile -Destination (Join-Path $dmgContentsDirectory ".VolumeIcon.icns") -Force

	if (Test-Path -LiteralPath $dmgFile -PathType Leaf) {
		Remove-Item -LiteralPath $dmgFile -Force
	}
	Invoke-NativeCommand -Name "hdiutil" -Arguments @("create", "-volname", $applicationName, "-srcfolder", $dmgContentsDirectory, "-ov", "-format", "UDRW", $writableDmgFile)
	New-Item -ItemType Directory -Path $mountPoint -Force | Out-Null
	try {
		Invoke-NativeCommand -Name "hdiutil" -Arguments @("attach", "-nobrowse", "-noverify", "-noautoopen", "-readwrite", "-mountpoint", $mountPoint, $writableDmgFile)
		$dmgAttached = $true
		$volumeIcon = Join-Path $mountPoint ".VolumeIcon.icns"
		Invoke-NativeCommand -Name "SetFile" -Arguments @("-a", "C", $mountPoint)
		Invoke-NativeCommand -Name "SetFile" -Arguments @("-a", "V", $volumeIcon)
	}
	finally {
		if ($dmgAttached) {
			Invoke-NativeCommand -Name "hdiutil" -Arguments @("detach", $mountPoint)
			$dmgAttached = $false
		}
	}
	Invoke-NativeCommand -Name "hdiutil" -Arguments @("convert", $writableDmgFile, "-format", "UDZO", "-o", $dmgFile)
	if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) {
		$diskImageIdentifier = "$BundleIdentifier.dmg"
		Write-Host "使用 Developer ID 身份签名 DMG：$SigningIdentity"
		Invoke-NativeCommand -Name "codesign" -Arguments @("--force", "--timestamp", "--sign", $SigningIdentity, "--identifier", $diskImageIdentifier, $dmgFile)
		Invoke-NativeCommand -Name "codesign" -Arguments @("--verify", "--strict", $dmgFile)
		Submit-ForNotarization -Path $dmgFile -Description "macOS DMG"
		Invoke-NativeCommand -Name "xcrun" -Arguments @("stapler", "staple", $dmgFile)
		Invoke-NativeCommand -Name "xcrun" -Arguments @("stapler", "validate", $dmgFile)
		Invoke-NativeCommand -Name "codesign" -Arguments @("--verify", "--strict", $dmgFile)
	}
}
finally {
	if ($dmgAttached) {
		Invoke-NativeCommand -Name "hdiutil" -Arguments @("detach", $mountPoint)
	}
	if (Test-Path -LiteralPath $stagingDirectory) {
		Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
	}
}

if (-not (Test-Path -LiteralPath $dmgFile -PathType Leaf)) {
	throw "未生成 macOS 安装包：$dmgFile"
}

Write-Host "macOS 安装包生成完成：$dmgFile"
