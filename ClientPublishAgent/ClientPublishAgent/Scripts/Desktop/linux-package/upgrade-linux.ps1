param(
	[ValidateSet("linux-arm64", "linux-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[bool]$BackupStagingRelease = $true,
	[string]$PatchToolPath,
	[scriptblock]$BeforeBaselineCommit,
	[scriptblock]$AfterBaselineCommit
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "linux-common.ps1")
$backupScript = Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1"
. $backupScript
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
$RuntimeIdentifier = Get-LinuxRuntimeIdentifier -RuntimeIdentifier $RuntimeIdentifier
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Split-Path -Parent $scriptsDirectory } else { Split-Path -Parent ([System.IO.Path]::GetFullPath($SourceDirectory)) }
$RootDirectory = Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $RuntimeIdentifier -RepositoryRoot $repoRoot -RootDirectory $RootDirectory
if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
	if ($null -ne $BeforeBaselineCommit -or $null -ne $AfterBaselineCommit) { throw "基线提交回调只能在 Linux 中执行。" }
	$PSBoundParameters["RuntimeIdentifier"] = $RuntimeIdentifier
	$PSBoundParameters["RootDirectory"] = $RootDirectory
	Invoke-LinuxScriptInWsl -ScriptPath $PSCommandPath -Parameters $PSBoundParameters
	return
}
$hostRuntimeIdentifier = Get-LinuxRuntimeIdentifier

$CurrentVersion = Read-LinuxSemanticVersion -Prompt "请输入当前版本号" -Value $CurrentVersion
$NextVersion = Read-LinuxSemanticVersion -Prompt "请输入下一版本号" -Value $NextVersion
[version]$currentVersionValue = $null
[version]$nextVersionValue = $null
if (-not [version]::TryParse($CurrentVersion, [ref]$currentVersionValue) -or -not [version]::TryParse($NextVersion, [ref]$nextVersionValue)) {
	throw "版本号格式无效。当前版本：$CurrentVersion；下一版本：$NextVersion"
}
if ($nextVersionValue -lt $currentVersionValue) { throw "下一版本号不能小于当前版本号。" }
$shouldGenerateUpgradePackage = $nextVersionValue -gt $currentVersionValue

$RootDirectory = [System.IO.Path]::GetFullPath($RootDirectory)
if ([string]::IsNullOrWhiteSpace($OssHost)) { $OssHost = "http://10.106.1.123/upgrade/WarrenQ/WarrenqF1/$RuntimeIdentifier" }
$stagingDirectory = Join-Path $RootDirectory "StagingRelease"
$oldDirectory = Join-Path $RootDirectory "OldRelease"
$patchOutputDirectory = Join-Path $RootDirectory "OutputPatchPackage"
$toolArchitectureDirectory = switch ($hostRuntimeIdentifier) { "linux-arm64" { "arm64"; break } "linux-x64" { "x64"; break } }
if (-not (Test-Path -LiteralPath $stagingDirectory -PathType Container)) { throw "未找到暂存发布目录：$stagingDirectory" }

$manifest = [ordered]@{
	mainAppName = "WarrenqF1.Desktop"
	clientVersion = $NextVersion
	appType = "OssClient"
	updateAppName = "WarrenqF1.Updater"
	updatePath = "update/"
}
[System.IO.File]::WriteAllText((Join-Path $stagingDirectory "generalupdate.manifest.json"), ($manifest | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))

