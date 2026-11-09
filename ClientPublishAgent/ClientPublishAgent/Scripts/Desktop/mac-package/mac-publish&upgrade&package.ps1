param(
	[ValidateSet("osx-arm64", "osx-x64")]
	[string]$RuntimeIdentifier,
	[string]$CurrentVersion,
	[string]$NextVersion,
	[string]$OssHost,
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[string]$OutputDirectory,
	[string]$SigningIdentity,
	[string]$BundleIdentifier = "com.gildata.warrenqf1",
	[ValidateSet("upgrade", "installer", "upgrade-installer")]
	[string]$PackageType = "upgrade-installer"
)

$ErrorActionPreference = "Stop"
$backupScript = Join-Path (Split-Path -Parent $PSScriptRoot) "backup-staging-release.ps1"
. $backupScript
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
$publishScript = Join-Path $PSScriptRoot "publish-mac.ps1"
$upgradeScript = Join-Path $PSScriptRoot "upgrade-mac.ps1"
$packageScript = Join-Path $PSScriptRoot "package-mac.ps1"
$publishParameters = @{}
$upgradeParameters = @{ CurrentVersion = $CurrentVersion; NextVersion = $NextVersion }
$upgradeParameters["BackupStagingRelease"] = $false
$runtimeIdentifierForRoot = $RuntimeIdentifier
if ([string]::IsNullOrWhiteSpace($runtimeIdentifierForRoot)) {
	$machineArchitecture = (& uname -m).Trim()
	if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($machineArchitecture)) {
		$runtimeIdentifierForRoot = switch ($machineArchitecture) { "arm64" { "osx-arm64"; break } "x86_64" { "osx-x64"; break } default { $null } }
	}
}
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Split-Path -Parent $scriptsDirectory } else { Split-Path -Parent ([System.IO.Path]::GetFullPath($SourceDirectory)) }
$rootDirectory = if ([string]::IsNullOrWhiteSpace($runtimeIdentifierForRoot)) { throw "无法解析 macOS 运行时标识，无法确定发布根目录。" } else { Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $runtimeIdentifierForRoot -RepositoryRoot $repoRoot -RootDirectory $RootDirectory }
$publishParameters = @{ RootDirectory = $rootDirectory }
if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) { $publishParameters.SourceDirectory = $SourceDirectory }
$publishParameters.RootDirectory = $rootDirectory
$upgradeParameters.RootDirectory = $rootDirectory
$packageParameters = @{ BundleIdentifier = $BundleIdentifier }
$packageParameters.RootDirectory = $rootDirectory
if (-not [string]::IsNullOrWhiteSpace($rootDirectory)) {
	$packageParameters.PublishDirectory = Join-Path $rootDirectory "StagingRelease"
}
if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
	$publishParameters.RuntimeIdentifier = $RuntimeIdentifier
	$upgradeParameters.RuntimeIdentifier = $RuntimeIdentifier
	$packageParameters.RuntimeIdentifier = $RuntimeIdentifier
}
if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) {
	$upgradeParameters["SourceDirectory"] = $SourceDirectory
	$packageParameters["SourceDirectory"] = $SourceDirectory
}
if (-not [string]::IsNullOrWhiteSpace($OssHost)) { $upgradeParameters.OssHost = $OssHost }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $packageParameters.OutputDirectory = Join-Path $rootDirectory "output" } else { $packageParameters.OutputDirectory = $OutputDirectory }
if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) { $packageParameters.SigningIdentity = $SigningIdentity }
& $publishScript @publishParameters
if ($PackageType -in "upgrade", "upgrade-installer") { & $upgradeScript @upgradeParameters }
if ($PackageType -eq "installer") {
	$stagingDirectory = Join-Path $rootDirectory "StagingRelease"
	$manifest = [ordered]@{ mainAppName = "WarrenqF1.Desktop"; clientVersion = $NextVersion; appType = "OssClient"; updateAppName = "WarrenqF1.Updater"; updatePath = "update/" }
	[System.IO.File]::WriteAllText((Join-Path $stagingDirectory "generalupdate.manifest.json"), ($manifest | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
}
if ($PackageType -in "installer", "upgrade-installer") { & $packageScript @packageParameters }
$stagingDirectory = Join-Path $rootDirectory "StagingRelease"
$manifestFile = Join-Path $stagingDirectory "generalupdate.manifest.json"
if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "未找到升级发布清单：$manifestFile" }
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json -ErrorAction Stop
$clientVersion = ([string]$manifest.clientVersion).Trim()
if ([string]::IsNullOrWhiteSpace($clientVersion)) { throw "升级发布清单未包含有效的 clientVersion：$manifestFile" }
Backup-StagingRelease -StagingDirectory $stagingDirectory -Version $clientVersion
