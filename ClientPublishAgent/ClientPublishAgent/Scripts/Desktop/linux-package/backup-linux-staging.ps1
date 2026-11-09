param(
	[Parameter(Mandatory)][string]$RootDirectory,
	[Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "linux-common.ps1")
if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
	Invoke-LinuxScriptInWsl -ScriptPath $PSCommandPath -Parameters $PSBoundParameters
	return
}
. (Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1")
Backup-StagingRelease -StagingDirectory (Join-Path $RootDirectory "StagingRelease") -Version $Version
