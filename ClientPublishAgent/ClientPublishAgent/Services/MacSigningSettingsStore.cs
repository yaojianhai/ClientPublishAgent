using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClientPublishAgent.Services;

public sealed class MacSigningSettingsStore
{
	private readonly string _path;

	public MacSigningSettingsStore(string? path = null) => _path = path ?? Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"PersonalAgentPlatform", "mac-signing-settings.json");

	public Settings? Load() => File.Exists(_path)
		? JsonSerializer.Deserialize(File.ReadAllText(_path), MacSigningSettingsJsonContext.Default.Settings)
			?? throw new InvalidDataException("macOS 签名配置为空。")
		: null;

	public void Save(string signingIdentity, string notaryKeychainProfile)
	{
		var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("无法解析 macOS 签名配置目录。");
		Directory.CreateDirectory(directory);
		var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(signingIdentity, notaryKeychainProfile), MacSigningSettingsJsonContext.Default.Settings));
			File.Move(temporary, _path, true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	public sealed record Settings(string SigningIdentity, string NotaryKeychainProfile);
}

[JsonSerializable(typeof(MacSigningSettingsStore.Settings))]
internal partial class MacSigningSettingsJsonContext : JsonSerializerContext;