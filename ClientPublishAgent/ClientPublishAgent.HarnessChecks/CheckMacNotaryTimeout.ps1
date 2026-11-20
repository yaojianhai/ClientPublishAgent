$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientPublishAgent\Scripts\Desktop\mac-package\package-mac.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'macOS packaging script failed to parse.' }
$function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Submit-ForNotarization' }, $true)
if ($null -eq $function) { throw 'Notarization function not found.' }
. ([scriptblock]::Create($function.Extent.Text))

$NotaryKeychainProfile = 'test-profile'
$script:calls = 0
function xcrun {
	$script:calls++
	$global:LASTEXITCODE = $script:fakeExitCode
	Write-Output $script:fakeOutput
}
function Expect-NotaryFailure {
	param([string]$Expected)
	$message = $null
	try { Submit-ForNotarization -Path 'test.dmg' -Description 'macOS DMG' }
	catch { $message = $_.Exception.Message }
	if ($null -eq $message -or -not $message.Contains($Expected)) { throw "Expected failure containing: $Expected; got: $message" }
	return $message
}

$script:fakeExitCode = 1
$script:fakeOutput = 'Error: HTTPClientError.connectTimeout'
$message = Expect-NotaryFailure 'Apple 公证连接超时'
if (-not $message.Contains('提交状态不确定') -or -not $message.Contains('notarytool history') -or -not $message.Contains('不要分发')) {
	throw 'Timeout recovery guidance is incomplete.'
}
if ($script:calls -ne 1) { throw 'Timeout must not cause another submission.' }

$script:fakeOutput = 'Error: another failure'
$message = Expect-NotaryFailure 'Apple 公证提交失败（退出代码：1）'
if ($message.Contains('连接超时')) { throw 'Other failures must not be reported as timeouts.' }

$script:fakeExitCode = 0
$script:fakeOutput = '{"status":"Invalid","id":"test-id"}'
Expect-NotaryFailure 'Apple 未接受' | Out-Null
$script:fakeOutput = 'invalid json'
Expect-NotaryFailure '无法解析 Apple 公证结果' | Out-Null
$script:fakeOutput = '{"status":"Accepted","id":"test-id"}'
Submit-ForNotarization -Path 'test.dmg' -Description 'macOS DMG'
if ($script:calls -ne 5) { throw 'Unexpected notarization submission count.' }
Write-Host 'macOS notarization result checks passed.'