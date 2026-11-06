param(
	[ValidateSet("osx-arm64", "osx-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[bool]$BackupStagingRelease = $true
)

$ErrorActionPreference = "Stop"
$backupScript = Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1"
. $backupScript
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
$machineArchitecture = (& uname -m).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($machineArchitecture)) {
	throw "无法获取当前系统处理器架构。"
}
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$RuntimeIdentifier = switch ($machineArchitecture) { "arm64" { "osx-arm64"; break } "x86_64" { "osx-x64"; break } default { throw "不支持的 macOS 处理器架构：$machineArchitecture" } }
}

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
$shouldGenerateUpgradePackage = $nextVersionValue -gt $currentVersionValue

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Split-Path -Parent $scriptsDirectory } else { Split-Path -Parent ([System.IO.Path]::GetFullPath($SourceDirectory)) }
$RootDirectory = if ([string]::IsNullOrWhiteSpace($RootDirectory)) { Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $RuntimeIdentifier -RepositoryRoot $repoRoot } else { $RootDirectory }
if ([string]::IsNullOrWhiteSpace($OssHost)) { $OssHost = "http://10.106.1.123/upgrade/WarrenQ/WarrenqF1/$RuntimeIdentifier" }
$stagingDirectory = Join-Path $RootDirectory "StagingRelease"
$oldDirectory = Join-Path $RootDirectory "OldRelease"
$patchOutputDirectory = Join-Path $RootDirectory "OutputPatchPackage"
$toolArchitectureDirectory = switch ($machineArchitecture) {
	"arm64" { "arm64"; break }
	"x86_64" { "x64"; break }
	default { throw "不支持的 macOS 处理器架构：$machineArchitecture" }
}
$patchTool = Join-Path $PSScriptRoot "$toolArchitectureDirectory/GeneralUpdate.Tools.Cmd"
if (-not (Test-Path -LiteralPath $stagingDirectory -PathType Container)) { throw "未找到暂存发布目录：$stagingDirectory" }

$manifest = [ordered]@{
	mainAppName = "WarrenqF1.Desktop"
	clientVersion = $NextVersion
	appType = "OssClient"
	updateAppName = "WarrenqF1.Updater"
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

if (-not $shouldGenerateUpgradePackage) {
	Write-Host "下一版本号不大于当前版本号，跳过升级包生成。"
	Save-OldRelease -SourceDirectory $stagingDirectory -DestinationDirectory $oldDirectory
	Write-Host "已将最新暂存发布保存为 OldRelease：$oldDirectory"
	if ($BackupStagingRelease) { Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $NextVersion }
	return
}

$oldReleaseHasContent = Test-Path -LiteralPath $oldDirectory -PathType Container
if ($oldReleaseHasContent) { $oldReleaseHasContent = @(Get-ChildItem -LiteralPath $oldDirectory -Force).Count -gt 0 }
if ($oldReleaseHasContent) {
	if (-not (Test-Path -LiteralPath $patchTool -PathType Leaf)) { throw "未找到 macOS 差分工具：$patchTool" }
	$architectures = (& lipo -archs $patchTool).Trim().Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries)
	if ($LASTEXITCODE -ne 0 -or $architectures -notcontains $machineArchitecture) { throw "差分工具不支持当前系统处理器架构 $machineArchitecture：$patchTool（当前：$($architectures -join ', ')）" }
	& chmod +x $patchTool
	if ($LASTEXITCODE -ne 0) { throw "无法设置 macOS 差分工具执行权限：$patchTool" }
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
Write-Host "macOS 升级包处理完成：$oldDirectory"
if ($BackupStagingRelease) { Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $NextVersion }
