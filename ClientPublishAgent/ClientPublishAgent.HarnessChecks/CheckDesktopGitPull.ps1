$ErrorActionPreference = 'Stop'
$scripts = Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientPublishAgent/Scripts/Desktop'
. (Join-Path $scripts 'publish-root-config.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('desktop-git-pull-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
function Invoke-FixtureGit {
	param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
	& git @Arguments
	if ($LASTEXITCODE -ne 0) { throw "Git fixture failed: $Arguments" }
}
try {
	$remote = Join-Path $root 'remote.git'
	$work = Join-Path $root 'work'
	$other = Join-Path $root 'other'
	Invoke-FixtureGit init --bare --initial-branch=main $remote
	Invoke-FixtureGit clone $remote $work
	Invoke-FixtureGit -C $work config user.name 'Fixture'
	Invoke-FixtureGit -C $work config user.email 'fixture@example.invalid'
	New-Item -ItemType Directory -Path (Join-Path $work 'src') -Force | Out-Null
	Set-Content -LiteralPath (Join-Path $work 'src/value.txt') -Value 'initial'
	Invoke-FixtureGit -C $work add src/value.txt
	Invoke-FixtureGit -C $work commit -m initial
	Invoke-FixtureGit -C $work push -u origin main
	Invoke-FixtureGit clone $remote $other
	Invoke-FixtureGit -C $other config user.name 'Fixture'
	Invoke-FixtureGit -C $other config user.email 'fixture@example.invalid'
	Set-Content -LiteralPath (Join-Path $other 'src/value.txt') -Value 'updated'
	Invoke-FixtureGit -C $other commit -am update
	Invoke-FixtureGit -C $other push
	Set-Content -LiteralPath (Join-Path $work 'local-untracked.txt') -Value 'keep'
	Sync-DesktopSource -SourceDirectory (Join-Path $work 'src')
	if ((Get-Content -LiteralPath (Join-Path $work 'src/value.txt') -Raw).Trim() -cne 'updated' -or
		(Get-Content -LiteralPath (Join-Path $work 'local-untracked.txt') -Raw).Trim() -cne 'keep') { throw '拉取后代码或本地文件不正确。' }
	Set-Content -LiteralPath (Join-Path $other 'src/value.txt') -Value 'remote-conflict'
	Invoke-FixtureGit -C $other commit -am conflict
	Invoke-FixtureGit -C $other push
	Set-Content -LiteralPath (Join-Path $work 'src/value.txt') -Value 'local-change'
	$failed = $false
	try { Sync-DesktopSource -SourceDirectory (Join-Path $work 'src') } catch { $failed = $_.Exception.Message.Contains('Git 拉取失败') }
	if (-not $failed -or (Get-Content -LiteralPath (Join-Path $work 'src/value.txt') -Raw).Trim() -cne 'local-change') {
		throw '拉取冲突未停止或覆盖了本地修改。'
	}
	Write-Host '桌面源码拉取检查通过。'
} finally {
	Remove-Item -LiteralPath $root -Recurse -Force
}