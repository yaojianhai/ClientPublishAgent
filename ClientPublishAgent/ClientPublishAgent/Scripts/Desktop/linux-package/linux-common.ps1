function Get-LinuxRuntimeIdentifier {
	param(
		[string]$RuntimeIdentifier
	)

	if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
		throw "Linux 发布和打包脚本仅支持在 Linux 上运行。"
	}
	if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
		if ($RuntimeIdentifier -notin "linux-arm64", "linux-x64") { throw "不支持的 Linux 运行时标识：$RuntimeIdentifier" }
		return $RuntimeIdentifier
	}
	$detectedRuntimeIdentifier = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
		"Arm64" { "linux-arm64"; break }
		"X64" { "linux-x64"; break }
		default { throw "Linux 发布和打包脚本仅支持 ARM64 和 x64 处理器架构。" }
	}
	return $detectedRuntimeIdentifier
}

function Read-LinuxSemanticVersion {
	param([Parameter(Mandatory)][string]$Prompt, [string]$Value)

	if ([string]::IsNullOrWhiteSpace($Value)) { $Value = Read-Host $Prompt }
	if ($null -ne $Value) { $Value = $Value.Trim() }
	[version]$parsedVersion = $null
	if ($Value -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
		-not [version]::TryParse($Value, [ref]$parsedVersion)) {
		throw "版本号格式无效：$Value。请输入三段数字版本号，例如 1.2.3。"
	}
	return $Value
}

function Test-LinuxExecutable {
	param(
		[Parameter(Mandatory)][string]$Path,
		[Parameter(Mandatory)][ValidateSet("linux-arm64", "linux-x64")][string]$RuntimeIdentifier
	)

	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "未找到 Linux 可执行文件：$Path" }
	$header = [byte[]]::new(20)
	$stream = [System.IO.File]::OpenRead($Path)
	try { $bytesRead = $stream.Read($header, 0, $header.Length) } finally { $stream.Dispose() }
	if ($bytesRead -ne $header.Length -or $header[0] -ne 0x7F -or $header[1] -ne 0x45 -or $header[2] -ne 0x4C -or $header[3] -ne 0x46 -or $header[4] -ne 2 -or $header[5] -ne 1) {
		throw "文件不是受支持的 64 位小端 ELF 可执行文件：$Path"
	}
	$expectedMachine = switch ($RuntimeIdentifier) { "linux-arm64" { 183; break } "linux-x64" { 62; break } }
	$machine = [int]$header[18] -bor ([int]$header[19] -shl 8)
	if ($machine -ne $expectedMachine) { throw "可执行文件不支持目标运行时 $RuntimeIdentifier：$Path" }
}

function Set-LinuxExecutablePermission {
	param([Parameter(Mandatory)][string]$Path)

	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "未找到可执行文件：$Path" }
	& chmod +x $Path
	if ($LASTEXITCODE -ne 0) { throw "无法设置可执行权限：$Path" }
}
