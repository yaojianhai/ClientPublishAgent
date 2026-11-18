using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClientPublishAgent.Services;

public sealed class WindowsPublishConfigStore
{
	public const string DefaultOssHost = "https://cloud.gildata.com/gil/upgrade/WarrenQ/WarrenqF1";
	private readonly string _configPath;
	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true, PropertyNamingPolicy = null };

	public string RootDirectory { get; private set; } = "";
	public string LinuxRootDirectory { get; private set; } = "";
	public string MacOSRootDirectory { get; private set; } = "";
	public string OssHost { get; private set; } = DefaultOssHost;
	public static event Action? ConfigurationChanged;

	public WindowsPublishConfigStore(string? configPath = null)
	{
		_configPath = configPath ?? ConfigPath;
		Load();
	}

	private void Load()
	{
		if (!File.Exists(_configPath)) return;

		var config = JsonSerializer.Deserialize(File.ReadAllText(_configPath), WindowsPublishConfigJsonContext.Default.Configuration);
		RootDirectory = config?.RootDirectory ?? "";
		LinuxRootDirectory = config?.LinuxRootDirectory ?? "";
		MacOSRootDirectory = config?.MacOSRootDirectory ?? "";
		OssHost = string.IsNullOrWhiteSpace(config?.OssHost) ? DefaultOssHost : ValidateOssHost(config.OssHost);
	}

	public static string ValidateOssHost(string value)
	{
		var address = value?.Trim() ?? "";
		if (address.Length > 2048 || address.Any(char.IsControl) ||
			!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
			string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
			!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
			throw new ArgumentException("请输入不包含账号、查询参数或片段的 HTTP/HTTPS 升级基础地址。", nameof(value));
		var lastSegment = uri.AbsolutePath.TrimEnd('/').Split('/').Last();
		if (lastSegment is "win-x64" or "linux-x64" or "linux-arm64" or "osx-x64" or "osx-arm64")
			throw new ArgumentException("请填写不包含平台和架构后缀的升级基础地址。", nameof(value));
		return address.TrimEnd('/');
	}

	public void SaveOssHost(string value)
	{
		var address = ValidateOssHost(value);
		Load();
		SaveConfiguration(new Configuration(RootDirectory, LinuxRootDirectory, MacOSRootDirectory, address));
		OssHost = address;
	}

	public void SaveRootDirectory(string rootDirectory)
	{
		if (string.IsNullOrWhiteSpace(rootDirectory) || !IsWindowsAbsolutePath(rootDirectory.Trim()))
			throw new ArgumentException("请输入 Windows 绝对路径，例如 D:\\Publish\\WarrenqF1\\win-x64。", nameof(rootDirectory));

		var path = rootDirectory.Trim();
		Load();
		SaveConfiguration(new Configuration(path, LinuxRootDirectory, MacOSRootDirectory, OssHost));
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

		Load();
		var linuxRootDirectory = isLinux ? path : LinuxRootDirectory;
		var macOSRootDirectory = isLinux ? MacOSRootDirectory : path;
		SaveConfiguration(new Configuration(RootDirectory, linuxRootDirectory, macOSRootDirectory, OssHost));
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
			File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, WindowsPublishConfigJsonContext.Default.Configuration));
			File.Move(temporary, _configPath, true);
			ConfigurationChanged?.Invoke();
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private static bool IsWindowsAbsolutePath(string path) =>
		(path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) ||
		path.StartsWith("\\\\", StringComparison.Ordinal);

	internal sealed record Configuration(string RootDirectory, string LinuxRootDirectory = "", string MacOSRootDirectory = "", string OssHost = "");
}

[System.Text.Json.Serialization.JsonSerializable(typeof(WindowsPublishConfigStore.Configuration))]
internal partial class WindowsPublishConfigJsonContext : JsonSerializerContext
{
}