$oldReleaseHasContent = Test-Path -LiteralPath $oldDirectory -PathType Container
if ($oldReleaseHasContent) { $oldReleaseHasContent = @(Get-ChildItem -LiteralPath $oldDirectory -Force).Count -gt 0 }
if ($shouldGenerateUpgradePackage -and $oldReleaseHasContent) {
	$oldManifestPath = Join-Path $oldDirectory "generalupdate.manifest.json"
	if (Test-Path -LiteralPath $oldManifestPath -PathType Leaf) {
		$oldManifest = Get-Content -LiteralPath $oldManifestPath -Raw | ConvertFrom-Json
		if ([string]$oldManifest.clientVersion -ne $CurrentVersion) { throw "旧版本清单与当前版本不一致：$($oldManifest.clientVersion)；当前版本：$CurrentVersion" }
	}
	if ([string]::IsNullOrWhiteSpace($PatchToolPath)) {
		$PatchToolPath = Join-Path $PSScriptRoot "$toolArchitectureDirectory/GeneralUpdate.Tools.Cmd"
	}
	Test-LinuxExecutable -Path $PatchToolPath -RuntimeIdentifier $hostRuntimeIdentifier
	Set-LinuxExecutablePermission -Path $PatchToolPath
}
$preparedPatchDirectory = $null
$preparedOldDirectory = Join-Path $RootDirectory (".old-release-" + [guid]::NewGuid().ToString("N"))
$backupOldDirectory = Join-Path $RootDirectory (".old-release-previous-" + [guid]::NewGuid().ToString("N"))
$patchBackupDirectory = Join-Path $RootDirectory (".patch-previous-" + [guid]::NewGuid().ToString("N"))
$patchChanges = [System.Collections.Generic.List[object]]::new()
$oldBackedUp = $false
$oldInstalled = $false
$patchDirectoryCreated = $false
try {
	if ($shouldGenerateUpgradePackage -and $oldReleaseHasContent) {
		$preparedPatchDirectory = Join-Path $RootDirectory (".patch-" + [guid]::NewGuid().ToString("N"))
		& $PatchToolPath `
			--old-dir $oldDirectory `
			--new-dir $stagingDirectory `
			--output-dir $preparedPatchDirectory `
			--host $OssHost `
			--current-version $CurrentVersion `
			--next-version $NextVersion `
			--suspicious-files include
		if ($LASTEXITCODE -ne 0) { throw "GeneralUpdate.Tools.Cmd 执行失败，退出代码：$LASTEXITCODE" }
		foreach ($name in "patch_v${CurrentVersion}_to_v${NextVersion}.zip", "oss_config.json") {
			$file = Get-Item -LiteralPath (Join-Path $preparedPatchDirectory $name) -ErrorAction Stop
			if ($file.PSIsContainer -or $file.Length -eq 0) { throw "差分产物为空或不是文件：$($file.FullName)" }
		}
	} else {
		if (-not $shouldGenerateUpgradePackage) { Write-Host "下一版本号不大于当前版本号，跳过升级包生成。" }
		else { Write-Host "OldRelease 不存在或为空，首次发布跳过差分包生成。" }
	}
	if ($null -ne $BeforeBaselineCommit) { & $BeforeBaselineCommit }
	if ((Test-Path -LiteralPath $oldDirectory) -and -not (Test-Path -LiteralPath $oldDirectory -PathType Container)) { throw "旧版本路径不是目录：$oldDirectory" }
	Copy-Item -LiteralPath $stagingDirectory -Destination $preparedOldDirectory -Recurse -Force
	if ($preparedPatchDirectory) {
		if ((Test-Path -LiteralPath $patchOutputDirectory) -and -not (Test-Path -LiteralPath $patchOutputDirectory -PathType Container)) { throw "差分输出路径不是目录：$patchOutputDirectory" }
		foreach ($name in "patch_v${CurrentVersion}_to_v${NextVersion}.zip", "oss_config.json") {
			$target = Join-Path $patchOutputDirectory $name
			$existing = Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
			if ($existing -and ($existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint))) { throw "差分产物目标不是普通文件：$target" }
		}
	}
	if (Test-Path -LiteralPath $oldDirectory) {
		Move-Item -LiteralPath $oldDirectory -Destination $backupOldDirectory
		$oldBackedUp = $true
	}
	Move-Item -LiteralPath $preparedOldDirectory -Destination $oldDirectory
	$oldInstalled = $true
	if ($preparedPatchDirectory) {
		if (-not (Test-Path -LiteralPath $patchOutputDirectory)) {
			New-Item -ItemType Directory -Path $patchOutputDirectory | Out-Null
			$patchDirectoryCreated = $true
		}
		New-Item -ItemType Directory -Path $patchBackupDirectory | Out-Null
		foreach ($name in "patch_v${CurrentVersion}_to_v${NextVersion}.zip", "oss_config.json") {
			$target = Join-Path $patchOutputDirectory $name
			$backup = Join-Path $patchBackupDirectory $name
			$change = @{ Target = $target; Backup = $backup; BackedUp = $false; Installed = $false }
			$patchChanges.Add($change)
			if (Test-Path -LiteralPath $target) {
				Move-Item -LiteralPath $target -Destination $backup
				$change.BackedUp = $true
			}
			Move-Item -LiteralPath (Join-Path $preparedPatchDirectory $name) -Destination $target
			$change.Installed = $true
		}
	}
	if ($null -ne $AfterBaselineCommit) { & $AfterBaselineCommit }
} catch {
	$failure = $_
	try {
		for ($i = $patchChanges.Count - 1; $i -ge 0; $i--) {
			$change = $patchChanges[$i]
			if ($change.Installed) { Remove-Item -LiteralPath $change.Target -Force }
			if ($change.BackedUp) { Move-Item -LiteralPath $change.Backup -Destination $change.Target }
		}
		if ($patchDirectoryCreated) { Remove-Item -LiteralPath $patchOutputDirectory -Force }
		if ($oldInstalled) { Remove-Item -LiteralPath $oldDirectory -Recurse -Force }
		if ($oldBackedUp) { Move-Item -LiteralPath $backupOldDirectory -Destination $oldDirectory }
		if (Test-Path -LiteralPath $patchBackupDirectory) { Remove-Item -LiteralPath $patchBackupDirectory -Force }
	} catch {
		throw "提交失败：$failure；回滚失败：$_。请检查备份目录 $backupOldDirectory 和 $patchBackupDirectory。"
	}
	throw $failure
} finally {
	foreach ($directory in $preparedPatchDirectory, $preparedOldDirectory) {
		if ($directory -and (Test-Path -LiteralPath $directory)) { Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction Continue }
	}
}
if ($oldBackedUp -and (Test-Path -LiteralPath $backupOldDirectory)) { Remove-Item -LiteralPath $backupOldDirectory -Recurse -Force }
if (Test-Path -LiteralPath $patchBackupDirectory) { Remove-Item -LiteralPath $patchBackupDirectory -Recurse -Force -ErrorAction Continue }
Write-Host "Linux 升级包处理完成：$oldDirectory"
if ($BackupStagingRelease) { Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $NextVersion }
