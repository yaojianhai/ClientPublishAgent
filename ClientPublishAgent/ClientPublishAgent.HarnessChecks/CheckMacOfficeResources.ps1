$ErrorActionPreference = 'Stop'
if (-not ($IsLinux -or $IsMacOS)) { throw '此检查需要 macOS 或 Linux。' }
$scripts = Join-Path (Split-Path -Parent $PSScriptRoot) 'ClientPublishAgent/Scripts/Desktop/mac-package'
$root = Join-Path ([IO.Path]::GetTempPath()) ('mac-office-check-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'Test.app'
$macos = Join-Path $app 'Contents/MacOS'
$resources = Join-Path $app 'Contents/Resources'
$office = Join-Path $macos 'Addin/Office'
try {
	New-Item -ItemType Directory -Path (Join-Path $office 'packages/onlyoffice/9.4.0-develop'), $resources -Force | Out-Null
	Set-Content -LiteralPath (Join-Path $office 'index.html') -Value 'office-home'
	Set-Content -LiteralPath (Join-Path $office 'packages/onlyoffice/9.4.0-develop/example.js') -Value 'office-sdk'
	Set-Content -LiteralPath (Join-Path $macos 'unrelated.txt') -Value 'unchanged'
	. (Join-Path $scripts 'mac-office-resources.ps1')
	Move-MacOfficeResources -MacOsDirectory $macos -ResourcesDirectory $resources
	$link = Get-Item -LiteralPath $office -Force
	if (-not ($link.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '原 Office 路径不是符号链接。' }
	if ($link.LinkTarget -cne '../../Resources/Addin/Office') { throw "Office 链接目标不正确：$($link.LinkTarget)" }
	if ((Get-Content -LiteralPath (Join-Path $office 'index.html') -Raw).Trim() -cne 'office-home' -or
		(Get-Content -LiteralPath (Join-Path $office 'packages/onlyoffice/9.4.0-develop/example.js') -Raw).Trim() -cne 'office-sdk') {
		throw '原运行时路径不能读取 Office 资源。'
	}
	if (-not (Test-Path -LiteralPath (Join-Path $resources 'Addin/Office/packages/onlyoffice/9.4.0-develop/example.js')) -or
		-not (Test-Path -LiteralPath (Join-Path $macos 'unrelated.txt'))) { throw '资源迁移或其他文件保留失败。' }
	if ($IsMacOS) {
		$arch = (& uname -m).Trim()
		$toolArch = switch ($arch) { 'arm64' { 'arm64' } 'x86_64' { 'x64' } default { throw "不支持的架构：$arch" } }
		Copy-Item -LiteralPath (Join-Path $scripts "$toolArch/GeneralUpdate.Tools.Cmd") -Destination (Join-Path $macos 'Test')
		& chmod +x (Join-Path $macos 'Test')
		if ($LASTEXITCODE -ne 0) { throw '无法设置测试程序权限。' }
		$plist = '<?xml version="1.0" encoding="UTF-8"?><!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd"><plist version="1.0"><dict><key>CFBundleExecutable</key><string>Test</string><key>CFBundleIdentifier</key><string>com.example.officecheck</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>'
		[IO.File]::WriteAllText((Join-Path $app 'Contents/Info.plist'), $plist)
		& codesign --force --deep --sign - $app
		if ($LASTEXITCODE -ne 0) { throw '包含 OnlyOffice 资源的应用签名失败。' }
		& ditto $app (Join-Path $root 'Copy.app')
		if ($LASTEXITCODE -ne 0) { throw '复制签名应用失败。' }
		& codesign --verify --deep --strict (Join-Path $root 'Copy.app')
		if ($LASTEXITCODE -ne 0) { throw '复制后的应用签名验证失败。' }
		if ((Get-Content -LiteralPath (Join-Path $root 'Copy.app/Contents/MacOS/Addin/Office/index.html') -Raw).Trim() -cne 'office-home') { throw '复制后 Office 路径不可用。' }
	}
	Write-Host 'macOS Office 资源布局检查通过。'
} finally {
	if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}