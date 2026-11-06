param(
	[ValidateSet("linux-arm", "linux-arm64")]
	[string]$RuntimeIdentifier = "linux-arm64",
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[bool]$DownloadOpenClaw = $true,
	[bool]$DownloadNodeModules = $true
)

$ErrorActionPreference = "Stop"
if ($RuntimeIdentifier -eq "linux-arm") { throw "Linux 打包脚本仅支持 64 位架构，请使用 linux-arm64 或 publish-linux.ps1 发布 linux-x64。" }
$parameters = @{ RuntimeIdentifier = $RuntimeIdentifier; DownloadOpenClaw = $DownloadOpenClaw; DownloadNodeModules = $DownloadNodeModules }
if (-not [string]::IsNullOrWhiteSpace($RootDirectory)) { $parameters.RootDirectory = $RootDirectory }
if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) { $parameters.SourceDirectory = $SourceDirectory }
& (Join-Path $PSScriptRoot "publish-linux.ps1") @parameters
