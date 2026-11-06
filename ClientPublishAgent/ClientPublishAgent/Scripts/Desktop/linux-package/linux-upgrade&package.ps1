param(
	[ValidateSet("linux-arm64", "linux-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[string]$OutputDirectory,
	[ValidatePattern('^[A-Za-z][A-Za-z0-9.-]*$')]
	[string]$BundleIdentifier = "com.gildata.warrenqf1"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "linux-common.ps1")
$backupScript = Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1"
. $backupScript
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
$RuntimeIdentifier = Get-LinuxRuntimeIdentifier -RuntimeIdentifier $RuntimeIdentifier
$CurrentVersion = Read-LinuxSemanticVersion -Prompt "请输入当前版本号" -Value $CurrentVersion
$NextVersion = Read-LinuxSemanticVersion -Prompt "请输入下一版本号" -Value $NextVersion
$srcDirectory = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "src" } else { [System.IO.Path]::GetFullPath($SourceDirectory) }
$repoRoot = Split-Path -Parent $srcDirectory
$RootDirectory = Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $RuntimeIdentifier -RepositoryRoot $repoRoot -RootDirectory $RootDirectory
if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
	$parameters = @{ RuntimeIdentifier = $RuntimeIdentifier; CurrentVersion = $CurrentVersion; NextVersion = $NextVersion; BundleIdentifier = $BundleIdentifier }
	$parameters.RootDirectory = $RootDirectory
	if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) { $parameters.SourceDirectory = $SourceDirectory }
	if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $parameters.OssHost = $OssHost }
	if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) { $parameters.OutputDirectory = $OutputDirectory }
	Invoke-LinuxScriptInWsl -ScriptPath $PSCommandPath -Parameters $parameters
	return
}
$upgradeScript = Join-Path $PSScriptRoot "upgrade-linux.ps1"
$packageScript = Join-Path $PSScriptRoot "package-linux.ps1"
$rootDirectory = $RootDirectory
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $rootDirectory "output" }
function Get-CanonicalLinuxPath {
	param([string]$Path)
	if (-not [System.IO.Path]::IsPathRooted($Path)) { $Path = Join-Path (Get-Location).Path $Path }
	$result = & realpath -m -- $Path
	if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($result)) { throw "无法规范化路径：$Path" }
	return $result
}
function Test-PathsOverlap {
	param([string]$First, [string]$Second)
	$firstPrefix = $First.TrimEnd('/') + '/'
	$secondPrefix = $Second.TrimEnd('/') + '/'
	return $First -ceq $Second -or $First.StartsWith($secondPrefix, [StringComparison]::Ordinal) -or $Second.StartsWith($firstPrefix, [StringComparison]::Ordinal)
}
$OutputDirectory = Get-CanonicalLinuxPath $OutputDirectory
foreach ($name in "OldRelease", "OutputPatchPackage", "StagingRelease") {
	$protectedPath = Get-CanonicalLinuxPath (Join-Path $rootDirectory $name)
	if (Test-PathsOverlap $OutputDirectory $protectedPath) { throw "安装包输出目录与受保护目录重叠：$OutputDirectory；$protectedPath" }
}
$packageStagingDirectory = Join-Path $rootDirectory (".deb-stage-" + [guid]::NewGuid().ToString("N"))
$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion; RootDirectory = $rootDirectory }
$upgradeParameters["BackupStagingRelease"] = $false
$packageSourceDirectory = $srcDirectory
$packageOutputDirectory = $packageStagingDirectory
$packageParameters = @{ BundleIdentifier = $BundleIdentifier; RootDirectory = $rootDirectory; OutputDirectory = $packageOutputDirectory; PublishDirectory = (Join-Path $rootDirectory "StagingRelease"); SourceDirectory = $packageSourceDirectory }
if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$upgradeParameters.RuntimeIdentifier = $RuntimeIdentifier
	$packageParameters.RuntimeIdentifier = $RuntimeIdentifier
}
if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
$upgradeParameters.BeforeBaselineCommit = { & $packageScript @packageParameters }
$upgradeParameters.AfterBaselineCommit = {
	$packages = @(Get-ChildItem -LiteralPath $packageStagingDirectory -Filter "*.deb" -File)
	if ($packages.Count -ne 1 -or $packages[0].Length -eq 0) { throw "暂存目录中没有唯一有效的 Debian 安装包：$packageStagingDirectory" }
	$createdDirectories = [System.Collections.Generic.List[string]]::new()
	$missing = [System.Collections.Generic.List[string]]::new()
	$directory = $OutputDirectory
	while (-not (Test-Path -LiteralPath $directory -PathType Container)) {
		if (Test-Path -LiteralPath $directory) { throw "安装包输出路径不是目录：$directory" }
		$missing.Add($directory)
		$parent = Split-Path -Parent $directory
		if (-not $parent -or $parent -eq $directory) { throw "无法创建安装包输出目录：$OutputDirectory" }
		$directory = $parent
	}
	$identifier = [guid]::NewGuid().ToString("N")
	$target = Join-Path $OutputDirectory $packages[0].Name
	$temporary = Join-Path $OutputDirectory (".deb-new-$identifier")
	$backup = Join-Path $OutputDirectory (".deb-previous-$identifier")
	$backedUp = $false
	$installed = $false
	try {
		for ($i = $missing.Count - 1; $i -ge 0; $i--) {
			New-Item -ItemType Directory -Path $missing[$i] | Out-Null
			$createdDirectories.Add($missing[$i])
		}
		$existing = Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
		if ($existing -and ($existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint))) { throw "安装包目标不是普通文件：$target" }
		Copy-Item -LiteralPath $packages[0].FullName -Destination $temporary
		if ($existing) {
			Move-Item -LiteralPath $target -Destination $backup
			$backedUp = $true
		}
		Move-Item -LiteralPath $temporary -Destination $target
		$installed = $true
	} catch {
		$failure = $_
		try {
			if ($installed) { Remove-Item -LiteralPath $target -Force }
			if ($backedUp) { Move-Item -LiteralPath $backup -Destination $target }
			if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
			for ($i = $createdDirectories.Count - 1; $i -ge 0; $i--) { Remove-Item -LiteralPath $createdDirectories[$i] -Force }
		} catch { throw "安装包提交失败：$failure；回滚失败：$_。请检查备份文件 $backup。" }
		throw $failure
	}
	if ($backedUp) { Remove-Item -LiteralPath $backup -Force -ErrorAction Continue }
	Write-Host "Debian 安装包已提交：$target"
}
try {
	& $upgradeScript @upgradeParameters
	Backup-StagingRelease -StagingDirectory (Join-Path $rootDirectory "StagingRelease") -Version $NextVersion
} finally {
	if (Test-Path -LiteralPath $packageStagingDirectory) { Remove-Item -LiteralPath $packageStagingDirectory -Recurse -Force -ErrorAction Continue }
}
