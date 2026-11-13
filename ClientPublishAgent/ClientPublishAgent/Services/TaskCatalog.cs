using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClientPublishAgent.Services;

public enum NativeHost { Windows, Linux, MacOS }
public sealed record PluginRequest(string Task, string ScriptsRoot, string RuntimeIdentifier, string CurrentVersion, string NextVersion, string OssHost, string SourceDirectory = "", string PackageType = "upgrade-installer", bool PublishAot = true, string SigningIdentity = "");
public sealed record PluginCommand(string Executable, IReadOnlyList<string> Arguments, string Directory, string OutputRoot,
    string? ScriptPath = null, string? SourceDirectory = null);
public interface ITaskPlugin
{
    string Id { get; }
    string Description { get; }
    bool Supports(NativeHost host, string runtime);
    PluginCommand Prepare(PluginRequest request);
}

public static class TaskCatalog
{
    public static NativeHost Host => OperatingSystem.IsWindows() ? NativeHost.Windows :
        OperatingSystem.IsMacOS() ? NativeHost.MacOS : NativeHost.Linux;
    public static string GetDesktopOutputRoot(string taskId, string scriptsRoot, string runtimeIdentifier)
    {
        var propertyName = taskId switch
        {
            "windows-package" => "RootDirectory",
            "linux-package" => "LinuxRootDirectory",
            "macos-package" => "MacOSRootDirectory",
            _ => throw new ArgumentException("Task does not have a desktop publish output root.", nameof(taskId))
        };
        var fullScriptsRoot = Path.GetFullPath(scriptsRoot);
        var repositoryRoot = Directory.GetParent(fullScriptsRoot)?.Parent?.FullName ?? fullScriptsRoot;
        var root = Path.Combine(repositoryRoot, "publish", runtimeIdentifier);
        var configPath = Path.Combine(fullScriptsRoot, "win-package", "publish-config.json");
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        if (config.RootElement.TryGetProperty(propertyName, out var configuredRoot) &&
            configuredRoot.ValueKind == System.Text.Json.JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(configuredRoot.GetString()))
        {
            var configuredBase = configuredRoot.GetString()!;
            var isPosixAbsolute = configuredBase.StartsWith("/", StringComparison.Ordinal);
            if (!Path.IsPathFullyQualified(configuredBase) && !isPosixAbsolute)
                throw new InvalidDataException(propertyName + " must be an absolute path.");
            var configuredBaseWithoutSeparator = configuredBase.TrimEnd('/', '\\');
            root = taskId == "windows-package" || configuredBaseWithoutSeparator.EndsWith(runtimeIdentifier, StringComparison.Ordinal)
                ? configuredBase
                : isPosixAbsolute
                    ? $"{configuredBaseWithoutSeparator}/{runtimeIdentifier}"
                    : Path.Combine(configuredBase, runtimeIdentifier);
        }
        return root;
    }
    public static IReadOnlyList<ITaskPlugin> Plugins { get; } =
    [
        new ScriptPlugin("windows-package", "Windows x64 · NSIS", "win-package", "publish-upgrade-package-win-x64.ps1", ["win-x64"], [NativeHost.Windows]),
        new ScriptPlugin("linux-package", "Linux x64/arm64 · dpkg (Windows: WSL Debian + pwsh)", "linux-package", "linux-publish&upgrade&package.ps1", ["linux-x64", "linux-arm64"], [NativeHost.Windows, NativeHost.Linux]),
        new ScriptPlugin("macos-package", "macOS x64/arm64 · native hdiutil", "mac-package", "mac-publish&upgrade&package.ps1", ["osx-x64", "osx-arm64"], [NativeHost.MacOS]),
        new MarketServerPlugin()
    ];
    public static ITaskPlugin Get(string id) => Plugins.SingleOrDefault(p => p.Id == id) ?? throw new ArgumentException("Unknown plugin.");
    public static void ValidateRequest(PluginRequest request)
    {
        if (!Path.IsPathFullyQualified(request.ScriptsRoot)) throw new ArgumentException("Plugin root must be absolute.");
        static Version Parse(string value)
        {
            if (!Regex.IsMatch(value, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$") || !Version.TryParse(value, out var version))
                throw new ArgumentException("Version must be x.y.z.");
            return version;
        }
        if (Parse(request.NextVersion) < Parse(request.CurrentVersion)) throw new ArgumentException("Next version cannot be lower than current.");
        if (request.OssHost.Length > 2048 || request.OssHost.Any(char.IsControl)) throw new ArgumentException("Invalid OSS address.");
    }
    public static string RequireCommand(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir.Trim('"'), name + (OperatingSystem.IsWindows() ? ".exe" : ""));
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new PlatformNotSupportedException("Required executable not on PATH: " + name);
    }
    private sealed class ScriptPlugin(string id, string description, string folder, string script, string[] runtimes, NativeHost[] hosts) : ITaskPlugin
    {
        public string Id => id;
        public string Description => description;
        public bool Supports(NativeHost host, string runtime) => hosts.Contains(host) && runtimes.Contains(runtime);
        public PluginCommand Prepare(PluginRequest request)
        {
            if (!Supports(Host, request.RuntimeIdentifier)) throw new PlatformNotSupportedException("Invalid native host/runtime combination for " + Id);
            ValidateRequest(request);
            var shell = id == "windows-package" ? PowerShellTaskRunner.ResolvePowerShell() : PowerShellTaskRunner.ResolvePowerShell7();
            if (id == "linux-package") RequireCommand(Host == NativeHost.Windows ? "wsl" : "dpkg-deb");
            if (id == "macos-package") RequireCommand("hdiutil");
            var directory = Path.Combine(Path.GetFullPath(request.ScriptsRoot), folder);
            var path = Path.Combine(directory, script);
            if (!File.Exists(path)) throw new FileNotFoundException("Plugin script missing.", path);
            var args = new List<string> { "-NoProfile" };
            var sourceDirectory = string.IsNullOrWhiteSpace(request.SourceDirectory) ? string.Empty : Path.GetFullPath(request.SourceDirectory);
            var packageType = string.IsNullOrWhiteSpace(request.PackageType) ? "upgrade-installer" : request.PackageType.Trim();
            if (OperatingSystem.IsWindows()) args.AddRange(["-ExecutionPolicy", "Bypass"]);
            args.AddRange(["-File", path, "-CurrentVersion", request.CurrentVersion, "-NextVersion", request.NextVersion]);
            if (id != "marketserver" && !string.IsNullOrWhiteSpace(sourceDirectory)) args.AddRange(["-SourceDirectory", sourceDirectory]);
            args.AddRange(["-RuntimeIdentifier", request.RuntimeIdentifier, "-PackageType", packageType]);
            if (id is "windows-package" or "linux-package" or "macos-package")
                args.AddRange(["-PublishAot", request.PublishAot.ToString().ToLowerInvariant()]);
            if (id == "macos-package" && !string.IsNullOrWhiteSpace(request.SigningIdentity))
                args.AddRange(["-SigningIdentity", request.SigningIdentity.Trim()]);
            if (!string.IsNullOrEmpty(request.OssHost)) args.AddRange(["-OssHost", request.OssHost]);
            var root = id is "windows-package" or "linux-package" or "macos-package"
                ? GetDesktopOutputRoot(id, request.ScriptsRoot, request.RuntimeIdentifier)
                : Path.Combine(Directory.GetParent(Path.GetFullPath(request.ScriptsRoot))?.Parent?.FullName ?? request.ScriptsRoot,
                    "publish", request.RuntimeIdentifier);
            if (id is "windows-package" or "linux-package" or "macos-package")
            {
                if (id != "windows-package") args.AddRange(["-RootDirectory", root]);
            }
            return new PluginCommand(shell, args, directory, root, path, sourceDirectory);
        }
    }
    private sealed class MarketServerPlugin : ITaskPlugin
    {
        public string Id => "marketserver";
        public string Description => "MarketServer Linux x64 native AOT · Linux clang/.NET SDK required; Windows .bat cross-AOT is not supported";
        public bool Supports(NativeHost host, string runtime) => host == NativeHost.Linux && runtime == "linux-x64";
        public PluginCommand Prepare(PluginRequest request)
        {
            if (!Supports(Host, request.RuntimeIdentifier)) throw new PlatformNotSupportedException(Description);
            ValidateRequest(request);
            RequireCommand("clang");
            var executable = RequireCommand("dotnet");
            var root = Path.GetFullPath(string.IsNullOrWhiteSpace(request.SourceDirectory) ? request.ScriptsRoot : request.SourceDirectory);
            var sourceDirectory = string.IsNullOrWhiteSpace(request.SourceDirectory)
                ? Path.Combine(root, "src")
                : Path.GetFullPath(request.SourceDirectory);
            var project = Path.Combine(sourceDirectory, "WarrenqF1.MarketServer", "WarrenqF1.MarketServer.csproj");
            if (!File.Exists(project)) throw new FileNotFoundException("MarketServer project missing.", project);
            var repositoryRoot = Directory.GetParent(sourceDirectory)?.FullName ?? root;
            var output = Path.Combine(repositoryRoot, "publish", "marketserver", "linux-x64");
            return new PluginCommand(executable, ["publish", project, "--configuration", "Release", "--runtime", "linux-x64",
                "--self-contained", "true", "--output", output, "-p:PublishAot=true", "-p:PublishTrimmed=true", "-p:GLibcVersion=2.28",
                "-p:StripSymbols=true", "-p:DebugSymbols=false", "-p:DebugType=None", "-p:CopyOutputSymbolsToPublishDirectory=false"], sourceDirectory, output,
                SourceDirectory: sourceDirectory);
        }
    }
}
