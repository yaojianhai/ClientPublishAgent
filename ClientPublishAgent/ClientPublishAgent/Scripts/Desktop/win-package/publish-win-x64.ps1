param(
	[string]$SourceDirectory = $env:CLIENT_PUBLISH_SOURCE_DIRECTORY
)

$ErrorActionPreference = "Stop"

$srcDirectory = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "src" } else { [System.IO.Path]::GetFullPath($SourceDirectory) }
$repoRoot = Split-Path -Parent $srcDirectory
$configScript = Join-Path $PSScriptRoot "publish-config.ps1"
. $configScript
$runtimeIdentifier = "win-x64"
$rootDirectory = Get-WindowsPublishRootDirectory
$publishDirectory = Join-Path $rootDirectory "StagingRelease"
$updaterDirectory = Join-Path $publishDirectory "update"
$engineDirectory = Join-Path $publishDirectory "WarrenqAIEngine"
$launcherDirectory = Join-Path $engineDirectory "Launcher"
$nodeOutputDirectory = Join-Path $engineDirectory "node"
$aiServiceDirectory = Join-Path $engineDirectory "warrenqai"
$desktopProject = Join-Path $srcDirectory "WarrenqF1.Desktop\WarrenqF1.Desktop.csproj"
$updaterProject = Join-Path $srcDirectory "WarrenqF1.Updater\WarrenqF1.Updater.csproj"
$launcherProject = Join-Path $srcDirectory "OpenClaw\WarrenqF1.OpenClawLauncher\WarrenqAILauncher.csproj"
$nodePlatformDirectory = "windows"
$nodeSourceDirectory = Join-Path $repoRoot "Dependency\Node\$nodePlatformDirectory"
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
. (Join-Path $scriptsDirectory "publish-openclaw.ps1")
. (Join-Path $scriptsDirectory "copy-office.ps1")

function Publish-Project {
	param([Parameter(Mandatory)][string]$ProjectPath, [Parameter(Mandatory)][string]$OutputDirectory)

	dotnet publish $ProjectPath `
		--configuration Release `
		--runtime $runtimeIdentifier `
		--self-contained true `
		--output $OutputDirectory `
		-p:PublishAot=true `
		-p:DebugSymbols=false `
		-p:DebugType=None `
		-p:CopyOutputSymbolsToPublishDirectory=false
	if ($LASTEXITCODE -ne 0) { throw "发布失败：$ProjectPath" }
	foreach ($debugFilePattern in "*.pdb", "*.dbg", "*.dwarf") {
		Get-ChildItem -LiteralPath $OutputDirectory -Filter $debugFilePattern -File -Recurse | Remove-Item -Force
	}
}

function Copy-NodeRuntime {
	if (-not (Test-Path -LiteralPath $nodeSourceDirectory -PathType Container)) { throw "未找到 Node 运行时目录：$nodeSourceDirectory" }
	$nodeSourceExecutable = Join-Path $nodeSourceDirectory "node.exe"
	if (-not (Test-Path -LiteralPath $nodeSourceExecutable -PathType Leaf)) { throw "未找到 Node 可执行文件：$nodeSourceExecutable" }
	New-Item -ItemType Directory -Path $nodeOutputDirectory -Force | Out-Null
	Get-ChildItem -LiteralPath $nodeSourceDirectory -Force | Copy-Item -Destination $nodeOutputDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $publishDirectory) {
	if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) { throw "暂存发布路径不是目录：$publishDirectory" }
	Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
Copy-OfficeDependency -RepositoryDirectory $repoRoot -PublishDirectory $publishDirectory
Publish-Project -ProjectPath $desktopProject -OutputDirectory $publishDirectory
Install-OpenClawRuntime -PublishDirectory $publishDirectory -AiServiceDirectory $aiServiceDirectory -RuntimeIdentifier $runtimeIdentifier
Copy-NodeRuntime
New-Item -ItemType Directory -Path $updaterDirectory -Force | Out-Null
Publish-Project -ProjectPath $updaterProject -OutputDirectory $updaterDirectory
New-Item -ItemType Directory -Path $launcherDirectory -Force | Out-Null
Publish-Project -ProjectPath $launcherProject -OutputDirectory $launcherDirectory
Write-Host "Windows 暂存发布完成：$publishDirectory"