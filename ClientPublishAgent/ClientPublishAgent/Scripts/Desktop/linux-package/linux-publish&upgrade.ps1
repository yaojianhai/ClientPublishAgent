param(
	[ValidateSet("linux-arm64", "linux-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "linux-common.ps1")
$RuntimeIdentifier = Get-LinuxRuntimeIdentifier -RuntimeIdentifier $RuntimeIdentifier
$CurrentVersion = Read-LinuxSemanticVersion -Prompt "请输入当前版本号" -Value $CurrentVersion
$NextVersion = Read-LinuxSemanticVersion -Prompt "请输入下一版本号" -Value $NextVersion
$publishScript = Join-Path $PSScriptRoot "publish-linux.ps1"
$upgradeScript = Join-Path $PSScriptRoot "upgrade-linux.ps1"
$publishParameters = @{}
$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion }
if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$publishParameters.RuntimeIdentifier = $RuntimeIdentifier
	$upgradeParameters.RuntimeIdentifier = $RuntimeIdentifier
}
if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
& $publishScript @publishParameters
& $upgradeScript @upgradeParameters
