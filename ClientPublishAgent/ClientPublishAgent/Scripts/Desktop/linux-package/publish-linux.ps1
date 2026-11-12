param(
	[ValidateSet("linux-arm64", "linux-x64")]
	[string]$RuntimeIdentifier,
	[string]$RootDirectory,
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY,
	[bool]$DownloadOpenClaw = $true,
	[bool]$DownloadNodeModules = $true
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "linux-common.ps1")
. (Join-Path (Split-Path -Parent $PSScriptRoot) "publish-root-config.ps1")
$RuntimeIdentifier = Get-LinuxRuntimeIdentifier -RuntimeIdentifier $RuntimeIdentifier
$isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$hostRuntimeIdentifier = if ($isWindowsHost) { "win-x64" } else { Get-LinuxRuntimeIdentifier }
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$defaultSourceDirectory = Join-Path (Split-Path -Parent $scriptsDirectory) "src"
$srcDirectory = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { $defaultSourceDirectory } else { [System.IO.Path]::GetFullPath($SourceDirectory) }
$repoRoot = Split-Path -Parent $srcDirectory
$rootDirectory = Resolve-DesktopPublishRootDirectory -RuntimeIdentifier $RuntimeIdentifier -RepositoryRoot $repoRoot -RootDirectory $RootDirectory
if ($isWindowsHost) {
	$wslParameters = @{
		RuntimeIdentifier = $RuntimeIdentifier
		RootDirectory = $rootDirectory
		DownloadOpenClaw = $DownloadOpenClaw
		DownloadNodeModules = $DownloadNodeModules
	}
	if (-not [string]::IsNullOrWhiteSpace($SourceDirectory)) { $wslParameters.SourceDirectory = $SourceDirectory }
	Invoke-LinuxScriptInWsl -ScriptPath $PSCommandPath -Parameters $wslParameters
	return
}

$hostRuntimeIdentifier = Get-LinuxRuntimeIdentifier
$artifactsDirectory = Join-Path $rootDirectory "artifacts"
$publishDirectory = Join-Path $rootDirectory "StagingRelease"
$updaterDirectory = Join-Path $publishDirectory "update"
$engineDirectory = Join-Path $publishDirectory "WarrenqAIEngine"
$launcherDirectory = Join-Path $engineDirectory "Launcher"
$nodeOutputDirectory = Join-Path $engineDirectory "node"
$aiServiceDirectory = Join-Path $engineDirectory "warrenqai"
$desktopProject = Join-Path $srcDirectory "WarrenqF1.Desktop/WarrenqF1.Desktop.csproj"
$updaterProject = Join-Path $srcDirectory "WarrenqF1.Updater/WarrenqF1.Updater.csproj"
$launcherProject = Join-Path $srcDirectory "OpenClaw/WarrenqF1.OpenClawLauncher/WarrenqAILauncher.csproj"
$nodeArchitectureDirectory = switch ($RuntimeIdentifier) { "linux-arm64" { "arm64"; break } "linux-x64" { "x64"; break } }
$nodeSourceDirectory = Join-Path $repoRoot "Dependency/Node/linux/$nodeArchitectureDirectory"
if (-not (Test-Path -LiteralPath $nodeSourceDirectory -PathType Container)) {
	$nodeSourceDirectory = Join-Path $repoRoot "Dependency/Node/linux"
}
. (Join-Path $scriptsDirectory "publish-openclaw.ps1")
. (Join-Path $scriptsDirectory "copy-office.ps1")

$requiredCommands = "dotnet", "chmod", "tar", "clang", "objcopy"
foreach ($command in $requiredCommands) {
	if (-not (Get-Command $command -CommandType Application -ErrorAction SilentlyContinue)) { throw "未找到 Linux 发布所需命令：$command" }
}
Test-LinuxExecutable -Path (Join-Path $nodeSourceDirectory "node") -RuntimeIdentifier $RuntimeIdentifier
Test-LinuxExecutable -Path (Get-OpenClaw7ZipExecutable -RuntimeIdentifier $RuntimeIdentifier -BundledOnly) -RuntimeIdentifier $RuntimeIdentifier
Test-LinuxExecutable -Path (Get-OpenClaw7ZipExecutable -RuntimeIdentifier $hostRuntimeIdentifier -BundledOnly) -RuntimeIdentifier $hostRuntimeIdentifier

function Publish-Project {
	param([Parameter(Mandatory)][string]$ProjectPath, [Parameter(Mandatory)][string]$OutputDirectory)
	dotnet publish $ProjectPath `
		--configuration Release `
		--runtime $RuntimeIdentifier `
		--self-contained true `
		--artifacts-path $artifactsDirectory `
		--output $OutputDirectory `
		-p:PublishAot=true `
		-p:PublishTrimmed=true `
		-p:GLibcVersion=2.28 `
		-p:StripSymbols=true `
		-p:DebugSymbols=false `
		-p:DebugType=None `
		-p:CopyOutputSymbolsToPublishDirectory=false
	if ($LASTEXITCODE -ne 0) { throw "发布失败：$ProjectPath" }
	$executableName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
	Test-LinuxExecutable -Path (Join-Path $OutputDirectory $executableName) -RuntimeIdentifier $RuntimeIdentifier
	foreach ($debugFilePattern in "*.pdb", "*.dbg", "*.dwarf") {
		Get-ChildItem -LiteralPath $OutputDirectory -Filter $debugFilePattern -File -Recurse | Remove-Item -Force
	}
}

function Copy-NodeRuntime {
	New-Item -ItemType Directory -Path $nodeOutputDirectory -Force | Out-Null
	Get-ChildItem -LiteralPath $nodeSourceDirectory -Force | Copy-Item -Destination $nodeOutputDirectory -Recurse -Force
	Set-LinuxExecutablePermission -Path (Join-Path $nodeOutputDirectory "node")
}

if (Test-Path -LiteralPath $publishDirectory) {
	if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) { throw "暂存发布路径不是目录：$publishDirectory" }
	Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
Copy-OfficeDependency -RepositoryDirectory $repoRoot -PublishDirectory $publishDirectory
Publish-Project -ProjectPath $desktopProject -OutputDirectory $publishDirectory
Install-OpenClawRuntime -PublishDirectory $publishDirectory -AiServiceDirectory $aiServiceDirectory -RuntimeIdentifier $RuntimeIdentifier -DownloadOpenClaw $DownloadOpenClaw -DownloadNodeModules $DownloadNodeModules
Copy-NodeRuntime
New-Item -ItemType Directory -Path $updaterDirectory -Force | Out-Null
Publish-Project -ProjectPath $updaterProject -OutputDirectory $updaterDirectory
New-Item -ItemType Directory -Path $launcherDirectory -Force | Out-Null
Publish-Project -ProjectPath $launcherProject -OutputDirectory $launcherDirectory
foreach ($relativeExecutablePath in "WarrenqF1.Desktop", "update/WarrenqF1.Updater", "WarrenqAIEngine/Launcher/WarrenqAILauncher", "WarrenqAIEngine/7zz", "WarrenqAIEngine/node/node") {
	$executablePath = Join-Path $publishDirectory $relativeExecutablePath
	Test-LinuxExecutable -Path $executablePath -RuntimeIdentifier $RuntimeIdentifier
	Set-LinuxExecutablePermission -Path $executablePath
}
Write-Host "Linux AOT 暂存发布完成：$publishDirectory"
