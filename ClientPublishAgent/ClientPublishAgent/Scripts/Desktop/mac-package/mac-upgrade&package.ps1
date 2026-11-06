param(
	[ValidateSet("osx-arm64", "osx-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[string]$OutputDirectory,
	[string]$SigningIdentity,
	[string]$BundleIdentifier = "com.gildata.warrenqf1"
)

$ErrorActionPreference = "Stop"
$upgradeScript = Join-Path $PSScriptRoot "upgrade-mac.ps1"
$packageScript = Join-Path $PSScriptRoot "package-mac.ps1"
$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion }
$packageParameters = @{ BundleIdentifier = $BundleIdentifier }
if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$upgradeParameters.RuntimeIdentifier = $RuntimeIdentifier
	$packageParameters.RuntimeIdentifier = $RuntimeIdentifier
}
if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) { $packageParameters.OutputDirectory = $OutputDirectory }
if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) { $packageParameters.SigningIdentity = $SigningIdentity }
& $upgradeScript @upgradeParameters
& $packageScript @packageParameters