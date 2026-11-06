param(
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost
)

$ErrorActionPreference = "Stop"
$configScript = Join-Path $PSScriptRoot "publish-config.ps1"
. $configScript
$backupScript = Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1"
. $backupScript
$publishScript = Join-Path $PSScriptRoot "publish-win-x64.ps1"
$upgradeScript = Join-Path $PSScriptRoot "upgrade-win-x64.ps1"

$upgradeParameters = @{
	CurrentVersion = $CurrentVersion
	NextVersion = $NextVersion
	RootDirectory = (Get-WindowsPublishRootDirectory)
	BackupStagingRelease = $false
}

if (-not [string]::IsNullOrWhiteSpace($OssHost)) {
	$upgradeParameters.OssHost = $OssHost
}
& $publishScript
if ($LASTEXITCODE -ne 0) {
	throw "Windows 发布失败，退出代码：$LASTEXITCODE"
}

& $upgradeScript @upgradeParameters
if ($LASTEXITCODE -ne 0) {
	throw "Windows 升级处理失败，退出代码：$LASTEXITCODE"
}

Write-Host "Windows 发布和升级处理完成，未执行安装包打包。"
$stagingDirectory = Join-Path (Get-WindowsPublishRootDirectory) "StagingRelease"
$manifestFile = Join-Path $stagingDirectory "generalupdate.manifest.json"
if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "未找到升级发布清单：$manifestFile" }
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json -ErrorAction Stop
$clientVersion = ([string]$manifest.clientVersion).Trim()
if ([string]::IsNullOrWhiteSpace($clientVersion)) { throw "升级发布清单未包含有效的 clientVersion：$manifestFile" }
Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $clientVersion
