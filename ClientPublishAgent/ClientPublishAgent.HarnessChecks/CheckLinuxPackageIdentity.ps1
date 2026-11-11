$ErrorActionPreference = "Stop"
if (-not $IsLinux) { throw "Run this check with PowerShell 7 on Linux or WSL Debian." }
$linuxScripts = Join-Path (Split-Path -Parent $PSScriptRoot) "ClientPublishAgent/Scripts/Desktop/linux-package"
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("linux-package-identity-" + [guid]::NewGuid().ToString("N"))
$source = Join-Path $root "src"
$publish = Join-Path $root "publish"
$output = Join-Path $root "output"
$bundle = "com.example.warrenq.identity"
$utf8 = [System.Text.UTF8Encoding]::new($false)
function Assert-Equal($Actual, $Expected, [string]$Message) {
	if ($Actual -cne $Expected) { throw "${Message}: expected '$Expected', got '$Actual'." }
}
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
	& $Command @Arguments
	if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}
try {
	$iconDirectory = Join-Path $source "WarrenqF1/Assets/Images/Logos"
	New-Item -ItemType Directory -Path $iconDirectory, $publish -Force | Out-Null
	[System.IO.File]::WriteAllBytes((Join-Path $iconDirectory "warrenq.png"), [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aV1sAAAAASUVORK5CYII="))
	$probeSource = Join-Path $root "probe.c"
	[System.IO.File]::WriteAllText($probeSource, @'
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
int main(int argc, char **argv) {
	const char *package = getenv("DEB_PACKAGE_NAME");
	const char *desktop = getenv("GIO_LAUNCHED_DESKTOP_FILE");
	if (!package || !desktop) return 2;
	printf("PACKAGE=%s\nDESKTOP=%s\n", package, desktop);
	for (int i = 1; i < argc; ++i) printf("ARG=%s\n", argv[i]);
	fflush(stdout);
	execl("/bin/sh", "sh", "-c", "printf 'CHILD_PACKAGE=%s\nCHILD_DESKTOP=%s\n' \"$DEB_PACKAGE_NAME\" \"$GIO_LAUNCHED_DESKTOP_FILE\"", (char *)0);
	return 3;
}
'@, $utf8)
	$probe = Join-Path $root "probe"
	Invoke-Checked gcc @("-Wall", "-Wextra", "-Werror", $probeSource, "-o", $probe)
	$hostRuntime = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -eq "Arm64") { "linux-arm64" } else { "linux-x64" }
	foreach ($runtime in @($hostRuntime, (@("linux-x64", "linux-arm64") | Where-Object { $_ -ne $hostRuntime }))) {
		foreach ($relative in "WarrenqF1.Desktop", "update/WarrenqF1.Updater", "WarrenqAIEngine/Launcher/WarrenqAILauncher", "WarrenqAIEngine/7zz", "WarrenqAIEngine/node/node") {
			$path = Join-Path $publish $relative
			New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
			$bytes = [System.IO.File]::ReadAllBytes($probe)
			# Cross-architecture fixtures validate packaging only; only the host ELF is executed.
			$bytes[18] = if ($runtime -eq "linux-arm64") { 183 } else { 62 }
			[System.IO.File]::WriteAllBytes($path, $bytes)
		}
		& (Join-Path $linuxScripts "package-linux.ps1") -RuntimeIdentifier $runtime -RootDirectory $root -SourceDirectory $source -PublishDirectory $publish -OutputDirectory $output -ClientVersion 1.2.3 -BundleIdentifier $bundle
		$architecture = if ($runtime -eq "linux-arm64") { "arm64" } else { "amd64" }
		$deb = Join-Path $output "warrenqf1_1.2.3_${architecture}.deb"
		Assert-Equal ((Invoke-Checked dpkg-deb @("--field", $deb, "Package")) -join "") "warrenqf1" "Debian identity"
		Assert-Equal ((Invoke-Checked dpkg-deb @("--field", $deb, "Architecture")) -join "") $architecture "Debian architecture"
		$extracted = Join-Path $root $runtime
		Invoke-Checked dpkg-deb @("--raw-extract", $deb, $extracted)
		$link = Get-Item -LiteralPath (Join-Path $extracted "usr/bin/warrenqf1") -Force
		Assert-Equal $link.LinkTarget "/opt/WarrenqF1/warrenqf1" "Command symlink"
		$launcherPath = Join-Path $extracted "opt/WarrenqF1/warrenqf1"
		$launcher = [System.IO.File]::ReadAllText($launcherPath)
		foreach ($desktopName in "$bundle.desktop", "WarrenqF1.Updater.desktop") {
			$entry = Get-Content -LiteralPath (Join-Path $extracted "usr/share/applications/$desktopName")
			Assert-Equal ($entry | Where-Object { $_ -clike "Exec=*" }) "Exec=/opt/WarrenqF1/warrenqf1" "Desktop entry"
			Assert-Equal ($entry | Where-Object { $_ -clike "TryExec=*" }) "TryExec=/opt/WarrenqF1/WarrenqF1.Desktop" "Desktop executable check"
		}
		foreach ($script in $launcherPath, (Join-Path $extracted "DEBIAN/postinst"), (Join-Path $extracted "DEBIAN/postrm")) {
			Invoke-Checked sh @("-n", $script)
			Invoke-Checked test @("-x", $script)
		}
		$mockBin = Join-Path $root "mock-bin"
		$cacheLog = Join-Path $root "cache-$runtime.log"
		New-Item -ItemType Directory -Path $mockBin -Force | Out-Null
		$cacheTool = Join-Path $mockBin "update-desktop-database"
		[System.IO.File]::WriteAllText($cacheTool, "#!/bin/sh`nprintf '%s\n' `"`$*`" >> '$cacheLog'`nexit 0`n", $utf8)
		Invoke-Checked chmod @("+x", $cacheTool)
		$previousPath = $env:PATH
		try {
			$env:PATH = "$mockBin`:/usr/bin:/bin"
			$postinst = Join-Path $extracted "DEBIAN/postinst"
			$postrm = Join-Path $extracted "DEBIAN/postrm"
			Invoke-Checked /bin/sh @($postinst, "configure", "1.0.0") | Out-Null
			Invoke-Checked /bin/sh @($postinst, "abort-upgrade") | Out-Null
			foreach ($operation in "remove", "purge", "upgrade", "failed-upgrade") {
				Invoke-Checked /bin/sh @($postrm, $operation) | Out-Null
			}
			Assert-Equal ((Get-Content -LiteralPath $cacheLog) -join "|") "/usr/share/applications|/usr/share/applications|/usr/share/applications" "Cache refresh lifecycle"
			[System.IO.File]::WriteAllText($cacheTool, "#!/bin/sh`nexit 23`n", $utf8)
			foreach ($hook in @(@($postinst, "configure"), @($postrm, "remove"))) {
				& /bin/sh @hook
				Assert-Equal $LASTEXITCODE 23 "Cache refresh failure propagation"
			}
			$env:PATH = Join-Path $root "missing-tools"
			$warning = @(& /bin/sh $postinst configure 2>&1)
			Assert-Equal $LASTEXITCODE 0 "Optional desktop cache utility"
			if (-not ($warning -match "desktop-file-utils is unavailable")) { throw "Missing cache utility warning was not reported." }

			[System.IO.File]::WriteAllText($cacheTool, "#!/bin/sh`nprintf '%s\n' `"`$*`" >> '$cacheLog'`nexit 0`n", $utf8)
			$desktopFixture = Join-Path $root "desktop-user-$runtime"
			$fixtureDesktop = Join-Path $desktopFixture "桌面"
			New-Item -ItemType Directory -Path $fixtureDesktop -Force | Out-Null
			$desktopMockBin = Join-Path $root "desktop-mock-bin"
			New-Item -ItemType Directory -Path $desktopMockBin -Force | Out-Null
			$env:PATH = "$desktopMockBin`:/usr/bin:/bin"
			$desktopScripts = @{
				id = "#!/bin/sh`nprintf '0\n'`n"
				getent = "#!/bin/sh`nprintf '%s\n' 'fixture-user:x:1000:1000::${desktopFixture}:/bin/sh'`n"
				runuser = "#!/bin/sh`ncase `"`$*`" in *xdg-user-dir*) printf '%s\n' '${fixtureDesktop}';; *) while [ `"`$1`" != '--' ]; do shift; done; shift; exec `"`$@`";; esac`n"
				install = "#!/bin/sh`nshift 3`ncp `"`$1`" `"`$2`"`nchmod 755 `"`$2`"`n"
			}
			foreach ($name in $desktopScripts.Keys) {
				$path = Join-Path $desktopMockBin $name
				[System.IO.File]::WriteAllText($path, $desktopScripts[$name], $utf8)
				Invoke-Checked chmod @("+x", $path)
			}
			$desktopPostinst = Join-Path $root "postinst-desktop-test"
			$hookContents = [System.IO.File]::ReadAllText($postinst).Replace("/usr/share/applications", "$extracted/usr/share/applications")
			[System.IO.File]::WriteAllText($desktopPostinst, $hookContents, $utf8)
			$shortcut = Join-Path $fixtureDesktop "$bundle.desktop"
			$env:PATH = "$desktopMockBin`:$mockBin`:/usr/bin:/bin"
			Invoke-Checked /bin/sh @($desktopPostinst, "configure") | Out-Null
			if (-not (Test-Path -LiteralPath $shortcut -PathType Leaf)) { throw "Post-install did not create the desktop shortcut." }
			Invoke-Checked test @("-x", $shortcut)
			$shortcutContents = Get-Content -LiteralPath $shortcut
			if ($shortcutContents -notcontains "Exec=/opt/WarrenqF1/warrenqf1" -or $shortcutContents -notcontains "X-WarrenqF1-Package=warrenqf1") { throw "Desktop shortcut content is incomplete." }
			[System.IO.File]::WriteAllText($shortcut, "User-owned shortcut", $utf8)
			Invoke-Checked /bin/sh @($desktopPostinst, "configure") | Out-Null
			Assert-Equal ([System.IO.File]::ReadAllText($shortcut)) "User-owned shortcut" "Preserve existing desktop shortcut"
			Remove-Item -LiteralPath $shortcut -Force
			Invoke-Checked /bin/sh @($desktopPostinst, "configure") | Out-Null
			Invoke-Checked /bin/sh @($postrm, "remove") | Out-Null
			if (Test-Path -LiteralPath $shortcut) { throw "Package-managed desktop shortcut was not removed." }
			[System.IO.File]::WriteAllText($shortcut, "User-owned shortcut", $utf8)
			Invoke-Checked /bin/sh @($postrm, "remove") | Out-Null
			Assert-Equal ([System.IO.File]::ReadAllText($shortcut)) "User-owned shortcut" "Preserve unrelated shortcut on removal"
		} finally {
			$env:PATH = $previousPath
		}
		if ($runtime -ne $hostRuntime) { continue }
		# Redirect the package source only, without installing into the host system.
		$testLauncher = Join-Path $root "launcher.sh"
		[System.IO.File]::WriteAllText($testLauncher, $launcher.Replace("source_dir=/opt/WarrenqF1", "source_dir='$extracted/opt/WarrenqF1'"), $utf8)
		$previousDataHome = $env:XDG_DATA_HOME
		$previousPackage = $env:DEB_PACKAGE_NAME
		$previousDesktop = $env:GIO_LAUNCHED_DESKTOP_FILE
		try {
			$env:XDG_DATA_HOME = Join-Path $root "user data"
			$env:DEB_PACKAGE_NAME = "unrelated-package"
			$env:GIO_LAUNCHED_DESKTOP_FILE = "/unrelated.desktop"
			$lines = @(Invoke-Checked sh @($testLauncher, "argument with spaces", "--download"))
			Assert-Equal ($lines -join "`n") "PACKAGE=warrenqf1`nDESKTOP=/usr/share/applications/$bundle.desktop`nARG=argument with spaces`nARG=--download`nCHILD_PACKAGE=warrenqf1`nCHILD_DESKTOP=/usr/share/applications/$bundle.desktop" "Parent and child identity and arguments"
			$runtimeParent = Join-Path $env:XDG_DATA_HOME "WarrenqF1"
			$application = Join-Path $runtimeParent "application"
			Assert-Equal (Get-Content -LiteralPath (Join-Path $runtimeParent ".deb-version") -Raw).Trim() "1.2.3" "First-run package marker"
			$sentinel = Join-Path $application "self-update.txt"
			[System.IO.File]::WriteAllText($sentinel, "keep newer runtime", $utf8)
			[System.IO.File]::WriteAllText((Join-Path $application "generalupdate.manifest.json"), '{"clientVersion":"2.0.0"}', $utf8)
			[System.IO.File]::WriteAllText((Join-Path $runtimeParent ".deb-version"), "1.0.0", $utf8)
			Invoke-Checked sh @($testLauncher) | Out-Null
			Assert-Equal ([System.IO.File]::ReadAllText($sentinel)) "keep newer runtime" "Preserve newer self-update"
			[System.IO.File]::WriteAllText((Join-Path $application "generalupdate.manifest.json"), '{"clientVersion":"1.0.0"}', $utf8)
			[System.IO.File]::WriteAllText((Join-Path $runtimeParent ".deb-version"), "1.0.0", $utf8)
			Invoke-Checked sh @($testLauncher) | Out-Null
			if (Test-Path -LiteralPath $sentinel) { throw "Older runtime was not refreshed from the package." }
			Assert-Equal ((Get-Content -LiteralPath (Join-Path $application "generalupdate.manifest.json") -Raw | ConvertFrom-Json).clientVersion) "1.2.3" "Refresh older runtime"
		} finally {
			$env:XDG_DATA_HOME = $previousDataHome
			$env:DEB_PACKAGE_NAME = $previousPackage
			$env:GIO_LAUNCHED_DESKTOP_FILE = $previousDesktop
		}
	}
	Write-Host "Linux package identity checks passed for amd64 and arm64; host launch and self-update checks passed."
} finally {
	if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
