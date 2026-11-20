$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientPublishAgent\Scripts\Desktop\publish-openclaw.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('openclaw-refresh-' + [guid]::NewGuid().ToString('N'))
try {
	$fixture = Join-Path $root 'fixture'
	$package = Join-Path $fixture 'package'
	$artifact = Join-Path $root 'output\OpenClaw'
	$publish = Join-Path $root 'output\StagingRelease'
	$service = Join-Path $publish 'WarrenqAIEngine\warrenqai'
	New-Item -ItemType Directory -Force -Path (Join-Path $package 'dist'), $artifact, $publish | Out-Null
	Set-Content -LiteralPath (Join-Path $package 'dist\index.js') -Value 'fixture'
	Set-Content -LiteralPath (Join-Path $package 'wqfaos.mjs') -Value 'fixture'
	$fixtureArchive = Join-Path $root 'fixture.tgz'
	& tar -czf $fixtureArchive -C $fixture package
	if ($LASTEXITCODE -ne 0) { throw 'Failed to create fixture archive.' }
	$fakeSevenZip = Join-Path $root 'fake-7zip.ps1'
	Set-Content -LiteralPath $fakeSevenZip -Value 'param($action, $outputPath) if ($action -eq "a") { Set-Content -LiteralPath $outputPath -Value "fixture" }; $global:LASTEXITCODE = 0'
	function Get-OpenClaw7ZipExecutable { return $fakeSevenZip }
	function Get-OpenClawApiToken { return 'fixture-token' }
	function Read-Host { param($Prompt) $script:prompts++; return $script:answers.Dequeue() }
	$script:prompts = 0
	$script:answers = [System.Collections.Generic.Queue[string]]::new()
	function Download-OpenClawArtifact {
		param($Url, $DestinationPath, $ApiToken, $CurlExecutable)
		$script:downloads += [IO.Path]::GetFileName($DestinationPath)
		if ($DestinationPath.EndsWith('.tgz')) { Copy-Item -LiteralPath $fixtureArchive -Destination $DestinationPath -Force }
		else { Set-Content -LiteralPath $DestinationPath -Value 'downloaded-node' }
	}
	$open = Join-Path $artifact 'openclaw-release.tgz'
	$node = Join-Path $artifact 'node_modules.7z'
	Copy-Item -LiteralPath $fixtureArchive -Destination $open
	Set-Content -LiteralPath $node -Value 'cached-node'
	if (Should-Download-OpenClawArtifact -ArchivePath $open -Redownload $false) { throw 'Legacy cache reuse changed.' }
	$runtime = if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }
	foreach ($case in @(
		@{ Open = $false; Node = $false; Expected = @() },
		@{ Open = $true; Node = $false; Expected = @('openclaw-release.tgz') },
		@{ Open = $false; Node = $true; Expected = @('node_modules.7z') },
		@{ Open = $true; Node = $true; Expected = @('openclaw-release.tgz', 'node_modules.7z') }
	)) {
		$script:downloads = @()
		Install-OpenClawRuntime -PublishDirectory $publish -AiServiceDirectory $service -RuntimeIdentifier $runtime -RefreshOpenClaw $case.Open -RefreshNodeModules $case.Node
		if (($script:downloads -join ',') -ne ($case.Expected -join ',')) { throw "Unexpected downloads: $($script:downloads -join ',')" }
		foreach ($name in @('wqfaos.mjs', 'dist.7z', 'node_modules.7z')) {
			if (-not (Test-Path -LiteralPath (Join-Path $service $name) -PathType Leaf)) { throw "Missing packaged artifact: $name" }
		}
	}
	Remove-Item -LiteralPath $open
	if (-not (Should-Download-OpenClawArtifact -ArchivePath $open -Redownload $false)) { throw 'Legacy missing-cache behavior changed.' }
	$script:downloads = @()
	Install-OpenClawRuntime -PublishDirectory $publish -AiServiceDirectory $service -RuntimeIdentifier $runtime -RefreshOpenClaw $false -RefreshNodeModules $false
	if (($script:downloads -join ',') -ne 'openclaw-release.tgz') { throw 'Missing OpenClaw was not downloaded automatically.' }
	Remove-Item -LiteralPath $node
	$script:downloads = @()
	Install-OpenClawRuntime -PublishDirectory $publish -AiServiceDirectory $service -RuntimeIdentifier $runtime -RefreshOpenClaw $false -RefreshNodeModules $false
	if (($script:downloads -join ',') -ne 'node_modules.7z') { throw 'Missing node_modules was not downloaded automatically.' }
	Remove-Item -LiteralPath $open, $node
	$script:downloads = @()
	Install-OpenClawRuntime -PublishDirectory $publish -AiServiceDirectory $service -RuntimeIdentifier $runtime -RefreshOpenClaw $false -RefreshNodeModules $false
	if (($script:downloads -join ',') -ne 'openclaw-release.tgz,node_modules.7z') { throw 'Both missing artifacts were not downloaded automatically.' }
	if ($script:prompts -ne 0) { throw 'Agent download policy unexpectedly requested interactive input.' }
	$script:prompts = 0
	$script:answers = [System.Collections.Generic.Queue[string]]::new()
	$script:answers.Enqueue('N'); $script:answers.Enqueue('Y')
	$script:downloads = @()
	Install-OpenClawRuntime -PublishDirectory $publish -AiServiceDirectory $service -RuntimeIdentifier $runtime
	if ($script:prompts -ne 2 -or ($script:downloads -join ',') -ne 'node_modules.7z') { throw 'Manual interactive download choices changed.' }
	$script:downloads = @()
	Install-OpenClawRuntime -PublishDirectory $publish -AiServiceDirectory $service -RuntimeIdentifier $runtime -DownloadOpenClaw $false -DownloadNodeModules $false
	if ($script:prompts -ne 2 -or $script:downloads.Count -ne 0) { throw 'Legacy explicit download behavior changed.' }
	Write-Host 'OpenClaw cache and download policy checks passed.'
} finally {
	if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}