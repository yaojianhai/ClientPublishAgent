param(
	[ValidateSet("osx-arm64", "osx-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[string]$OutputDirectory,
	[string]$SigningIdentity = $env:APPLE_SIGNING_IDENTITY,
	[string]$NotaryKeychainProfile = $env:APPLE_NOTARY_PROFILE,
	[string]$BundleIdentifier = "com.gildata.warrenqf1"
)

$ErrorActionPreference = "Stop"
$hasSigningIdentity = -not [string]::IsNullOrWhiteSpace($SigningIdentity)
$hasNotaryKeychainProfile = -not [string]::IsNullOrWhiteSpace($NotaryKeychainProfile)
if ($hasSigningIdentity -ne $hasNotaryKeychainProfile) {
	throw "macOS 完整分发要求同时提供 Developer ID 签名身份和 notarytool 钥匙串配置名称；两项都留空时生成未签名测试包。"
}
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
$packageParameters.SigningIdentity = $SigningIdentity
$packageParameters.NotaryKeychainProfile = $NotaryKeychainProfile
& $upgradeScript @upgradeParameters
& $packageScript @packageParameters