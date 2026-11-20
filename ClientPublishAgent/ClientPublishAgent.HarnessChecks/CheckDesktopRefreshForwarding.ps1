$ErrorActionPreference = 'Stop'
$scripts = Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientPublishAgent\Scripts\Desktop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('desktop-refresh-' + [guid]::NewGuid().ToString('N'))
$previousOutput = $env:REFRESH_FIXTURE_OUTPUT
try {
	$desktop = Join-Path $root 'Desktop'
	$win = Join-Path $desktop 'win-package'
	$mac = Join-Path $desktop 'mac-package'
	$output = Join-Path $root 'output'
	New-Item -ItemType Directory -Force -Path $win, $mac, (Join-Path $output 'StagingRelease') | Out-Null
	$env:REFRESH_FIXTURE_OUTPUT = $output
	Set-Content -LiteralPath (Join-Path $desktop 'backup-staging-release.ps1') -Value 'function Backup-StagingRelease { param($StagingDirectory, $Version) }'
	Set-Content -LiteralPath (Join-Path $desktop 'publish-root-config.ps1') -Value 'function Resolve-DesktopPublishRootDirectory { param($RuntimeIdentifier, $RepositoryRoot, $RootDirectory) return $RootDirectory }'
	Set-Content -LiteralPath (Join-Path $win 'publish-config.ps1') -Value 'function Get-WindowsPublishRootDirectory { return $env:REFRESH_FIXTURE_OUTPUT }'
	$fakePublish = 'param($RuntimeIdentifier, $RootDirectory, $SourceDirectory, $PublishAot, $RefreshOpenClaw, $RefreshNodeModules) Set-Content -LiteralPath (Join-Path $env:REFRESH_FIXTURE_OUTPUT "arguments.txt") -Value "$RefreshOpenClaw,$RefreshNodeModules"; $global:LASTEXITCODE = 0'
	$fakeUpgrade = 'param($CurrentVersion, $NextVersion, $RootDirectory, $BackupStagingRelease, $RuntimeIdentifier, $OssHost, $SourceDirectory) Set-Content -LiteralPath (Join-Path $env:REFRESH_FIXTURE_OUTPUT "StagingRelease\generalupdate.manifest.json") -Value ''{"clientVersion":"1.0.1"}''; $global:LASTEXITCODE = 0'
	foreach ($platform in @(@{ Dir = $win; Publish = 'publish-win-x64.ps1'; Upgrade = 'upgrade-win-x64.ps1' }, @{ Dir = $mac; Publish = 'publish-mac.ps1'; Upgrade = 'upgrade-mac.ps1' })) {
		Set-Content -LiteralPath (Join-Path $platform.Dir $platform.Publish) -Value $fakePublish
		Set-Content -LiteralPath (Join-Path $platform.Dir $platform.Upgrade) -Value $fakeUpgrade
	}
	foreach ($name in @('publish-upgrade-package-win-x64.ps1')) {
		Copy-Item -LiteralPath (Join-Path $scripts "win-package\$name") -Destination $win
		$args = @('-NoProfile', '-File', (Join-Path $win $name), '-CurrentVersion', '1.0.0', '-NextVersion', '1.0.1', '-RefreshOpenClaw', 'false', '-RefreshNodeModules', 'true')
		if ($name -eq 'publish-upgrade-package-win-x64.ps1') { $args += @('-PackageType', 'upgrade') }
		& (Get-Process -Id $PID).Path @args
		if ($LASTEXITCODE -ne 0 -or (Get-Content -LiteralPath (Join-Path $output 'arguments.txt') -Raw).Trim() -ne 'false,true') { throw "Windows forwarding failed: $name" }
	}
	foreach ($name in @('mac-publish&upgrade&package.ps1')) {
		Copy-Item -LiteralPath (Join-Path $scripts "mac-package\$name") -Destination $mac
		$args = @('-NoProfile', '-File', (Join-Path $mac $name), '-RuntimeIdentifier', 'osx-x64', '-CurrentVersion', '1.0.0', '-NextVersion', '1.0.1', '-RefreshOpenClaw', 'true', '-RefreshNodeModules', 'false')
		if ($name -eq 'mac-publish&upgrade&package.ps1') { $args += @('-RootDirectory', $output, '-PackageType', 'upgrade') }
		& (Get-Process -Id $PID).Path @args
		if ($LASTEXITCODE -ne 0 -or (Get-Content -LiteralPath (Join-Path $output 'arguments.txt') -Raw).Trim() -ne 'true,false') { throw "macOS forwarding failed: $name" }
	}
	Write-Host 'Windows and macOS refresh forwarding checks passed.'
} finally {
	$env:REFRESH_FIXTURE_OUTPUT = $previousOutput
	if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}