function Get-LinuxRuntimeIdentifier {
	param(
		[string]$RuntimeIdentifier
	)

	if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux) -and
		-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
		throw "Linux 发布脚本仅支持 Windows 或 Linux；Debian 制包在 Linux 或 WSL Debian 中运行。"
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

function Invoke-LinuxScriptInWsl {
	param(
		[Parameter(Mandatory)][string]$ScriptPath,
		[Parameter(Mandatory)][System.Collections.IDictionary]$Parameters
	)

	if (-not (Get-Command wsl.exe -CommandType Application -ErrorAction SilentlyContinue)) { throw "未找到 WSL，请先配置 Debian 和 PowerShell 7。" }
	$arguments = @("-NoLogo", "-NoProfile", "-File")
	$paths = @("ScriptPath", "RootDirectory", "PatchToolPath", "PublishDirectory", "OutputDirectory", "SourceDirectory")
	$values = [ordered]@{ ScriptPath = $ScriptPath }
	foreach ($key in $Parameters.Keys) { $values[$key] = $Parameters[$key] }
	foreach ($key in @($values.Keys)) {
		if ($key -in $paths -and -not [string]::IsNullOrWhiteSpace([string]$values[$key])) {
			if ([string]$values[$key] -match '^/') { continue }
			$windowsPath = [System.IO.Path]::GetFullPath([string]$values[$key])
			$linuxPath = & wsl.exe -d Debian --exec wslpath -a -u $windowsPath
			if ($LASTEXITCODE -ne 0) { throw "无法转换 WSL 路径：$windowsPath" }
			$values[$key] = ($linuxPath -join "`n").Trim()
			if ([string]::IsNullOrWhiteSpace($values[$key])) { throw "WSL 路径为空：$windowsPath" }
		}
	}
	$arguments += $values.ScriptPath
	foreach ($key in $Parameters.Keys) { $arguments += @("-$key", [string]$values[$key]) }
	$wslLaunchArguments = @("-d", "Debian", "--exec")
	if ($values.Contains("SourceDirectory") -and -not [string]::IsNullOrWhiteSpace([string]$values.SourceDirectory)) {
		$wslLaunchArguments += @("env", "CLIENT_PUBLISH_SOURCE_DIRECTORY=$([string]$values.SourceDirectory)", "pwsh")
	} else {
		$wslLaunchArguments += "pwsh"
	}
	& wsl.exe @wslLaunchArguments @arguments
	if ($LASTEXITCODE -ne 0) { throw "WSL Debian 脚本执行失败：$ScriptPath，退出代码：$LASTEXITCODE" }
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
	if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { return }
	& chmod +x $Path
	if ($LASTEXITCODE -ne 0) { throw "无法设置可执行权限：$Path" }
}
