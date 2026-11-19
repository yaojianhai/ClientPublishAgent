$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw '此检查需要在 Windows 上运行，以验证 Linux 脚本拒绝非 Linux 主机。' }
$linux = Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientPublishAgent\Scripts\Desktop\linux-package'
$root = Join-Path ([IO.Path]::GetTempPath()) ('linux-native-host-' + [guid]::NewGuid().ToString('N'))
try {
	$cases = @(
		@{ File = 'publish-linux.ps1'; Parameters = @{ RuntimeIdentifier = 'linux-x64'; RootDirectory = $root } },
		@{ File = 'publish-linux-arm64.ps1'; Parameters = @{ RuntimeIdentifier = 'linux-arm64'; RootDirectory = $root } },
		@{ File = 'linux-publish&upgrade.ps1'; Parameters = @{ RuntimeIdentifier = 'linux-x64'; CurrentVersion = '1.0.0'; NextVersion = '1.0.1' } },
		@{ File = 'linux-publish&upgrade&package.ps1'; Parameters = @{ RuntimeIdentifier = 'linux-x64'; CurrentVersion = '1.0.0'; NextVersion = '1.0.1'; RootDirectory = $root } },
		@{ File = 'linux-upgrade&package.ps1'; Parameters = @{ RuntimeIdentifier = 'linux-x64'; CurrentVersion = '1.0.0'; NextVersion = '1.0.1'; RootDirectory = $root } },
		@{ File = 'upgrade-linux.ps1'; Parameters = @{ RuntimeIdentifier = 'linux-x64'; CurrentVersion = '1.0.0'; NextVersion = '1.0.1'; RootDirectory = $root } },
		@{ File = 'package-linux.ps1'; Parameters = @{ RuntimeIdentifier = 'linux-x64'; RootDirectory = $root } },
		@{ File = 'backup-linux-staging.ps1'; Parameters = @{ RootDirectory = $root; Version = '1.0.1' } }
	)
	foreach ($case in $cases) {
		$rejected = $false
		$parameters = $case.Parameters
		try { & (Join-Path $linux $case.File) @parameters } catch { $rejected = $_.Exception.Message.Contains('仅支持在 Linux 上运行') }
		if (-not $rejected) { throw "脚本未明确拒绝 Windows 主机：$($case.File)" }
	}
	if (Test-Path -LiteralPath $root) { throw '拒绝执行前修改了输出目录。' }
	Write-Host 'Linux 原生主机限制检查通过。'
} finally {
	if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}