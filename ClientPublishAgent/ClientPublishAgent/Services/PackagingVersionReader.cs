using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClientPublishAgent.Services;

public static class PackagingVersionReader
{
	private static readonly Regex PatchFileNamePattern = new(
		@"^patch_v(?<from>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))_to_v(?<to>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))\.zip$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	public static (string CurrentVersion, string NextVersion)? GetLatestPatchVersions(string outputRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
		var patchDirectory = Path.Combine(outputRoot, "OutputPatchPackage");
		if (!Directory.Exists(patchDirectory)) return null;

		var latestPatch = Directory.EnumerateFiles(patchDirectory)
			.Select(path => new FileInfo(path))
			.Select(file => (File: file, Match: PatchFileNamePattern.Match(file.Name)))
			.Where(item => item.Match.Success)
			.OrderByDescending(item => item.File.LastWriteTimeUtc)
			.ThenByDescending(item => item.File.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		if (latestPatch.File is null) return null;

		var currentVersion = latestPatch.Match.Groups["to"].Value;
		return (currentVersion, IncrementVersion(currentVersion));
	}

	public static string IncrementVersion(string currentVersion)
	{
		var parts = currentVersion.Split('.');
		if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
			!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
			!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch) ||
			minor > 99 || patch > 99)
		{
			throw new InvalidDataException($"增量包文件名中的版本号无效或超出 0-99 进位范围：{currentVersion}");
		}

		patch++;
		if (patch == 100)
		{
			patch = 0;
			minor++;
			if (minor == 100)
			{
				minor = 0;
				try { major = checked(major + 1); }
				catch (OverflowException ex) { throw new InvalidDataException("版本号主版本已达到上限。", ex); }
			}
		}

		return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{patch}");
	}
}