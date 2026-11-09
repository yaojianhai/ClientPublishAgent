using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClientPublishAgent.Services;

public sealed class PowerShellTaskRunner
{
	private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
	private readonly SemaphoreSlim _inputGate = new(1, 1);
	private readonly object _inputSync = new();
	private StreamWriter? _input;

	/// <summary>Reports whether the currently running process accepts local standard input.</summary>
	public event Action<bool>? InputAvailabilityChanged;

	public async Task<int> RunAsync(string scriptPath, string currentVersion, string nextVersion,
		string ossHost, IProgress<string> output, CancellationToken cancellationToken,
		IReadOnlyDictionary<string, string?>? environment = null)
	{
		var arguments = new List<string> { "-NoProfile", "-File", scriptPath,
			"-CurrentVersion", currentVersion, "-NextVersion", nextVersion };
		if (OperatingSystem.IsWindows()) { arguments.InsertRange(1, ["-ExecutionPolicy", "Bypass"]); }
		if (!string.IsNullOrWhiteSpace(ossHost)) { arguments.Add("-OssHost"); arguments.Add(ossHost); }
		return await RunProcessAsync(ResolvePowerShell(), arguments, Path.GetDirectoryName(scriptPath)!, output,
			cancellationToken, interactive: true, environment: environment);
	}

