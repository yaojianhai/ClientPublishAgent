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

function Initialize-WslProcessHelper {
	if ("ClientPublishAgent.Scripts.WslProcess" -as [type]) { return }
	Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClientPublishAgent.Scripts
{
	public sealed class WslProcessResult
	{
		public int ExitCode { get; set; }
		public string StandardOutput { get; set; }
	}

	public static class WslProcess
	{
		private static int proxyWarningReported;

		public static WslProcessResult Run(string[] arguments, bool captureStandardOutput)
		{
			Console.OutputEncoding = new UTF8Encoding(false);
			Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
			var startInfo = new ProcessStartInfo
			{
				FileName = "wsl.exe",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

			using (var process = new Process { StartInfo = startInfo })
			using (var capturedOutput = captureStandardOutput ? new MemoryStream() : null)
			{
				if (!process.Start()) throw new InvalidOperationException("无法启动 wsl.exe。");
				var outputTask = captureStandardOutput
					? process.StandardOutput.BaseStream.CopyToAsync(capturedOutput)
					: process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
				var errorTask = ForwardErrorAsync(process.StandardError.BaseStream);
				process.WaitForExit();
				Task.WaitAll(outputTask, errorTask);
				return new WslProcessResult
				{
					ExitCode = process.ExitCode,
					StandardOutput = captureStandardOutput ? Encoding.UTF8.GetString(capturedOutput.ToArray()) : string.Empty
				};
			}
		}

		private static async Task ForwardErrorAsync(Stream stream)
		{
			var buffer = new byte[4096];
			var line = new List<byte>();
			var skipUtf16LineTerminator = false;
			int count;
			while ((count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
			{
				for (var index = 0; index < count; index++)
				{
					if (skipUtf16LineTerminator)
					{
						skipUtf16LineTerminator = false;
						if (buffer[index] == 0) continue;
					}
					if (buffer[index] == (byte)'\n')
					{
						skipUtf16LineTerminator = IsUtf16LittleEndian(line);
						WriteErrorLine(line);
						line.Clear();
					}
					else line.Add(buffer[index]);
				}
			}
			if (line.Count > 0) WriteErrorLine(line);
		}

		private static void WriteErrorLine(List<byte> line)
		{
			var bytes = line.ToArray();
			var encoding = IsUtf16LittleEndian(line) ? Encoding.Unicode : Encoding.UTF8;
			var message = encoding.GetString(bytes).TrimEnd('\r', '\0');
			if (string.IsNullOrEmpty(message)) return;
			if (IsProxyWarning(message) && Interlocked.Exchange(ref proxyWarningReported, 1) != 0) return;
			Console.Error.WriteLine(message);
		}

		private static bool IsUtf16LittleEndian(List<byte> line)
		{
			var oddNulls = 0;
			for (var index = 1; index < line.Count; index += 2)
				if (line[index] == 0) oddNulls++;
			return line.Count >= 4 && oddNulls * 4 >= line.Count;
		}

		private static bool IsProxyWarning(string message)
		{
			return message.StartsWith("wsl:", StringComparison.OrdinalIgnoreCase) &&
				message.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0 &&
				message.IndexOf("WSL", StringComparison.OrdinalIgnoreCase) >= 0 &&
				(message.IndexOf("NAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 message.IndexOf("代理", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 message.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0);
		}
	}
}
'@
}

function Invoke-WslProcess {
	param([Parameter(Mandatory)][string[]]$Arguments, [switch]$CaptureStandardOutput)
	return [ClientPublishAgent.Scripts.WslProcess]::Run($Arguments, $CaptureStandardOutput.IsPresent)
}

function Invoke-LinuxScriptInWsl {
	param(
		[Parameter(Mandatory)][string]$ScriptPath,
		[Parameter(Mandatory)][System.Collections.IDictionary]$Parameters
	)

	if (-not (Get-Command wsl.exe -CommandType Application -ErrorAction SilentlyContinue)) { throw "未找到 WSL，请先配置 Debian 和 PowerShell 7。" }
	Initialize-WslProcessHelper
	$arguments = @("-NoLogo", "-NoProfile", "-File")
	$paths = @("ScriptPath", "RootDirectory", "PatchToolPath", "PublishDirectory", "OutputDirectory", "SourceDirectory")
	$values = [ordered]@{ ScriptPath = $ScriptPath }
	foreach ($key in $Parameters.Keys) { $values[$key] = $Parameters[$key] }
	foreach ($key in @($values.Keys)) {
		if ($key -in $paths -and -not [string]::IsNullOrWhiteSpace([string]$values[$key])) {
			if ([string]$values[$key] -match '^/') { continue }
			$windowsPath = [System.IO.Path]::GetFullPath([string]$values[$key])
			$conversion = Invoke-WslProcess -Arguments @("-d", "Debian", "--exec", "wslpath", "-a", "-u", $windowsPath) -CaptureStandardOutput
			if ($conversion.ExitCode -ne 0) { throw "无法转换 WSL 路径：$windowsPath" }
			$values[$key] = $conversion.StandardOutput.Trim()
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
	$wslResult = Invoke-WslProcess -Arguments ($wslLaunchArguments + $arguments)
	if ($wslResult.ExitCode -ne 0) { throw "WSL Debian 脚本执行失败：$ScriptPath，退出代码：$($wslResult.ExitCode)" }
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
