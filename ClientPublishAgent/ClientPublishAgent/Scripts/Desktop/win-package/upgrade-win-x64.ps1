param(
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost = "https://cloud.gildata.com/gil/upgrade/WarrenQ/WarrenqF1/win-x64",
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[bool]$BackupStagingRelease = $true
)

$ErrorActionPreference = "Stop"
$configScript = Join-Path $PSScriptRoot "publish-config.ps1"
. $configScript
$backupScript = Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1"
. $backupScript
# 必须与 publish-win-x64.ps1 和安装包脚本使用同一发布根目录。
$rootDirectory = if ([string]::IsNullOrWhiteSpace($RootDirectory)) { Get-WindowsPublishRootDirectory } else { [System.IO.Path]::GetFullPath($RootDirectory) }
$stagingDirectory = Join-Path $rootDirectory "StagingRelease"
$oldDirectory = Join-Path $rootDirectory "OldRelease"
$patchOutputDirectory = Join-Path $rootDirectory "OutputPatchPackage"
$patchTool = Join-Path $PSScriptRoot "GeneralUpdate.Tools.Cmd.exe"

function Read-SemanticVersion {
	param(
		[Parameter(Mandatory)][string]$Prompt,
		[string]$Value
	)

	do {
		if ([string]::IsNullOrWhiteSpace($Value)) { $Value = (Read-Host $Prompt).Trim() } else { $Value = $Value.Trim() }
		if ($Value -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { return $Value }
		Write-Warning "版本号格式无效：$Value。请输入 Semantic Versioning 格式，例如 1.2.3。"
		$Value = $null
	} while ($true)
}

$CurrentVersion = Read-SemanticVersion -Prompt "请输入当前版本号" -Value $CurrentVersion
$NextVersion = Read-SemanticVersion -Prompt "请输入下一版本号" -Value $NextVersion
[version]$currentVersionValue = $null
[version]$nextVersionValue = $null
if (-not [version]::TryParse($CurrentVersion, [ref]$currentVersionValue) -or -not [version]::TryParse($NextVersion, [ref]$nextVersionValue)) {
	throw "版本号格式无效。当前版本：$CurrentVersion；下一版本：$NextVersion"
}
if ($nextVersionValue -lt $currentVersionValue) { throw "下一版本号不能小于当前版本号。" }
if (-not (Test-Path -LiteralPath $stagingDirectory -PathType Container)) { throw "未找到暂存发布目录：$stagingDirectory" }
$manifest = [ordered]@{
	mainAppName = "WarrenqF1.Desktop.exe"
	clientVersion = $NextVersion
	appType = "OssClient"
	updateAppName = "WarrenqF1.Updater.exe"
	updatePath = "update/"
}
[System.IO.File]::WriteAllText((Join-Path $stagingDirectory "generalupdate.manifest.json"), ($manifest | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))

function Save-OldRelease {
	param(
		[Parameter(Mandatory)][string]$SourceDirectory,
		[Parameter(Mandatory)][string]$DestinationDirectory
	)

	$destinationParent = Split-Path -Parent $DestinationDirectory
	if (Test-Path -LiteralPath $DestinationDirectory) {
		if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) { throw "旧版本路径不是目录：$DestinationDirectory" }
		Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
	}
	New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
	Copy-Item -LiteralPath $SourceDirectory -Destination $DestinationDirectory -Recurse -Force
}

if ($nextVersionValue -le $currentVersionValue) {
	Write-Host "下一版本号不大于当前版本号，跳过升级包生成。"
	Save-OldRelease -SourceDirectory $stagingDirectory -DestinationDirectory $oldDirectory
	Write-Host "已将最新暂存发布保存为 OldRelease：$oldDirectory"
	if ($BackupStagingRelease) { Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $NextVersion }
	return
}

$oldReleaseHasContent = Test-Path -LiteralPath $oldDirectory -PathType Container
if ($oldReleaseHasContent) { $oldReleaseHasContent = @(Get-ChildItem -LiteralPath $oldDirectory -Force).Count -gt 0 }
if ($oldReleaseHasContent) {
	if (-not (Test-Path -LiteralPath $patchTool -PathType Leaf)) { throw "未找到 Windows 差分工具：$patchTool" }
	if (Test-Path -LiteralPath $patchOutputDirectory) {
		Remove-Item -LiteralPath $patchOutputDirectory -Recurse -Force
	}
	New-Item -ItemType Directory -Path $patchOutputDirectory -Force | Out-Null
	& $patchTool `
		--old-dir $oldDirectory `
		--new-dir $stagingDirectory `
		--output-dir $patchOutputDirectory `
		--host $OssHost `
		--current-version $CurrentVersion `
		--next-version $NextVersion `
		--suspicious-files include
	if ($LASTEXITCODE -ne 0) { throw "GeneralUpdate.Tools.Cmd 执行失败，退出代码：$LASTEXITCODE" }
} else { Write-Host "OldRelease 不存在或为空，首次发布跳过差分包生成。" }

Save-OldRelease -SourceDirectory $stagingDirectory -DestinationDirectory $oldDirectory
Write-Host "Windows 升级包处理完成：$oldDirectory"
if ($BackupStagingRelease) { Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $NextVersion }