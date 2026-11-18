$ErrorActionPreference = "Stop"
$scripts = Join-Path (Split-Path -Parent $PSScriptRoot) "ClientPublishAgent/Scripts/Desktop"
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("desktop-oss-check-" + [guid]::NewGuid().ToString("N"))
$configDirectory = Join-Path $root "win-package"
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
try {
	Copy-Item -LiteralPath (Join-Path $scripts "publish-root-config.ps1") -Destination $root
	. (Join-Path $root "publish-root-config.ps1")
	$config = Join-Path $configDirectory "publish-config.json"
	'{"RootDirectory":"D:\\Publish\\win-x64"}' | Set-Content -LiteralPath $config -Encoding utf8
	if ((Resolve-DesktopOssHost -RuntimeIdentifier win-x64) -cne "https://cloud.gildata.com/gil/upgrade/WarrenQ/WarrenqF1/win-x64") { throw "旧配置默认地址错误。" }
	'{"OssHost":"https://updates.example.test/custom/"}' | Set-Content -LiteralPath $config -Encoding utf8
	foreach ($runtime in @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")) {
		if ((Resolve-DesktopOssHost -RuntimeIdentifier $runtime) -cne "https://updates.example.test/custom/$runtime") { throw "运行时地址错误：$runtime" }
	}
	if ((Resolve-DesktopOssHost -RuntimeIdentifier linux-x64 -OssHost "http://override.test/full") -cne "http://override.test/full") { throw "显式地址被修改。" }
	foreach ($value in @('ftp://example.test/a', 'https://user:pass@example.test', 'https://example.test/a?token=x', 'https://example.test/linux-x64')) {
		@{ OssHost = $value } | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding utf8
		$rejected = $false
		try { Resolve-DesktopOssHost -RuntimeIdentifier linux-x64 | Out-Null } catch { $rejected = $true }
		if (-not $rejected) { throw "无效地址未拒绝：$value" }
	}
	foreach ($file in @("publish-root-config.ps1", "win-package/upgrade-win-x64.ps1", "linux-package/upgrade-linux.ps1", "mac-package/upgrade-mac.ps1")) {
		$tokens = $null; $errors = $null
		[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $scripts $file), [ref]$tokens, [ref]$errors) | Out-Null
		if ($errors.Count -gt 0) { throw "脚本语法错误：$file $errors" }
	}
	if ($IsWindows) {
		foreach ($name in @("upgrade-win-x64.ps1", "publish-config.ps1", "GeneralUpdate.Tools.Cmd.exe")) {
			Copy-Item -LiteralPath (Join-Path $scripts "win-package/$name") -Destination $configDirectory
		}
		Copy-Item -LiteralPath (Join-Path $scripts "backup-staging-release.ps1") -Destination $root
		'{"OssHost":"https://updates.example.test/custom"}' | Set-Content -LiteralPath $config -Encoding utf8
		$output = Join-Path $root "output"
		$old = Join-Path $output "OldRelease"
		$staging = Join-Path $output "StagingRelease"
		New-Item -ItemType Directory -Path $old, $staging -Force | Out-Null
		Set-Content -LiteralPath (Join-Path $old "sample.txt") -Value "old"
		Set-Content -LiteralPath (Join-Path $staging "sample.txt") -Value "new"
		& (Join-Path $configDirectory "upgrade-win-x64.ps1") -CurrentVersion 1.0.0 -NextVersion 1.0.1 -RootDirectory $output -BackupStagingRelease $false
		$patch = Join-Path $output "OutputPatchPackage"
		if (-not (Test-Path -LiteralPath (Join-Path $patch "patch_v1.0.0_to_v1.0.1.zip") -PathType Leaf)) { throw "未生成升级差分包。" }
		$ossConfig = Get-Content -LiteralPath (Join-Path $patch "oss_config.json") -Raw
		if (-not $ossConfig.Contains("https://updates.example.test/custom/win-x64")) { throw "升级配置未包含用户指定的地址：$ossConfig" }
	}
	if ($IsLinux -and [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::X64) {
		$linux = Join-Path $root "linux-package"
		New-Item -ItemType Directory -Path (Join-Path $linux "x64") -Force | Out-Null
		foreach ($name in @("upgrade-linux.ps1", "linux-common.ps1", "x64/GeneralUpdate.Tools.Cmd")) {
			Copy-Item -LiteralPath (Join-Path $scripts "linux-package/$name") -Destination (Join-Path $linux $name)
		}
		Copy-Item -LiteralPath (Join-Path $scripts "backup-staging-release.ps1") -Destination $root
		'{"OssHost":"https://updates.example.test/custom"}' | Set-Content -LiteralPath $config -Encoding utf8
		$output = Join-Path $root "output"
		$old = Join-Path $output "OldRelease"
		$staging = Join-Path $output "StagingRelease"
		New-Item -ItemType Directory -Path $old, $staging -Force | Out-Null
		Set-Content -LiteralPath (Join-Path $old "sample.txt") -Value "old"
		Set-Content -LiteralPath (Join-Path $staging "sample.txt") -Value "new"
		& (Join-Path $linux "upgrade-linux.ps1") -RuntimeIdentifier linux-x64 -CurrentVersion 1.0.0 -NextVersion 1.0.1 -RootDirectory $output -BackupStagingRelease $false
		$patch = Join-Path $output "OutputPatchPackage"
		if (-not (Test-Path -LiteralPath (Join-Path $patch "patch_v1.0.0_to_v1.0.1.zip") -PathType Leaf)) { throw "未生成 Linux 升级差分包。" }
		$ossConfig = Get-Content -LiteralPath (Join-Path $patch "oss_config.json") -Raw
		if (-not $ossConfig.Contains("https://updates.example.test/custom/linux-x64")) { throw "Linux 升级配置未包含用户指定的地址：$ossConfig" }
	}
	Write-Host "桌面升级地址脚本检查通过。"
} finally {
	Remove-Item -LiteralPath $root -Recurse -Force
}