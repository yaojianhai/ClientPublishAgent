using System;
using System.IO;
using System.Text.Json;

namespace ClientPublishAgent.Services;

public sealed class PackagingPathsStore
{
	private readonly string _configPath;

	public string SourceDirectory { get; private set; } = "";
	public static event Action<string>? SourceDirectoryChanged;

	public PackagingPathsStore(string? configPath = null)
	{
		_configPath = configPath ?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"PersonalAgentPlatform", "packaging-paths.json");
		if (!File.Exists(_configPath)) return;
		var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(_configPath));
		if (config?.SourceDirectory is { Length: > 0 } directory && Path.IsPathFullyQualified(directory))
			SourceDirectory = Path.GetFullPath(directory);
	}

	public void SaveSourceDirectory(string sourceDirectory)
	{
		if (string.IsNullOrWhiteSpace(sourceDirectory) || !Path.IsPathFullyQualified(sourceDirectory))
			throw new ArgumentException("请输入 src 目录的绝对路径。", nameof(sourceDirectory));
		var path = Path.GetFullPath(sourceDirectory.Trim());
		if (!Directory.Exists(path)) throw new DirectoryNotFoundException("src 目录不存在：" + path);

		Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
		var temporary = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(temporary, JsonSerializer.Serialize(new Configuration(path)));
			File.Move(temporary, _configPath, true);
			SourceDirectory = path;
			SourceDirectoryChanged?.Invoke(path);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private sealed record Configuration(string SourceDirectory);
}
