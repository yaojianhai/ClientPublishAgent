using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClientPublishAgent.Services;

public sealed class PowerShellTaskRunner
{
	private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

	public async Task<int> RunAsync(string scriptPath, string currentVersion, string nextVersion,
		string ossHost, IProgress<string> output, CancellationToken cancellationToken)
	{
		var arguments = new List<string> { "-NoProfile", "-NonInteractive", "-File", scriptPath,
			"-CurrentVersion", currentVersion, "-NextVersion", nextVersion };
		if (OperatingSystem.IsWindows()) { arguments.InsertRange(2, ["-ExecutionPolicy", "Bypass"]); }
		if (!string.IsNullOrWhiteSpace(ossHost)) { arguments.Add("-OssHost"); arguments.Add(ossHost); }
		return await RunProcessAsync(ResolvePowerShell(), arguments, Path.GetDirectoryName(scriptPath)!, output, cancellationToken);
	}

	public static string ResolvePowerShell()
	{
		foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
		{
			var path = Path.Combine(directory.Trim('"'), OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
			if (File.Exists(path)) return Path.GetFullPath(path);
		}
		if (OperatingSystem.IsWindows())
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
		throw new PlatformNotSupportedException("Linux/macOS require PowerShell 7 (pwsh) on PATH.");
	}

	public async Task<int> RunProcessAsync(string executable, IEnumerable<string> arguments, string directory,
		IProgress<string> output, CancellationToken cancellationToken)
	{
		await ExecutionGate.WaitAsync(cancellationToken);
		try { return await RunCoreAsync(executable, arguments, directory, output, cancellationToken); }
		finally { ExecutionGate.Release(); }
	}

	private static async Task<int> RunCoreAsync(string executable, IEnumerable<string> arguments, string directory,
		IProgress<string> output, CancellationToken cancellationToken)
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
		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}
		using var process = new Process { StartInfo = startInfo };
		process.Start();
		process.StandardInput.Close();
		var stdout = ReadOutputAsync(process.StandardOutput, output);
		var stderr = ReadOutputAsync(process.StandardError, output);
		try
		{
			await process.WaitForExitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			try
			{
				if (!process.HasExited)
					process.Kill(entireProcessTree: true);
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

	private static async Task ReadOutputAsync(StreamReader reader, IProgress<string> output)
	{
		while (await reader.ReadLineAsync() is { } line)
			output.Report(line);
	}
}
