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
		if (Contains("下载制品失败"))
		{
			if (Match(@"curl:\s*\(22\)[^\r\n]*\b403\b"))
				signals.Add("The artifact download received HTTP 403. The server or an intermediary refused the request; this alone does not identify whether credentials, permissions, network or policy caused it. Compare a working request without exposing credentials.");
			else
				signals.Add("An artifact download failed; inspect the local curl error before deciding whether authentication, permissions or network is responsible.");
			if (Match(@"缓存不存在，将下载制品：[^\r\n]*openclaw-release\.tgz"))
				signals.Add("The OpenClaw archive was absent from the target cache, triggering a download.");
			else if (Match(@"下载参数指定重新下载制品：[^\r\n]*openclaw-release\.tgz"))
				signals.Add("The execution requested an OpenClaw refresh; check the user's request and approval before assuming it was intentional.");
		}
		if (Contains("未设置 Artifactory API 令牌"))
			signals.Add("The artifact API token was not set in the packaging process; do not disclose the token.");
		if (Contains("permission denied") || Contains("access is denied") || Contains("权限不足"))
			signals.Add("A permission error was reported; inspect the local log for the affected operation.");
		if (Contains("bundle format unrecognized") || Contains("code object is not signed") || Contains("errSecInternalComponent") ||
			Contains("命令执行失败：codesign") || Contains("codesign failed"))
			signals.Add("macOS code signing failed; inspect the local codesign error and signing configuration.");
		if (Contains("Apple 公证连接超时") || (Contains("Apple 公证提交失败") && Contains("HTTPClientError.connectTimeout")))
			signals.Add("Apple notarization reported a connection timeout. Submission status is unknown: check notarization history before resubmitting, and do not distribute a DMG until notarization and stapling are verified.");
		else if (Contains("Apple 公证提交失败") || Contains("Apple 未接受") ||
			(Contains("notarytool") && (Contains("failed") || Contains("失败") || Contains("error:"))))
			signals.Add("macOS notarization reported an error; inspect the local notarytool output.");
		if (Contains("不支持目标架构") || Contains("bad cpu type") || Contains("exec format error") || Contains("cross-OS native compilation is not supported"))
			signals.Add("A target architecture or executable compatibility error was reported.");
		if (Contains("changed after approval"))
			signals.Add("An approved script, configuration or version changed; request a fresh approval.");

		var state = exitCode.HasValue ? $"exit code {exitCode.Value}" : "failed before a process exit code was available";
		var result = $"Packaging failed for {runtime} ({state}). " +
			(signals.Count > 0 ? "Possible diagnostic signals (verify against the local task log): " + string.Join(" ", signals) :
				"No recognized error signal was found. Use evidence already provided by the user; if necessary ask for the missing redacted final error from the local task log. Do not scan the workspace or rerun packaging.");
		if (truncated) result += " Only the last 8192 characters of each output stream were inspected; earlier errors may be missing.";
		return result + " Raw task output is local and was not sent to the model.";
	}
}