	/// <summary>Writes one line to the active interactive process without recording it in task output.</summary>
	public async Task SendInputAsync(string input, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		if (input.Contains('\r') || input.Contains('\n')) throw new ArgumentException("Input must be a single line.", nameof(input));
		await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			StreamWriter writer;
			lock (_inputSync) writer = _input ?? throw new InvalidOperationException("No interactive task is running.");
			try
			{
				await writer.WriteLineAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
				await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or ObjectDisposedException)
			{
				throw new InvalidOperationException("The task no longer accepts input.", ex);
			}
		}
		finally { _inputGate.Release(); }
	}

	public static string ResolvePowerShell()
	{
		var path = FindPowerShell7();
		if (path is not null) return path;
		if (OperatingSystem.IsWindows())
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
		throw new PlatformNotSupportedException("未找到 PowerShell 7 (pwsh)。请先安装 PowerShell 7 并将其目录加入 PATH，然后重启应用。");
	}

	internal static string ResolvePowerShell7() => FindPowerShell7() ??
		throw new PlatformNotSupportedException(OperatingSystem.IsWindows()
			? "未找到 Windows 版 PowerShell 7 (pwsh)。请安装 PowerShell 7 或将其安装目录加入 PATH，然后重启应用。Windows 上执行 Linux 打包时，WSL Debian 内也需要安装 PowerShell 7。"
			: "未找到 PowerShell 7 (pwsh)。请先安装 PowerShell 7 并将其目录加入 PATH，然后重启应用。");

	private static string? FindPowerShell7()
	{
		foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
		{
			if (string.IsNullOrWhiteSpace(directory)) continue;
			var path = Path.Combine(directory.Trim('"'), OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
			if (File.Exists(path)) return Path.GetFullPath(path);
		}
		if (OperatingSystem.IsWindows())
		{
			foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
			{
				var programFiles = Environment.GetFolderPath(folder);
				if (string.IsNullOrEmpty(programFiles)) continue;
				var path = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
				if (File.Exists(path)) return path;
			}
		}
		return null;
	}

	public async Task<int> RunProcessAsync(string executable, IEnumerable<string> arguments, string directory,
		IProgress<string> output, CancellationToken cancellationToken, bool interactive = false,
		IReadOnlyDictionary<string, string?>? environment = null)
	{
		var processArguments = arguments.ToArray();
		if (interactive && (Path.GetFileNameWithoutExtension(executable).Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
			Path.GetFileNameWithoutExtension(executable).Equals("pwsh", StringComparison.OrdinalIgnoreCase)))
			processArguments = PrepareInteractivePowerShellArguments(processArguments);
		await ExecutionGate.WaitAsync(cancellationToken);
		try { return await RunCoreAsync(executable, processArguments, directory, output, cancellationToken, interactive, environment); }
		finally { ExecutionGate.Release(); }
	}

	private static string[] PrepareInteractivePowerShellArguments(string[] arguments)
	{
		var fileIndex = Array.FindIndex(arguments, a => a.Equals("-File", StringComparison.OrdinalIgnoreCase));
		var commandIndex = Array.FindIndex(arguments, a => a.Equals("-Command", StringComparison.OrdinalIgnoreCase));
		var index = fileIndex >= 0 ? fileIndex : commandIndex;
		if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException("Interactive PowerShell requires a script or command.", nameof(arguments));
		// Windows PowerShell can hide Read-Host prompts when its input is redirected.
		const string readHost = "$utf8 = New-Object System.Text.UTF8Encoding($false); [Console]::InputEncoding = $utf8; [Console]::OutputEncoding = $utf8; " +
			"function global:Read-Host { [CmdletBinding()] param([Parameter(Position=0)][object]$Prompt, [switch]$AsSecureString, [switch]$MaskInput) " +
			"if ($AsSecureString -or $MaskInput) { throw 'Secure input is not supported in redirected tasks.' }; " +
			"if ($null -ne $Prompt) { [Console]::Out.Write(([string]$Prompt) + ': ') }; [Console]::Out.Flush(); " +
			"$line = [Console]::In.ReadLine(); if ($null -eq $line) { throw 'Task input was closed.' }; $line }; ";
		string command;
		if (fileIndex >= 0)
		{
			static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
			var invocation = new StringBuilder("& ").Append(Quote(arguments[index + 1]));
			for (var i = index + 2; i < arguments.Length; i += 2)
			{
				if (i + 1 >= arguments.Length || !Regex.IsMatch(arguments[i], @"^-[A-Za-z][A-Za-z0-9]*$"))
					throw new ArgumentException("Invalid interactive script arguments.", nameof(arguments));
				invocation.Append(' ').Append(arguments[i]).Append(' ').Append(Quote(arguments[i + 1]));
			}
			command = readHost + "$global:LASTEXITCODE = $null; " + invocation +
				"; if (-not $?) { if ($null -ne $global:LASTEXITCODE) { exit $global:LASTEXITCODE }; exit 1 }";
		}
		else
		{
			if (index + 2 != arguments.Length) throw new ArgumentException("Invalid interactive command arguments.", nameof(arguments));
			command = readHost + arguments[index + 1];
		}
		return arguments.Take(index).Concat(["-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))]).ToArray();
	}

	private async Task<int> RunCoreAsync(string executable, IEnumerable<string> arguments, string directory,
		IProgress<string> output, CancellationToken cancellationToken, bool interactive,
		IReadOnlyDictionary<string, string?>? environment)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var startInfo = new ProcessStartInfo
		{
			FileName = executable,
			WorkingDirectory = directory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true
		};
		if (environment is not null)
			foreach (var entry in environment) startInfo.Environment[entry.Key] = entry.Value;
		if (interactive && (Path.GetFileNameWithoutExtension(executable).Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
			Path.GetFileNameWithoutExtension(executable).Equals("pwsh", StringComparison.OrdinalIgnoreCase)))
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			startInfo.StandardInputEncoding = new UTF8Encoding(false);
			startInfo.StandardOutputEncoding = Encoding.UTF8;
			startInfo.StandardErrorEncoding = Path.GetFileNameWithoutExtension(executable).Equals("powershell", StringComparison.OrdinalIgnoreCase)
				? Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage) : Encoding.UTF8;
		}
		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}
		using var process = new Process { StartInfo = startInfo };
		process.Start();
		var stdout = ReadOutputAsync(process.StandardOutput, output);
		var isPowerShell = Path.GetFileNameWithoutExtension(executable).Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
			Path.GetFileNameWithoutExtension(executable).Equals("pwsh", StringComparison.OrdinalIgnoreCase);
		var stderr = interactive && isPowerShell
			? ReadPowerShellErrorAsync(process.StandardError, output)
			: ReadOutputAsync(process.StandardError, output);
		try
		{
			if (interactive)
			{
				lock (_inputSync) _input = process.StandardInput;
				InputAvailabilityChanged?.Invoke(true);
			}
			else process.StandardInput.Close();
			try
			{
				await process.WaitForExitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				try
				{
					if (!process.HasExited) process.Kill(entireProcessTree: true);
				}
				catch (InvalidOperationException) when (process.HasExited) { }
				catch (Win32Exception ex)
				{
					if (!process.HasExited)
					{
						output.Report($"无法终止任务：{ex.Message}。将继续等待任务结束。");
						await process.WaitForExitAsync();
						await Task.WhenAll(stdout, stderr);
						return process.ExitCode;
					}
				}
				await process.WaitForExitAsync();
				await Task.WhenAll(stdout, stderr);
				throw;
			}

			await Task.WhenAll(stdout, stderr);
			return process.ExitCode;
		}
		catch
		{
			try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
			catch (InvalidOperationException) when (process.HasExited) { }
			await process.WaitForExitAsync();
			throw;
		}
		finally
		{
			if (interactive)
			{
				lock (_inputSync) _input = null;
				InputAvailabilityChanged?.Invoke(false);
			}
		}
	}

	private static async Task ReadOutputAsync(StreamReader reader, IProgress<string> output)
	{
		var buffer = new char[1024];
		int count;
		while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
			output.Report(new string(buffer, 0, count));
	}

	private static async Task ReadPowerShellErrorAsync(StreamReader reader, IProgress<string> output)
	{
		const string header = "#< CLIXML";
		const int maxClixmlChars = 8 * 1024 * 1024;
		var initial = new StringBuilder();
		var clixml = new StringBuilder();
		var buffer = new char[1024];
		var state = 0;
		var count = 0;
		while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
		{
			var chunk = new string(buffer, 0, count);
			if (state == 2)
			{
				output.Report(chunk);
				continue;
			}
			if (state == 3) continue;
			if (state == 0)
			{
				initial.Append(chunk);
				var value = initial.ToString();
				if (value.StartsWith(header, StringComparison.Ordinal))
				{
					state = 1;
					clixml.Append(value);
					initial.Clear();
				}
				else if (!header.StartsWith(value, StringComparison.Ordinal) || value.Length >= header.Length)
				{
					state = 2;
					output.Report(value);
					initial.Clear();
				}
				continue;
			}
			clixml.Append(chunk);
			if (clixml.Length > maxClixmlChars)
			{
				clixml.Clear();
				state = 3;
				output.Report("PowerShell 结构化日志超过 8 MiB，已停止读取该日志流。\n");
			}
		}
		if (state == 0 && initial.Length > 0) output.Report(initial.ToString());
		if (state != 1) return;
		var serialized = clixml.ToString();
		var start = serialized.IndexOf("<Objs", StringComparison.Ordinal);
		var end = serialized.LastIndexOf("</Objs>", StringComparison.Ordinal);
		if (start < 0 || end < start)
		{
			output.Report("无法解析 PowerShell 结构化日志：缺少 CLIXML 根节点。\n");
			return;
		}
		try
		{
			var document = XDocument.Parse(serialized[start..(end + "</Objs>".Length)]);
			foreach (var entry in document.Root!.Elements())
			{
				var stream = (string?)entry.Attribute("S");
				string? message = stream switch
				{
					"progress" => FormatProgress(entry),
					"information" => FindNamedValue(entry, "MessageData") ?? FindNamedValue(entry, "ToString"),
					"warning" or "verbose" or "debug" => entry.Value,
					"error" => FindNamedValue(entry, "ToString") ?? entry.Descendants().FirstOrDefault(element => element.Name.LocalName == "ToString")?.Value ?? entry.Value,
					_ => FindNamedValue(entry, "ToString") ?? FindNamedValue(entry, "MessageData")
				};
				if (!string.IsNullOrWhiteSpace(message)) output.Report(message.TrimEnd() + Environment.NewLine);
			}
		}
		catch (System.Xml.XmlException)
		{
			output.Report("无法解析 PowerShell 结构化日志：CLIXML 格式无效。\n");
		}
	}

	private static string? FormatProgress(XElement entry)
	{
		var record = entry.Descendants().FirstOrDefault(element => element.Name.LocalName == "PR");
		if (record is null) return null;
		var activity = record.Elements().FirstOrDefault(element => element.Name.LocalName == "AV")?.Value;
		var status = record.Elements().FirstOrDefault(element => element.Name.LocalName == "SD")?.Value;
		var percent = record.Elements().FirstOrDefault(element => element.Name.LocalName == "PC")?.Value;
		var details = new[] { activity, status, percent is not null and not "-1" ? percent + "%" : null }
			.Where(value => !string.IsNullOrWhiteSpace(value));
		return string.Join(" — ", details);
	}

	private static string? FindNamedValue(XElement entry, string name) => entry.Descendants()
		.FirstOrDefault(element => (element.Name.LocalName is "S" or "ToString") && (string?)element.Attribute("N") == name)?.Value;
}
