$ErrorActionPreference = 'Stop'
$scripts = Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientPublishAgent/Scripts/Desktop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('mac-signing-check-' + [guid]::NewGuid().ToString('N'))
$mac = Join-Path $root 'mac-package'
$oldIdentity = $env:APPLE_SIGNING_IDENTITY
$oldProfile = $env:APPLE_NOTARY_PROFILE
New-Item -ItemType Directory -Path $mac -Force | Out-Null
try {
	foreach ($name in @('mac-publish&upgrade&package.ps1', 'mac-upgrade&package.ps1')) {
		Copy-Item -LiteralPath (Join-Path $scripts "mac-package/$name") -Destination $mac
	}
	Copy-Item -LiteralPath (Join-Path $scripts 'publish-root-config.ps1') -Destination $root
	Set-Content -LiteralPath (Join-Path $root 'backup-staging-release.ps1') -Value 'function Backup-StagingRelease { param($StagingDirectory, $Version) }'
	Set-Content -LiteralPath (Join-Path $mac 'publish-mac.ps1') -Value 'param($RootDirectory,$RuntimeIdentifier,$PublishAot) New-Item -ItemType Directory -Path (Join-Path $RootDirectory "StagingRelease") -Force | Out-Null'
	Set-Content -LiteralPath (Join-Path $mac 'upgrade-mac.ps1') -Value 'param($CurrentVersion,$NextVersion,$RuntimeIdentifier,$OssHost,$RootDirectory,$BackupStagingRelease)'
	Set-Content -LiteralPath (Join-Path $mac 'package-mac.ps1') -Value 'param($BundleIdentifier,$RootDirectory,$PublishDirectory,$RuntimeIdentifier,$OutputDirectory,$SigningIdentity = $env:APPLE_SIGNING_IDENTITY,$NotaryKeychainProfile = $env:APPLE_NOTARY_PROFILE) @{ Identity = $SigningIdentity; Profile = $NotaryKeychainProfile } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot "result.json")'
	$env:APPLE_SIGNING_IDENTITY = 'environment identity'
	$env:APPLE_NOTARY_PROFILE = 'environment profile'
	foreach ($entry in @('mac-publish&upgrade&package.ps1', 'mac-upgrade&package.ps1')) {
		$parameters = @{ RuntimeIdentifier = 'osx-x64'; CurrentVersion = '1.0.0'; NextVersion = '1.0.1' }
		if ($entry -eq 'mac-publish&upgrade&package.ps1') { $parameters.RootDirectory = Join-Path $root 'output'; $parameters.PackageType = 'installer' }
		& (Join-Path $mac $entry) @parameters -SigningIdentity '' -NotaryKeychainProfile ''
		$result = Get-Content -LiteralPath (Join-Path $mac 'result.json') -Raw | ConvertFrom-Json
		if ($result.Identity -cne '' -or $result.Profile -cne '') { throw "清空配置后仍使用环境变量：$entry" }
		& (Join-Path $mac $entry) @parameters
		$result = Get-Content -LiteralPath (Join-Path $mac 'result.json') -Raw | ConvertFrom-Json
		if ($result.Identity -cne 'environment identity' -or $result.Profile -cne 'environment profile') { throw "未保留脚本环境变量默认行为：$entry" }
	}
	Write-Host 'macOS 签名参数传递检查通过。'
} finally {
	$env:APPLE_SIGNING_IDENTITY = $oldIdentity
	$env:APPLE_NOTARY_PROFILE = $oldProfile
	Remove-Item -LiteralPath $root -Recurse -Force
}