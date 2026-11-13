param(
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[ValidateSet("win-x64")]
	[string]$RuntimeIdentifier = "win-x64",
	[ValidateSet("upgrade", "installer", "upgrade-installer")]
	[string]$PackageType = "upgrade-installer",
	[ValidateSet("true", "false")]
	[string]$PublishAot = "true"
)

$ErrorActionPreference = "Stop"
$publishAotEnabled = $PublishAot -eq "true"
$configScript = Join-Path $PSScriptRoot "publish-config.ps1"
. $configScript
$backupScript = Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1"
. $backupScript
$publishScript = Join-Path $PSScriptRoot "publish-win-x64.ps1"
$upgradeScript = Join-Path $PSScriptRoot "upgrade-win-x64.ps1"
$installerScript = Join-Path $PSScriptRoot "WQ_打包.bat"
$stagingDirectory = Join-Path (Get-WindowsPublishRootDirectory) "StagingRelease"

& $publishScript -PublishAot $publishAotEnabled
if ($PackageType -in "upgrade", "upgrade-installer") {
	$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion; RootDirectory = (Get-WindowsPublishRootDirectory); BackupStagingRelease = $false }
	if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
	& $upgradeScript @upgradeParameters
}
if ($PackageType -eq "installer") {
	$manifest = [ordered]@{ mainAppName = "WarrenqF1.Desktop.exe"; clientVersion = $NextVersion; appType = "OssClient"; updateAppName = "WarrenqF1.Updater.exe"; updatePath = "update/" }
	[System.IO.File]::WriteAllText((Join-Path $stagingDirectory "generalupdate.manifest.json"), ($manifest | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
}
if ($PackageType -in "installer", "upgrade-installer") {
	$manifestFile = Join-Path $stagingDirectory "generalupdate.manifest.json"
	if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "未找到升级发布清单：$manifestFile" }
	$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json -ErrorAction Stop
	$clientVersion = ([string]$manifest.clientVersion).Trim()
	if ([string]::IsNullOrWhiteSpace($clientVersion)) { throw "升级发布清单未包含有效的 clientVersion：$manifestFile" }
	$installerCommand = "call `"$installerScript`" `"$stagingDirectory`" `"$clientVersion`""
	$installerExitCode = Invoke-WindowsBatchWithLegacyEncoding -CommandLine $installerCommand
	if ($installerExitCode -ne 0) { throw "Windows 安装包打包失败，退出代码：$installerExitCode" }
}
$manifestFile = Join-Path $stagingDirectory "generalupdate.manifest.json"
if (Test-Path -LiteralPath $manifestFile -PathType Leaf) {
	$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json -ErrorAction Stop
	$clientVersion = ([string]$manifest.clientVersion).Trim()
	if (-not [string]::IsNullOrWhiteSpace($clientVersion)) { Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $clientVersion }
}
