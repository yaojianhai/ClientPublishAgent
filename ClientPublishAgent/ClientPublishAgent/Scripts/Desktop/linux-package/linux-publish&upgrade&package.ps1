param(
	[ValidateSet("linux-arm64", "linux-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[string]$OutputDirectory,
	[ValidateSet("upgrade", "installer", "upgrade-installer")]
	[string]$PackageType = "upgrade-installer",
	[ValidatePattern('^[A-Za-z][A-Za-z0-9.-]*$')]
	[string]$BundleIdentifier = "com.gildata.warrenqf1",
	[bool]$DownloadOpenClaw = $true,
	[bool]$DownloadNodeModules = $true
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "linux-common.ps1")
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
$RuntimeIdentifier = Get-LinuxRuntimeIdentifier -RuntimeIdentifier $RuntimeIdentifier
$CurrentVersion = Read-LinuxSemanticVersion -Prompt "请输入当前版本号" -Value $CurrentVersion
$NextVersion = Read-LinuxSemanticVersion -Prompt "请输入下一版本号" -Value $NextVersion
$publishScript = Join-Path $PSScriptRoot "publish-linux.ps1"
$upgradeScript = Join-Path $PSScriptRoot "linux-upgrade&package.ps1"
$publishParameters = @{ DownloadOpenClaw = $DownloadOpenClaw; DownloadNodeModules = $DownloadNodeModules }
$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion }
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Split-Path -Parent $scriptsDirectory } else { Split-Path -Parent ([System.IO.Path]::GetFullPath($SourceDirectory)) }
$resolvedRootDirectory = Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $RuntimeIdentifier -RepositoryRoot $repoRoot -RootDirectory $RootDirectory
$publishParameters.RootDirectory = $resolvedRootDirectory
$upgradeParameters.RootDirectory = $resolvedRootDirectory
if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) { $publishParameters.SourceDirectory = $SourceDirectory }
$upgradeParameters.BundleIdentifier = $BundleIdentifier
if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) { $upgradeParameters.OutputDirectory = $OutputDirectory }
if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) { $upgradeParameters.SourceDirectory = $SourceDirectory }
if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$publishParameters.RuntimeIdentifier = $RuntimeIdentifier
	$upgradeParameters.RuntimeIdentifier = $RuntimeIdentifier
}
if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
& $publishScript @publishParameters
if ($PackageType -eq "upgrade-installer") {
	& $upgradeScript @upgradeParameters
} elseif ($PackageType -eq "upgrade") {
	$upgradeOnlyParameters = @{ RuntimeIdentifier = $RuntimeIdentifier; CurrentVersion = $CurrentVersion; NextVersion = $NextVersion; RootDirectory = $resolvedRootDirectory }
	if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) { $upgradeOnlyParameters.SourceDirectory = $SourceDirectory }
	if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeOnlyParameters.OssHost = $OssHost }
	& (Join-Path $PSScriptRoot "upgrade-linux.ps1") @upgradeOnlyParameters
} else {
	$stagingDirectory = Join-Path $resolvedRootDirectory "StagingRelease"
	$packageParameters = @{ RuntimeIdentifier = $RuntimeIdentifier; RootDirectory = $resolvedRootDirectory; PublishDirectory = $stagingDirectory; ClientVersion = $NextVersion; SourceDirectory = $SourceDirectory }
	if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) { $packageParameters.OutputDirectory = $OutputDirectory }
	& (Join-Path $PSScriptRoot "package-linux.ps1") @packageParameters
}
if ($PackageType -eq "installer") {
	& (Join-Path $PSScriptRoot "backup-linux-staging.ps1") -RootDirectory $resolvedRootDirectory -Version $NextVersion
}
