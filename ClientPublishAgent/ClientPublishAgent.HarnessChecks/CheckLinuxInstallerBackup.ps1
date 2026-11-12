$ErrorActionPreference = "Stop"
$desktopScripts = Join-Path (Split-Path -Parent $PSScriptRoot) "ClientPublishAgent/Scripts/Desktop"
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("linux-installer-backup-" + [guid]::NewGuid().ToString("N"))
$desktop = Join-Path $root "Desktop"
$linux = Join-Path $desktop "linux-package"
$publishRoot = Join-Path $root "output"
New-Item -ItemType Directory -Path $linux -Force | Out-Null
foreach ($name in "linux-publish&upgrade&package.ps1", "linux-common.ps1", "backup-linux-staging.ps1") {
	Copy-Item -LiteralPath (Join-Path $desktopScripts "linux-package/$name") -Destination $linux
}
foreach ($name in "publish-root-config.ps1", "backup-staging-release.ps1") {
	Copy-Item -LiteralPath (Join-Path $desktopScripts $name) -Destination $desktop
}
Set-Content -LiteralPath (Join-Path $linux "publish-linux.ps1") -Value 'param($RuntimeIdentifier, $RootDirectory, $SourceDirectory, $DownloadOpenClaw, $DownloadNodeModules) New-Item -ItemType Directory -Path (Join-Path $RootDirectory "StagingRelease") -Force | Out-Null; Set-Content -LiteralPath (Join-Path $RootDirectory "StagingRelease/published.txt") -Value "AOT"' -Encoding utf8
Set-Content -LiteralPath (Join-Path $linux "package-linux.ps1") -Value 'param($RuntimeIdentifier, $RootDirectory, $PublishDirectory, $ClientVersion, $SourceDirectory, $OutputDirectory, $BundleIdentifier) Set-Content -LiteralPath (Join-Path $PublishDirectory "package-completed.txt") -Value $ClientVersion; Set-Content -LiteralPath (Join-Path $PublishDirectory "bundle-identifier.txt") -Value $BundleIdentifier' -Encoding utf8
try {
	& (Join-Path $linux "linux-publish&upgrade&package.ps1") -RuntimeIdentifier linux-x64 -CurrentVersion 1.0.0 -NextVersion 1.0.1 -RootDirectory $publishRoot -PackageType installer -BundleIdentifier "com.example.warrenq"
	$backup = Join-Path $publishRoot "back_1.0.1"
	if ((Get-Content -LiteralPath (Join-Path $backup "published.txt") -Raw).Trim() -ne "AOT") { throw "AOT 发布文件未备份。" }
	if ((Get-Content -LiteralPath (Join-Path $backup "package-completed.txt") -Raw).Trim() -ne "1.0.1") { throw "备份未包含安装包处理后的文件。" }
	if ((Get-Content -LiteralPath (Join-Path $backup "bundle-identifier.txt") -Raw).Trim() -ne "com.example.warrenq") { throw "安装包流程未传递 BundleIdentifier。" }
	if (Test-Path -LiteralPath (Join-Path $publishRoot "OldRelease")) { throw "安装包流程不应修改升级基线。" }
	if ($IsWindows) {
		. (Join-Path $linux "linux-common.ps1")
		$probePath = Join-Path $linux "boolean argument probe.ps1"
		Set-Content -LiteralPath $probePath -Value 'param([bool]$Enabled, [bool]$Disabled, [string]$Message) if (-not $Enabled -or $Disabled -or $Message -cne "path with spaces and apostrophe''s") { throw "WSL argument binding failed." }' -Encoding utf8
		Invoke-LinuxScriptInWsl -ScriptPath $probePath -Parameters ([ordered]@{ Enabled = $true; Disabled = $false; Message = "path with spaces and apostrophe's" })
	}
	Write-Host "Linux 安装包备份检查通过。"
} finally {
	Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction Continue
}
