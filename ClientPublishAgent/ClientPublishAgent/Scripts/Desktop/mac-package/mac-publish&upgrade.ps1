param(
	[ValidateSet("osx-arm64", "osx-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[ValidateSet("true", "false")]
	[string]$PublishAot = "true"
)

$ErrorActionPreference = "Stop"
$publishAotEnabled = $PublishAot -eq "true"
$publishScript = Join-Path $PSScriptRoot "publish-mac.ps1"
$upgradeScript = Join-Path $PSScriptRoot "upgrade-mac.ps1"
$publishParameters = @{ PublishAot = $publishAotEnabled }
$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion }
if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$publishParameters.RuntimeIdentifier = $RuntimeIdentifier
	$upgradeParameters.RuntimeIdentifier = $RuntimeIdentifier
}
if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
& $publishScript @publishParameters
& $upgradeScript @upgradeParameters
