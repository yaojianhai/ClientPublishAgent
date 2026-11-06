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
$upgradeScript = Join-Path $PSScriptRoot "upgrade-win-x64.ps1"
$installerScript = Join-Path $PSScriptRoot "WQ_打包.bat"
$stagingDirectory = Join-Path (Get-WindowsPublishRootDirectory) "StagingRelease"

$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion }
$upgradeParameters["BackupStagingRelease"] = $false
if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
& $upgradeScript @upgradeParameters

$manifestFile = Join-Path $stagingDirectory "generalupdate.manifest.json"
if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "未找到升级发布清单：$manifestFile" }
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json -ErrorAction Stop
$clientVersion = ([string]$manifest.clientVersion).Trim()
if ([string]::IsNullOrWhiteSpace($clientVersion)) { throw "升级发布清单未包含有效的 clientVersion：$manifestFile" }
& $env:ComSpec /d /c "call `"$installerScript`" `"$stagingDirectory`" `"$clientVersion`""
if ($LASTEXITCODE -ne 0) { throw "Windows 安装包打包失败，退出代码：$LASTEXITCODE" }
Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $clientVersion
