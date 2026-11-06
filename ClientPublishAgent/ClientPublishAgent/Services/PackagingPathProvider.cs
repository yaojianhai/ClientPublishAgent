using System;
using System.IO;

namespace ClientPublishAgent.Services;

public static class PackagingPathProvider
{
	public static string ConfigurationPath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"PersonalAgentPlatform", "packaging-paths.json");

	public static string SourceDirectory => new PackagingPathsStore().SourceDirectory;

	public static string ResolveRepositoryRoot(string sourceDirectory)
	{
		if (string.IsNullOrWhiteSpace(sourceDirectory) || !Path.IsPathFullyQualified(sourceDirectory))
			throw new InvalidOperationException("请先在“打包目录配置”中设置 src 目录。");
		var source = Path.GetFullPath(sourceDirectory);
		if (!Directory.Exists(source)) throw new DirectoryNotFoundException("src 目录不存在：" + source);
		return Directory.GetParent(source)?.FullName ?? throw new InvalidOperationException("无法解析仓库根目录。");
	}
}
