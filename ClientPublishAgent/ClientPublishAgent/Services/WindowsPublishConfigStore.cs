using System;
using System.IO;
using System.Text.Json;

namespace ClientPublishAgent.Services;

public sealed class WindowsPublishConfigStore
{
	private readonly string _configPath;
	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true, PropertyNamingPolicy = null };

	public string RootDirectory { get; private set; } = "";
	public string LinuxRootDirectory { get; private set; } = "";
	public string MacOSRootDirectory { get; private set; } = "";

	public WindowsPublishConfigStore(string? configPath = null)
	{
		_configPath = configPath ?? ConfigPath;
		if (!File.Exists(_configPath)) return;

		var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(_configPath));
		RootDirectory = config?.RootDirectory ?? "";
		LinuxRootDirectory = config?.LinuxRootDirectory ?? "";
		MacOSRootDirectory = config?.MacOSRootDirectory ?? "";
	}

	public void SaveRootDirectory(string rootDirectory)
	{
		if (string.IsNullOrWhiteSpace(rootDirectory) || !IsWindowsAbsolutePath(rootDirectory.Trim()))
			throw new ArgumentException("请输入 Windows 绝对路径，例如 D:\\Publish\\WarrenqF1\\win-x64。", nameof(rootDirectory));

		var path = rootDirectory.Trim();
		SaveConfiguration(new Configuration(path, LinuxRootDirectory, MacOSRootDirectory));
		RootDirectory = path;
	}

	public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Scripts", "Desktop", "win-package", "publish-config.json");

	public void SaveLinuxRootDirectory(string rootDirectory) => SavePlatformRootDirectory(rootDirectory, true);

	public void SaveMacOSRootDirectory(string rootDirectory) => SavePlatformRootDirectory(rootDirectory, false);

	private void SavePlatformRootDirectory(string rootDirectory, bool isLinux)
	{
		if (string.IsNullOrWhiteSpace(rootDirectory))
			throw new ArgumentException("请输入绝对路径。", nameof(rootDirectory));

		var path = rootDirectory.Trim();
		if (!Path.IsPathFullyQualified(path) && !path.StartsWith('/') && !IsWindowsAbsolutePath(path))
			throw new ArgumentException("请输入绝对路径。", nameof(rootDirectory));

		var linuxRootDirectory = isLinux ? path : LinuxRootDirectory;
		var macOSRootDirectory = isLinux ? MacOSRootDirectory : path;
		SaveConfiguration(new Configuration(RootDirectory, linuxRootDirectory, macOSRootDirectory));
		LinuxRootDirectory = linuxRootDirectory;
		MacOSRootDirectory = macOSRootDirectory;
	}

	private void SaveConfiguration(Configuration configuration)
	{
		var directory = Path.GetDirectoryName(_configPath) ?? throw new InvalidOperationException("无法解析 publish-config.json 所在目录。");
		Directory.CreateDirectory(directory);
		var temporary = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, SerializerOptions));
			File.Move(temporary, _configPath, true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private static bool IsWindowsAbsolutePath(string path) =>
		(path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) ||
		path.StartsWith("\\\\", StringComparison.Ordinal);

	private sealed record Configuration(string RootDirectory, string LinuxRootDirectory = "", string MacOSRootDirectory = "");
}