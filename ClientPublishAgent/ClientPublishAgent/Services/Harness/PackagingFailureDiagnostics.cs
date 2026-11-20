using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ClientPublishAgent.Services.Harness;

internal static class PackagingFailureDiagnostics
{
	public static string Describe(string runtime, int? exitCode, string output, bool truncated)
	{
		var signals = new List<string>();
		bool Contains(string value) => output.Contains(value, StringComparison.OrdinalIgnoreCase);
		bool Match(string pattern) => Regex.IsMatch(output, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromSeconds(1));

		if (Contains("Git 拉取失败") || Contains("git pull failed") || Match(@"fatal:\s*(?:not a git repository|could not read|unable to access|authentication failed|couldn't find remote ref)"))
			signals.Add("Git source synchronization failed; check the local Git error and repository state.");
		if (Match(@"\berror\s+NU\d{4}\b"))
			signals.Add("A NuGet restore error was reported; check the local log for its code and package source.");
		if (Match(@"\berror\s+(?:CS|NETSDK)\d{3,5}\b"))
			signals.Add("A .NET compilation or SDK error was reported; check the local log for its code.");
		if (Contains("未找到 macOS 差分工具") || Contains("未找到 Linux 差分工具") || Contains("Plugin script missing"))
			signals.Add("A packaging script or platform-specific differential tool is missing.");
		if (Contains("ARTIFACTORY_API_TOKEN") || Contains("下载制品失败"))
			signals.Add("Artifact authentication or download failed; check local credentials and network access without sharing secrets.");
		if (Contains("permission denied") || Contains("access is denied") || Contains("权限不足"))
			signals.Add("A permission error was reported; inspect the local log for the affected operation.");
		if (Contains("bundle format unrecognized") || Contains("code object is not signed") || Contains("errSecInternalComponent") ||
			Contains("命令执行失败：codesign") || Contains("codesign failed"))
			signals.Add("macOS code signing failed; inspect the local codesign error and signing configuration.");
		if (Contains("notarytool") && (Contains("failed") || Contains("失败") || Contains("error:")))
			signals.Add("macOS notarization reported an error; inspect the local notarytool output.");
		if (Contains("不支持目标架构") || Contains("bad cpu type") || Contains("exec format error") || Contains("cross-OS native compilation is not supported"))
			signals.Add("A target architecture or executable compatibility error was reported.");
		if (Contains("changed after approval"))
			signals.Add("An approved script, configuration or version changed; request a fresh approval.");

		var state = exitCode.HasValue ? $"exit code {exitCode.Value}" : "failed before a process exit code was available";
		var result = $"Packaging failed for {runtime} ({state}). " +
			(signals.Count > 0 ? "Possible diagnostic signals (verify against the local task log): " + string.Join(" ", signals) :
				"No recognized error signal was found. Ask the user for the redacted final error from the local task log; do not scan the workspace or rerun packaging.");
		if (truncated) result += " Only the last 8192 characters of each output stream were inspected; earlier errors may be missing.";
		return result + " Raw task output is local and was not sent to the model.";
	}
}