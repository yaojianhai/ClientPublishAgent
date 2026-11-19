using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ClientPublishAgent.Services;
using ClientPublishAgent.Services.Harness;
using ClientPublishAgent.ViewModels;
using ClientPublishAgent.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
var ct = timeout.Token;
var root = Path.Combine(AppContext.BaseDirectory, "harness-check-" + Guid.NewGuid().ToString("N"));
var keyName = "HARNESS_CHECK_KEY_" + Guid.NewGuid().ToString("N");
Directory.CreateDirectory(root);
try
{
    Environment.SetEnvironmentVariable(keyName, "local-test-key");
    await using var model = new FakeModel();
    var output = new ConcurrentQueue<string>();
    if (args.Contains("--script-manager", StringComparer.Ordinal))
    {
        CheckScriptManager();
        return;
    }
    if (args.Contains("--packaging-oss", StringComparer.Ordinal))
    {
        CheckPackagingOssHost(root);
        return;
    }
    if (args.Contains("--desktop-output", StringComparer.Ordinal))
    {
        CheckDesktopOutputRoots(root);
        Console.WriteLine("Desktop output root checks passed.");
        return;
    }
    if (args.Contains("--packaging-target", StringComparer.Ordinal))
    {
        await CheckPackagingTargetAsync();
        return;
    }
    if (args.Contains("--packaging-version", StringComparer.Ordinal))
    {
        CheckPackagingVersionReader(root);
        await CheckPackagingVersionDefaultsAsync(root);
        return;
    }
    if (args.Contains("--packaging-ui", StringComparer.Ordinal))
    {
        await CheckPackagingUiAsync(ct);
        return;
    }
    if (args.Contains("--mac-signing-settings", StringComparer.Ordinal))
    {
        await CheckMacSigningSettingsAsync(root);
        return;
    }
    if (args.Contains("--attribution-json", StringComparer.Ordinal))
    {
        CheckRequestSourceAttributionSerialization();
        await CheckSessionSerializationWithoutReflectionAsync(model, root, keyName, ct);
        Console.WriteLine("Request source attribution JSON checks passed.");
        return;
    }
    if (args.Contains("--interactive", StringComparer.Ordinal))
    {
        await CheckInteractiveTasksAsync(model, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--chat-ui", StringComparer.Ordinal))
    {
        await CheckChatInteractionAsync(model, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--history-order", StringComparer.Ordinal))
    {
        await CheckChatInteractionAsync(model, root, keyName, ct, historyOrderOnly: true);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--compaction", StringComparer.Ordinal))
    {
        await CheckCompactionPersistenceAsync(model, output, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--automatic", StringComparer.Ordinal))
    {
        await CheckAutomaticAsync(model, output, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--platform", StringComparer.Ordinal))
    {
        await CheckPlatformAsync(model, output, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--session-approval", StringComparer.Ordinal))
    {
        await CheckSessionApprovalAsync(model, output, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--terminal-result", StringComparer.Ordinal))
    {
        await CheckTerminalResultAsync(model, output, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    if (args.Contains("--terminal-loop", StringComparer.Ordinal))
    {
        await CheckTerminalLoopAsync(model, output, root, keyName, ct);
        model.AssertHealthy();
        return;
    }
    Check(new AgentWorkspaceViewModel(new PlatformStore(Path.Combine(root, "empty-cache"))).KeyEnvironmentVariable == "", "no default API key variable");
    CheckHistoryIsolation(root);
    CheckScriptManager();
    CheckDesktopOutputRoots(root);
    try
    {
        using var missing = new HarnessWorkspace(new HarnessSettings(model.Endpoint, "fake-model", keyName + "_MISSING", root), output.Enqueue);
        throw new InvalidOperationException("FAIL: missing configured environment variable accepted");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("未找到指定的密钥环境变量")) { }
    using var workspace = new HarnessWorkspace(new HarnessSettings(model.Endpoint, "fake-model", keyName, root), output.Enqueue);
    await File.WriteAllTextAsync(Path.Combine(root, "publish-config.json"), JsonSerializer.Serialize(new { RootDirectory = root }), ct);
    await WriteScriptAsync(root, "Set-Content -Path (Join-Path $PSScriptRoot 'executed.txt') -Value 'ok'\nWrite-Output 'local-only-secret'\nexit 0", ct);
    await workspace.InitializeAsync(ct);

    model.EnqueueText("hello ", "stream");
    var chunks = new ConcurrentQueue<string>();
    var plain = await workspace.SendAsync("hello", chunks.Enqueue, ct);
    Check(plain.Text == "hello stream" && string.Concat(chunks) == plain.Text && chunks.Count >= 2, "streaming conversation");
    Check(model.Requests.All(r => r.Contains("\"stream\":true")), "model requests streaming");
    Check(!File.Exists(Path.Combine(root, "executed.txt")), "conversation without approval does not execute");

    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("SKIP: approval execution, PowerShell and gate checks require Windows.");
    }
    else
    {
        await CheckHarnessAsync(workspace, model, output, root, ct);
        await CheckGateAsync(root, ct);
        await CheckInteractiveTasksAsync(model, root, keyName, ct);
    }
    await CheckPlatformAsync(model, output, root, keyName, ct);
    await CheckAutomaticAsync(model, output, root, keyName, ct);
    await CheckSessionApprovalAsync(model, output, root, keyName, ct);
    model.AssertHealthy();
    Console.WriteLine("All applicable Harness checks passed.");
}
finally
{
    Environment.SetEnvironmentVariable(keyName, null);
    Directory.Delete(root, true);
}

    static void CheckScriptManager()
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "Scripts");
        var folder = Path.Combine(scripts, "check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "example.ps1");
            var encoding = new UTF8Encoding(true, true);
            File.WriteAllText(file, "original", encoding);
            var viewModel = new ScriptManagerViewModel();
            viewModel.SelectedScript = null;
            var notifications = new List<string?>();
            viewModel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
            viewModel.SelectedScript = Path.GetRelativePath(scripts, file);
            Check(viewModel.CanSave && viewModel.ScriptContent == "original" && notifications.Contains(nameof(viewModel.CanSave)), "script editor load and save state notification");
            viewModel.ScriptContent = "modified";
            viewModel.SaveScriptCommand.Execute(null);
            Check(File.ReadAllBytes(file).AsSpan().StartsWith(encoding.Preamble) && File.ReadAllText(file) == "modified", "script editor preserves UTF-8 BOM");
            File.WriteAllText(file, "external", encoding);
            viewModel.RefreshScriptsCommand.Execute(null);
            Check(viewModel.ScriptContent == "external", "script editor refresh reloads selected file");
            viewModel.SelectedScript = Path.Combine("..", Path.GetFileName(folder), "example.ps1");
            Check(!viewModel.CanSave, "script editor blocks paths outside Scripts");
            viewModel.SaveScriptCommand.Execute(null);
            Check(File.ReadAllText(file) == "external", "failed script load cannot overwrite file");
            viewModel.SelectedScript = Path.GetRelativePath(scripts, file);
            File.Delete(file);
            viewModel.SaveScriptCommand.Execute(null);
            Check(!File.Exists(file), "script editor does not recreate a removed file");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
        Console.WriteLine("Script manager checks passed.");
    }

    static void CheckPackagingOssHost(string root)
    {
        var scripts = Path.Combine(root, "oss-config", "Scripts", "Desktop");
        var configPath = Path.Combine(scripts, "win-package", "publish-config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { RootDirectory = "D:\\Publish\\win-x64", LinuxRootDirectory = "/linux", MacOSRootDirectory = "/mac" }));
        var first = new WindowsPublishConfigStore(configPath);
        var stale = new WindowsPublishConfigStore(configPath);
        Check(first.OssHost == WindowsPublishConfigStore.DefaultOssHost, "legacy config defaults to public upgrade base");
        first.SaveOssHost("  https://updates.example.test/new/path/  ");
        stale.SaveLinuxRootDirectory("/new-linux");
        var reloaded = new WindowsPublishConfigStore(configPath);
        Check(reloaded.OssHost == "https://updates.example.test/new/path" && reloaded.RootDirectory == "D:\\Publish\\win-x64" &&
            reloaded.LinuxRootDirectory == "/new-linux" && reloaded.MacOSRootDirectory == "/mac",
            "address persists and independently saving directories preserves existing settings");
        foreach (var runtime in new[] { "win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" })
            Check(TaskCatalog.GetDesktopOssHost(scripts, runtime, "") == "https://updates.example.test/new/path/" + runtime,
                "configured upgrade address includes the selected runtime: " + runtime);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(scripts, "win-package", "publish-upgrade-package-win-x64.ps1"), "");
            var plugin = TaskCatalog.Get("windows-package").Prepare(new PluginRequest("windows-package", scripts, "win-x64", "1.0.0", "1.0.1", ""));
            var hostArgument = plugin.Arguments.ToList().IndexOf("-OssHost");
            Check(hostArgument >= 0 && plugin.Arguments[hostArgument + 1] == "https://updates.example.test/new/path/win-x64",
                "manual and agent desktop plugin passes the resolved address to the script");
        }
        Check(TaskCatalog.GetDesktopOssHost(scripts, "linux-x64", "http://override.test/custom") == "http://override.test/custom",
            "explicit plugin address is not modified");
        foreach (var invalid in new[] { "", "ftp://example.test/a", "https://user:pass@example.test", "https://example.test/a?token=x", "https://example.test/linux-x64" })
        {
            try { reloaded.SaveOssHost(invalid); throw new Exception("FAIL: invalid upgrade base accepted: " + invalid); }
            catch (ArgumentException) { }
        }
        Check(new WindowsPublishConfigStore(configPath).OssHost == "https://updates.example.test/new/path",
            "invalid addresses do not overwrite saved configuration");
        Console.WriteLine("Packaging upgrade address checks passed.");
    }

    static void CheckDesktopOutputRoots(string root)
    {
        var scriptsRoot = Path.Combine(root, "desktop-output-check", "Scripts", "Desktop");
        Directory.CreateDirectory(Path.Combine(scriptsRoot, "win-package"));
        var windowsRoot = Path.Combine(root, "windows-output", "win-x64");
        var linuxRoot = Path.Combine(root, "linux-output");
        var macOSRoot = Path.Combine(root, "macos-output");
        File.WriteAllText(Path.Combine(scriptsRoot, "win-package", "publish-config.json"),
            JsonSerializer.Serialize(new { RootDirectory = windowsRoot, LinuxRootDirectory = linuxRoot, MacOSRootDirectory = macOSRoot }));

        Check(TaskCatalog.GetDesktopOutputRoot("windows-package", scriptsRoot, "win-x64") == windowsRoot,
            "Windows output root uses configured directory");
        Check(TaskCatalog.GetDesktopOutputRoot("linux-package", scriptsRoot, "linux-arm64") == Path.Combine(linuxRoot, "linux-arm64"),
            "Linux output root includes selected runtime");
        Check(TaskCatalog.GetDesktopOutputRoot("macos-package", scriptsRoot, "osx-x64") == Path.Combine(macOSRoot, "osx-x64"),
            "macOS output root includes selected runtime");
        File.WriteAllText(Path.Combine(scriptsRoot, "win-package", "publish-config.json"),
            JsonSerializer.Serialize(new { LinuxRootDirectory = "/data/publish" }));
        Check(TaskCatalog.GetDesktopOutputRoot("linux-package", scriptsRoot, "linux-x64") == "/data/publish/linux-x64",
            "Linux POSIX output root preserves its path format");
    }

    static async Task CheckPackagingUiAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("SKIP: live packaging window checks require Windows.");
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
                Application.Current!.Styles.Add(new FluentTheme());
                Dispatcher.UIThread.Post(async () =>
                {
                    MainWindow? window = null;
                    try
                    {
                        window = new MainWindow { ShowInTaskbar = false };
                        Check(window.Icon is not null, "main window loads the cross-platform native PNG window icon");
                        var logo = window.FindControl<Avalonia.Svg.Skia.Svg>("ApplicationLogo");
                        Check(logo is not null && logo.Width == 36 && logo.Height == 36 && logo.Path == "/Assets/ai.svg",
                            "main window displays the ai.svg title logo at 36 by 36");
                        var tabs = window.FindControl<TabControl>("MainTabs")!;
                        var tab = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, "打包目录配置"));
                        Check(tab.Content is PackagingPathsView, "main window uses shared packaging paths view");
                        tabs.SelectedItem = tab;
                        window.Show();
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                        window.UpdateLayout();
                        Check(logo!.Bounds.Width == 36 && logo.Bounds.Height == 36,
                            $"ai.svg title logo renders at 36 by 36 device-independent pixels (actual {logo.Bounds.Width}x{logo.Bounds.Height})");
                        var view = (PackagingPathsView)tab.Content!;
                        Check(view.FindControl<StackPanel>("WindowsOutputSection")!.IsVisible, "Windows output directory is visible on Windows");
                        Check(!view.FindControl<StackPanel>("LinuxOutputSection")!.IsVisible, "Linux output directory is hidden on Windows");
                        Check(!view.FindControl<StackPanel>("MacOSOutputSection")!.IsVisible, "macOS output directory is hidden on Windows");
                        completion.TrySetResult();
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    finally { window?.Close(); stop.Cancel(); }
                });
                Dispatcher.UIThread.MainLoop(stop.Token);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(ct);
        thread.Join();
        Console.WriteLine("Packaging window visibility checks passed.");
    }

    static async Task CheckMacSigningSettingsAsync(string root)
    {
        var path = Path.Combine(root, "signing-settings", "settings.json");
        var oldIdentity = Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY");
        var oldProfile = Environment.GetEnvironmentVariable("APPLE_NOTARY_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("APPLE_SIGNING_IDENTITY", "environment identity");
            Environment.SetEnvironmentVariable("APPLE_NOTARY_PROFILE", "environment profile");
            var first = new MainViewModel(new MacSigningSettingsStore(path));
            Check(first.SigningIdentity == "environment identity" && first.NotaryKeychainProfile == "environment profile" && !File.Exists(path), "initial environment defaults");
            first.SigningIdentity = "Developer ID Application: Example (TEAM123)";
            first.NotaryKeychainProfile = "example-notary";
            Check(first.SigningSettingsStatus.Contains("已保存"), "successful edit reports local persistence");
            await first.CloseAsync();
            var restored = new MainViewModel(new MacSigningSettingsStore(path));
            Check(restored.SigningIdentity == "Developer ID Application: Example (TEAM123)" && restored.NotaryKeychainProfile == "example-notary", "saved values restored instead of environment defaults");
            restored.SigningIdentity = "";
            restored.NotaryKeychainProfile = "";
            await restored.CloseAsync();
            var cleared = new MainViewModel(new MacSigningSettingsStore(path));
            Check(cleared.SigningIdentity == "" && cleared.NotaryKeychainProfile == "", "explicitly cleared values persist");
            await cleared.CloseAsync();
            await File.WriteAllTextAsync(path, "not valid json");
            var invalid = new MainViewModel(new MacSigningSettingsStore(path));
            Check(invalid.SigningSettingsStatus.Contains("读取") && invalid.SigningSettingsStatus.Contains("失败"), "load errors are visible");
            invalid.SigningIdentity = "edited";
            Check(await File.ReadAllTextAsync(path) == "not valid json", "corrupt configuration is not overwritten");
            await invalid.CloseAsync();
            var blocked = new MainViewModel(new MacSigningSettingsStore(Path.Combine(path, "settings.json")));
            blocked.SigningIdentity = "edited";
            Check(blocked.SigningSettingsStatus.Contains("保存") && blocked.SigningSettingsStatus.Contains("失败"), "save errors are visible");
            await blocked.CloseAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPLE_SIGNING_IDENTITY", oldIdentity);
            Environment.SetEnvironmentVariable("APPLE_NOTARY_PROFILE", oldProfile);
        }
        Console.WriteLine("macOS signing settings checks passed.");
    }

    static async Task CheckPackagingTargetAsync()
    {
        var viewModel = new MainViewModel();
        try
        {
            var expectedSystem = TaskCatalog.Host switch
            {
                NativeHost.Windows => "Windows",
                NativeHost.MacOS => "macOS",
                _ => "Linux"
            };
            var expectedArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "x64",
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                _ => "x64"
            };
            Check(viewModel.SelectedSystem == expectedSystem, "manual packaging system is fixed to the current host");
            var linuxPlugin = TaskCatalog.Get("linux-package");
            Check(linuxPlugin.Supports(NativeHost.Linux, "linux-x64") && linuxPlugin.Supports(NativeHost.Linux, "linux-arm64") &&
                !linuxPlugin.Supports(NativeHost.Windows, "linux-x64") && !linuxPlugin.Supports(NativeHost.MacOS, "linux-x64"),
                "Linux packaging accepts only Linux hosts and preserves both target architectures");
            if (OperatingSystem.IsWindows())
            {
                try { linuxPlugin.Prepare(new PluginRequest("linux-package", AppContext.BaseDirectory, "linux-x64", "1.0.0", "1.0.1", "")); throw new Exception("FAIL: Linux packaging started on Windows"); }
                catch (PlatformNotSupportedException) { }
            }
            Check(viewModel.SelectedArchitecture == expectedArchitecture && viewModel.Architectures.SequenceEqual(["x64", "arm64"]),
                "manual packaging offers mainstream architectures and defaults to the current host");
            var alternateArchitecture = expectedArchitecture == "x64" ? "arm64" : "x64";
            viewModel.SelectedArchitecture = alternateArchitecture;
            Check(viewModel.SelectedArchitecture == alternateArchitecture, "manual packaging architecture can be selected");
            Console.WriteLine("Manual packaging native target checks passed.");
        }
        finally { await viewModel.CloseAsync(); }
    }

    static async Task CheckPackagingVersionDefaultsAsync(string root)
    {
        var configPath = WindowsPublishConfigStore.ConfigPath;
        var originalConfig = File.Exists(configPath) ? await File.ReadAllBytesAsync(configPath) : null;
        var outputRoot = Path.Combine(root, "manual-version-defaults");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
            {
                RootDirectory = outputRoot, LinuxRootDirectory = outputRoot, MacOSRootDirectory = outputRoot
            }));
            async Task CheckVersionsAsync(string current, string next, string description)
            {
                var viewModel = new MainViewModel();
                try
                {
                    viewModel.SelectedArchitecture = "x64";
                    Check(viewModel.CurrentVersion == current && viewModel.NextVersion == next, description);
                }
                finally { await viewModel.CloseAsync(); }
            }
            var runtime = TaskCatalog.Host switch
            {
                NativeHost.Windows => "win-x64", NativeHost.MacOS => "osx-x64", _ => "linux-x64"
            };
            var patchDirectory = Path.Combine(TaskCatalog.Host == NativeHost.Windows ? outputRoot : Path.Combine(outputRoot, runtime), "OutputPatchPackage");
            await CheckVersionsAsync("1.0.0", "1.0.1", "manual packaging defaults when patch directory is missing");
            Directory.CreateDirectory(patchDirectory);
            await CheckVersionsAsync("1.0.0", "1.0.1", "manual packaging defaults when patch directory is empty");
            await File.WriteAllTextAsync(Path.Combine(patchDirectory, "unrecognized.zip"), "");
            await CheckVersionsAsync("1.0.0", "1.0.1", "manual packaging defaults when no filename contains a version");
            await File.WriteAllTextAsync(Path.Combine(patchDirectory, "patch_v1.0.98_to_v1.0.99.zip"), "");
            await CheckVersionsAsync("1.0.99", "1.1.0", "manual packaging preserves historical version reading and carry");
        }
        finally
        {
            if (originalConfig is not null) await File.WriteAllBytesAsync(configPath, originalConfig);
            else File.Delete(configPath);
        }
        Console.WriteLine("Manual packaging default version checks passed.");
    }

    static void CheckPackagingVersionReader(string root)
    {
        var outputRoot = Path.Combine(root, "packaging-version-output");
        var patchDirectory = Path.Combine(outputRoot, "OutputPatchPackage");
        Directory.CreateDirectory(patchDirectory);
        Check(PackagingVersionReader.GetLatestPatchVersions(outputRoot) is null,
            "packaging version reader reports no prior patch output");
        Check(PackagingVersionReader.IncrementVersion("1.0.99") == "1.1.0" &&
            PackagingVersionReader.IncrementVersion("1.99.99") == "2.0.0",
            "packaging version increment carries at 100 across version segments");

        var olderPatch = Path.Combine(patchDirectory, "patch_v1.0.98_to_v1.0.99.zip");
        var latestPatch = Path.Combine(patchDirectory, "patch_v1.0.99_to_v1.1.0.zip");
        File.WriteAllBytes(olderPatch, []);
        File.WriteAllBytes(latestPatch, []);
        File.SetLastWriteTimeUtc(olderPatch, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(latestPatch, DateTime.UtcNow);
        var versions = PackagingVersionReader.GetLatestPatchVersions(outputRoot);
        Check(versions == ("1.1.0", "1.1.1"),
            "packaging version reader uses the target version from the most recently written patch zip");

        var invalidPatch = Path.Combine(patchDirectory, "patch_v1.1.99_to_v1.1.100.zip");
        File.WriteAllBytes(invalidPatch, []);
        File.SetLastWriteTimeUtc(invalidPatch, DateTime.UtcNow.AddMinutes(1));
        try
        {
            PackagingVersionReader.GetLatestPatchVersions(outputRoot);
            throw new InvalidOperationException("FAIL: invalid package version was accepted");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("Packaging version reader checks passed.");
    }

    static async Task CheckInteractiveTasksAsync(FakeModel model, string root, string keyName, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var runner = new PowerShellTaskRunner();
        var events = new ConcurrentQueue<bool>();
        var output = new CollectingProgress();
        runner.InputAvailabilityChanged += events.Enqueue;
        var script = Path.Combine(root, "interactive-check.ps1");
        await File.WriteAllTextAsync(script, "param($CurrentVersion,$NextVersion,$OssHost)\nWrite-Output 'started-interactive-test'\n$v = Read-Host ([string]::Concat([char]0x8BF7,[char]0x8F93,[char]0x5165))\nWrite-Host ('value=' + $v)\nWrite-Host 'before-pause'\ncmd.exe /c pause\nWrite-Host 'interactive-finished'", token);
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var run = runner.RunAsync(script, "1.0.0", "1.0.1", "", output, runCancellation.Token);
        try
        {
            await WaitForOutputAsync(output, "\u8bf7\u8f93\u5165", token);
            Check(events.TryPeek(out var available) && available, "interactive task exposes local input");
            await runner.SendInputAsync("\u786e\u8ba4", token);
            await WaitForOutputAsync(output, "before-pause", token);
            await runner.SendInputAsync("", token);
            Check(await run == 0 && output.Text.Contains("value=\u786e\u8ba4") && output.Text.Contains("interactive-finished"), "Read-Host and cmd pause accept input");
        }
        catch
        {
            Console.WriteLine("Interactive test output: " + output.Text + "; events: " + string.Join(",", events) + "; state: " + run.Status);
            throw;
        }
        finally
        {
            if (!run.IsCompleted) runCancellation.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
        Check(events.Last() == false, "interactive input unavailable after process exit");
        try { await runner.SendInputAsync("stale", token); throw new Exception("FAIL: input accepted after exit"); }
        catch (InvalidOperationException) { }

        var argumentScript = Path.Combine(root, "interactive-arguments.ps1");
        var argumentOutput = new CollectingProgress();
        await File.WriteAllTextAsync(argumentScript, "param($CurrentVersion,$NextVersion,$OssHost)\nWrite-Output ('HOST:' + $OssHost)\nexit 7", token);
        var argument = "value'; Write-Output 'unexpected-command";
        var exitCode = await runner.RunAsync(argumentScript, "1.0.0", "1.0.1", argument, argumentOutput, token);
        Check(exitCode == 7 && argumentOutput.Text.Contains("HOST:" + argument), "interactive wrapper preserves exit code and literal arguments");

        var secureOutput = new CollectingProgress();
        var secureCode = await runner.RunProcessAsync(PowerShellTaskRunner.ResolvePowerShell(), ["-NoProfile", "-Command", "Read-Host 'password' -AsSecureString"],
            root, secureOutput, token, interactive: true);
        Check(secureCode != 0 && !secureOutput.Text.Contains("#< CLIXML") && !secureOutput.Text.Contains("<Objs Version="), "unsupported masked input fails explicitly without raw CLIXML");

        var streamOutput = new CollectingProgress();
        var streamCode = await runner.RunProcessAsync(PowerShellTaskRunner.ResolvePowerShell(), ["-NoProfile", "-Command",
            "Write-Progress -Activity '中文打包进度' -Status '处理中'; Write-Information '中文信息流' -InformationAction Continue; Write-Warning '中文警告'"],
            root, streamOutput, token, interactive: true);
        Check(streamCode == 0 && streamOutput.Text.Contains("中文信息流") && streamOutput.Text.Contains("中文警告") &&
            streamOutput.Text.Contains("中文打包进度") && streamOutput.Text.Contains("处理中") &&
            !streamOutput.Text.Contains("#< CLIXML") && !streamOutput.Text.Contains("<Objs Version="), "PowerShell streams render readable text instead of CLIXML or mojibake");

        var cancelledOutput = new CollectingProgress();
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            var waiting = runner.RunProcessAsync(PowerShellTaskRunner.ResolvePowerShell(), ["-NoProfile", "-Command", "$v = Read-Host 'Waiting for input'"],
                root, cancelledOutput, cancellation.Token, interactive: true);
            await WaitForOutputAsync(cancelledOutput, "Waiting for input", token);
            cancellation.Cancel();
            await ExpectCancellationAsync(waiting, "cancel interactive prompt");
            try { await runner.SendInputAsync("stale", token); throw new Exception("FAIL: cancelled input accepted"); }
            catch (InvalidOperationException) { }
        }

        var profile = new AgentProfile { Name = "Interactive", Endpoint = model.Endpoint, Model = "fake-model", KeyEnvironmentVariable = keyName,
            WorkspaceRoot = root, ScriptsDirectory = root, Terminal = true, Packaging = true };
        using var workspace = new HarnessWorkspace(new HarnessSettings(profile.Endpoint, profile.Model, keyName, "", profile), output.Report);
        var inputAvailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.ScriptInputAvailabilityChanged += available => { if (available) inputAvailable.TrySetResult(); };
        await workspace.InitializeAsync(token);
        model.EnqueueTool("run_terminal", new { command = "$v = Read-Host 'Local prompt'; Set-Content -LiteralPath 'interactive-answer.txt' -Value $v" });
        var approval = (await workspace.SendAsync("run interactive command", cancellationToken: token)).Approvals.Single();
        model.EnqueueText("interactive command done");
        var approved = workspace.DecideAsync(approval.Id, true, cancellationToken: token);
        await inputAvailable.Task.WaitAsync(token);
        await WaitForOutputAsync(output, "Local prompt", token);
        await workspace.SendScriptInputAsync("local-only-answer", token);
        await approved;
        Check((await File.ReadAllTextAsync(Path.Combine(root, "interactive-answer.txt"), token)).Trim() == "local-only-answer", "approved terminal receives user input");
        Check(!model.Requests.Last().Contains("local-only-answer"), "interactive input is not sent to model");
        var pluginRoot = Path.Combine(root, "win-package");
        Directory.CreateDirectory(pluginRoot);
        await File.WriteAllTextAsync(Path.Combine(root, "publish-root-config.ps1"), "", token);
        await File.WriteAllTextAsync(Path.Combine(pluginRoot, "publish-config.json"), JsonSerializer.Serialize(new { RootDirectory = root }), token);
        await File.WriteAllTextAsync(Path.Combine(pluginRoot, "publish-upgrade-package-win-x64.ps1"),
            "param($CurrentVersion,$NextVersion,$OssHost)\n$v = Read-Host 'Plugin prompt'\nSet-Content -LiteralPath 'plugin-answer.txt' -Value $v", token);
        var pluginReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.ScriptInputAvailabilityChanged += available => { if (available) pluginReady.TrySetResult(); };
        model.EnqueueTool("run_task_plugin", new { task = "windows-package", runtimeIdentifier = "win-x64", currentVersion = "1.0.0", nextVersion = "1.0.1", ossHost = "" });
        approval = (await workspace.SendAsync("run interactive packaging", cancellationToken: token)).Approvals.Single();
        model.EnqueueText("packaging complete");
        var plugin = workspace.DecideAsync(approval.Id, true, cancellationToken: token);
        await pluginReady.Task.WaitAsync(token);
        await WaitForOutputAsync(output, "Plugin prompt", token);
        await workspace.SendScriptInputAsync("plugin-confirmed", token);
        await plugin;
        Check((await File.ReadAllTextAsync(Path.Combine(pluginRoot, "plugin-answer.txt"), token)).Trim() == "plugin-confirmed", "approved packaging plugin receives user input");
        Check(!model.Requests.Last().Contains("plugin-confirmed"), "packaging input is not sent to model");
        Console.WriteLine("PASS: interactive PowerShell and batch input, cancellation and model isolation");
    }

    static async Task WaitForOutputAsync(CollectingProgress output, string text, CancellationToken ct)
    {
        while (!output.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) await Task.Delay(20, ct);
    }

    static async Task CheckChatInteractionAsync(FakeModel model, string root, string keyName, CancellationToken ct, bool historyOrderOnly = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("SKIP: live chat window checks require Windows.");
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
                Application.Current!.Styles.Add(new FluentTheme());
                SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
                Dispatcher.UIThread.Post(async () =>
                {
                    Window? window = null;
                    AgentWorkspaceViewModel? ui = null;
                    try
                    {
                        var store = new PlatformStore(Path.Combine(root, "chat-ui-cache"));
                        var profile = new AgentProfile { Name = "Chat checks", Endpoint = model.Endpoint, Model = "fake-model", KeyEnvironmentVariable = keyName,
                            WorkspaceRoot = root, Files = true, Terminal = true };
                        store.SaveProfiles([profile]);
                        ui = new AgentWorkspaceViewModel(store);
                        var view = new AgentView(ui);
                        window = new Window { Width = 980, Height = 720, ShowInTaskbar = false, Content = view };
                        window.Show();
                        window.UpdateLayout();
                        Check(view.FindControl<StackPanel>("SidebarContent")!.Margin.Right >= 16,
                            "sidebar content has clearance from the vertical scrollbar");
                        async Task LayoutAsync()
                        {
                            window.UpdateLayout();
                            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                            window.UpdateLayout();
                        }
                        await LayoutAsync();
                        var details = view.FindControl<Border>("DetailsPanel")!;
                        var toggle = view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("DetailsToggle")!;
                        var sidebarSplitter = view.FindControl<GridSplitter>("SidebarSplitter")!;
                        var detailsSplitter = view.FindControl<GridSplitter>("DetailsSplitter")!;
                        var initialDetailsWidth = details.Bounds.Width;
                        Check(details.IsVisible && toggle.IsChecked == true && initialDetailsWidth >= 300,
                            "task details respect the minimum width at startup");
                        window.Width = 1280;
                        await LayoutAsync();
                        Check(details.Bounds.Width > initialDetailsWidth + 20,
                            "task details expand when the workspace window becomes wider");
                        window.Width = 980;
                        await LayoutAsync();
                        Check(sidebarSplitter.ResizeDirection == GridResizeDirection.Columns && sidebarSplitter.ResizeBehavior == GridResizeBehavior.PreviousAndNext &&
                            detailsSplitter.ResizeDirection == GridResizeDirection.Columns && detailsSplitter.ResizeBehavior == GridResizeBehavior.PreviousAndNext &&
                            detailsSplitter.IsVisible, "both side panels expose column-resizing splitters");
                        toggle.IsChecked = false;
                        await LayoutAsync();
                        Check(!details.IsVisible && !detailsSplitter.IsVisible && !ui.IsDetailsVisible && view.FindControl<ScrollViewer>("ChatScroll")!.Bounds.Width > 500,
                            "hiding task details expands the conversation");
                        toggle.IsChecked = true;
                        await LayoutAsync();
                        Check(details.IsVisible && ui.IsDetailsVisible, "task details reopen from the toolbar");
                        Check(view.FindControl<Expander>("AgentSettings") is not null && view.FindControl<Expander>("AgentCapabilities") is not null,
                            "agent configuration and capability controls remain accessible");
                        await ui.InitializeOnStartupAsync();
                        Check(ui.IsConnected && ui.CanSend && ui.CanType && ui.CanEditSettings && ui.Sessions.Count == 0,
                            "startup initializes an editable conversation without creating empty history");
                        Check(ui.ModeText == "自动执行", "startup uses execute mode (shown as " + ui.ModeText + ")");
                        if (historyOrderOnly)
                        {
                            model.EnqueueText("first ordered reply");
                            ui.Input = "first ordered conversation";
                            await ui.SendCommand.ExecuteAsync(null);
                            var earlierSessionId = ui.SelectedSession!.Id;
                            model.EnqueueText("second turn reply");
                            ui.Input = "second turn question";
                            await ui.SendCommand.ExecuteAsync(null);
                            var savedConversation = store.Load(earlierSessionId);
                            Check(savedConversation.Messages is { Count: 4 } messages &&
                                messages[0].Text == "first ordered conversation" && messages[1].Text == "first ordered reply" &&
                                messages[2].Text == "second turn question" && messages[3].Text == "second turn reply",
                                "history persistence retains every user and assistant turn");
                            await ui.NewConversationCommand.ExecuteAsync(null);
                            model.EnqueueText("second ordered reply");
                            ui.Input = "second ordered conversation";
                            await ui.SendCommand.ExecuteAsync(null);
                            var historyOrderBeforeSwitch = ui.Sessions.Select(s => s.Id).ToArray();
                            var earlierSession = ui.Sessions.Single(s => s.Id == earlierSessionId);
                            await ui.SwitchToSessionAsync(earlierSession);
                            Check(ui.SelectedSession?.Id == earlierSessionId && ui.Sessions.Select(s => s.Id).SequenceEqual(historyOrderBeforeSwitch),
                                "opening a history item does not reorder the history list");
                            Check(ui.ChatItems.Count == 4 && ui.ChatItems[0].Text == "first ordered conversation" &&
                                ui.ChatItems[1].Text == "first ordered reply" && ui.ChatItems[2].Text == "second turn question" &&
                                ui.ChatItems[3].Text == "second turn reply",
                                "restoring history displays every saved user and assistant turn");
                            completion.TrySetResult();
                            return;
                        }
                        ui.Model = "startup-selected-model";
                        Check(view.FindControl<Button>("NewConversationButton")!.IsEnabled,
                            "new-conversation button remains clickable in an untouched workspace");
                        await ui.NewConversationCommand.ExecuteAsync(null);
                        Check(ui.IsConnected && ui.ChatItems.Count == 0 && ui.Sessions.Count == 0,
                            "new-conversation command leaves an untouched workspace unchanged");
                        ui.Input = "hello chat";
                        model.EnqueueText("hello ", "stream");
                        await ui.SendCommand.ExecuteAsync(null);
                        Check(ui.ChatItems.Count(c => c.Role == "user") == 1 && ui.ChatItems.Single(c => c.Role == "assistant").Text == "hello stream",
                            "stream updates a single assistant message");
                        Check(store.Search("", profile.Id).Single().Name == "hello chat",
                            "first prompt becomes the persisted history title");
                        Check(ui.Sessions.Single().Binding == ui.Profiles.Single().Binding,
                            "profile edits in the startup blank conversation apply before its first request");
                        Check(ui.CanEditSettings, "agent settings remain editable after the first interaction");
                        ui.Model = "post-interaction-model";
                        ui.SaveProfileCommand.Execute(null);
                        var updatedBinding = ui.Profiles.Single().Binding;
                        Check(ui.CanEditSettings && ui.Status.Contains("新会话将使用最新设置"),
                            "saved profile changes remain available and clearly apply to new sessions");
                        Check(view.FindControl<Button>("NewConversationButton")!.IsEnabled,
                            "new-conversation button is enabled after the first interaction");
                        await ui.NewConversationCommand.ExecuteAsync(null);
                        Check(ui.IsConnected && ui.ChatItems.Count == 0 && ui.Sessions.Count == 1 && ui.CanSend &&
                            !ui.CanStartNewConversation,
                            "new conversation preserves prior history and opens a blank unsaved workspace");

                        model.EnqueueTools(("write_workspace_file", new { path = "chat-once.txt", content = "once" }),
                            ("write_workspace_file", new { path = "chat-denied.txt", content = "denied" }));
                        ui.Input = "two tools";
                        await ui.SendCommand.ExecuteAsync(null);
                        Check(ui.Sessions.Single(s => s.Name == "two tools").Binding == updatedBinding,
                            "new conversations use profile changes saved after prior interaction");
                        var cards = ui.ChatItems.Where(c => c.IsPendingApproval).ToArray();
                        var once = cards.Single(c => c.Text.Contains("chat-once.txt"));
                        var denied = cards.Single(c => c.Text.Contains("chat-denied.txt"));
                        var choosing = ui.AllowOnceCommand.ExecuteAsync(once);
                        Check(!once.IsPendingApproval && denied.IsPendingApproval, "chosen card collapses immediately without dismissing other requests");
                        await choosing;
                        model.EnqueueText("batch completed");
                        await ui.DenyToolCommand.ExecuteAsync(denied);
                        Check(!ui.ChatItems.Any(c => c.IsPendingApproval) && File.ReadAllText(Path.Combine(root, "chat-once.txt")) == "once" &&
                            !File.Exists(Path.Combine(root, "chat-denied.txt")), "collapsed cards preserve independent allow and deny decisions");
                        var requests = model.Requests.Count;
                        await ui.AllowOnceCommand.ExecuteAsync(once);
                        Check(model.Requests.Count == requests, "resolved card cannot replay authorization");

                        model.EnqueueTools(("write_workspace_file", new { path = "chat-session-a.txt", content = "a" }),
                            ("write_workspace_file", new { path = "chat-session-b.txt", content = "b" }));
                        ui.Input = "allow session";
                        await ui.SendCommand.ExecuteAsync(null);
                        model.EnqueueText("session granted");
                        choosing = ui.AllowSessionCommand.ExecuteAsync(ui.ChatItems.First(c => c.IsPendingApproval));
                        Check(!ui.ChatItems.Any(c => c.IsPendingApproval), "session choice immediately collapses all current cards");
                        await choosing;
                        Check(ui.SessionApprovalEnabled && File.Exists(Path.Combine(root, "chat-session-b.txt")), "chat session action grants the live session");
                        ui.RevokeSessionApprovalCommand.Execute(null);

                        model.EnqueueText(string.Join("\n", Enumerable.Repeat("scroll-content", 100)));
                        ui.Input = "long reply";
                        await ui.SendCommand.ExecuteAsync(null);
                        await LayoutAsync();
                        var scroll = view.FindControl<ScrollViewer>("ChatScroll")!;
                        Check(scroll.Extent.Height > scroll.Viewport.Height && scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y <= 32,
                            "new messages automatically scroll to the bottom");
                        scroll.Offset = new Vector(0, 0);
                        await LayoutAsync();
                        var last = ui.ChatItems.Last(c => c.Role == "assistant");
                        last.Text += "\nstream continued while reading history";
                        await LayoutAsync();
                        Check(scroll.Offset.Y == 0 && view.FindControl<Button>("JumpToLatest")!.IsVisible, "streaming does not pull a reader away from history");
                        view.FindControl<Button>("JumpToLatest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await LayoutAsync();
                        Check(scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y <= 32, "latest-message button resumes following");

                        var input = view.FindControl<TextBox>("ChatInput")!;
                        var requestsBeforeNewline = model.Requests.Count;
                        var shiftEnter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Shift };
                        input.RaiseEvent(shiftEnter);
                        Check(input.Text?.Contains('\n') == true && model.Requests.Count == requestsBeforeNewline, "Shift Enter inserts a newline without sending");
                        ui.Input = "keyboard-send";
                        model.EnqueueText("keyboard reply");
                        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                        await ui.SendCommand.ExecutionTask!;
                        Check(ui.ChatItems.Any(c => c.IsUser && c.Text == "keyboard-send"), "Enter sends from the composer");

                        ui.Input = "split credential";
                        model.EnqueueText("local-", "test-key");
                        await ui.SendCommand.ExecuteAsync(null);
                        Check(!ui.ChatItems.Last().Text.Contains("local-test-key"), "message rendering redacts credentials spanning stream chunks");
                        var saved = store.Load(ui.SelectedSession!.Id);
                        var current = typeof(AgentWorkspaceViewModel).GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ui) as StoredConversation;
                        Check(current?.Id == saved.Id,
                            $"selected history points to the live conversation (selected {saved.Id}; current {current?.Id})");
                        Check(saved.Messages?.Count == ui.ChatItems.Count,
                            $"latest cache includes all visible messages (cache {saved.Messages?.Count}; visible {ui.ChatItems.Count}; status: {ui.Status})");
                        Check(saved.Messages is not null && !JsonSerializer.Serialize(saved.Messages).Contains("local-test-key"), "structured history is persisted and redacted");
                        await ui.DisconnectCommand.ExecuteAsync(null);
                        ui.SelectedSession = ui.Sessions.Single(s => s.Id == saved.Id);
                        await ui.RestoreSessionCommand.ExecuteAsync(null);
                        Check(ui.ChatItems.Count == saved.Messages!.Count && !ui.ChatItems.Any(c => c.IsPendingApproval) && !ui.SessionApprovalEnabled,
                            "message history restores without actionable approval cards or session grants");

                        ui.Input = "cancel pending";
                        model.EnqueueTool("write_workspace_file", new { path = "chat-cancelled.txt", content = "blocked" });
                        await ui.SendCommand.ExecuteAsync(null);
                        ui.CancelCommand.Execute(null);
                        Check(!ui.ChatItems.Any(c => c.IsPendingApproval) && !File.Exists(Path.Combine(root, "chat-cancelled.txt")), "cancellation collapses pending cards without execution");
                        Check(ui.CanSend, "send remains enabled after stopping a pending tool request");
                        model.EnqueueText("continued after stop");
                        ui.Input = "continue after stop";
                        await ui.SendCommand.ExecuteAsync(null);
                        Check(ui.CanSend && ui.ChatItems.Last(c => c.Role == "assistant").Text.Contains("continued after stop"),
                            "a stopped conversation accepts and completes the next message");

                        var terminalStarted = Path.Combine(root, "chat-terminal-started");
                        var terminalFinished = Path.Combine(root, "chat-terminal-finished");
                        model.EnqueueTool("run_terminal", new { command = $"Set-Content '{terminalStarted}' 'started'; Start-Sleep -Seconds 30; Set-Content '{terminalFinished}' 'finished'" });
                        ui.Input = "start a cancellable terminal task";
                        await ui.SendCommand.ExecuteAsync(null);
                        var terminalCard = ui.ChatItems.Single(c => c.IsPendingApproval);
                        var terminalRun = ui.AllowOnceCommand.ExecuteAsync(terminalCard);
                        await WaitForFileAsync(terminalStarted, stop.Token);
                        Check(ui.IsBusy && !ui.CanSend, "send is disabled while the approved task is running");
                        ui.CancelCommand.Execute(null);
                        await terminalRun;
                        Check(!ui.IsBusy && ui.CanSend && !File.Exists(terminalFinished),
                            "send is re-enabled after stopping a running task");
                        model.EnqueueText("continued after active stop");
                        ui.Input = "continue after active stop";
                        await ui.SendCommand.ExecuteAsync(null);
                        Check(ui.CanSend && ui.ChatItems.Last(c => c.Role == "assistant").Text.Contains("continued after active stop"),
                            "an actively stopped conversation accepts the next message");
                        var activeSessionId = ui.SelectedSession?.Id ?? throw new InvalidOperationException("Active history was not selected.");
                        foreach (var oldSession in store.Search("", profile.Id).Where(s => s.Id != activeSessionId))
                            store.Delete(oldSession.Id);
                        ui.RefreshSessionsCommand.Execute(null);
                        Check(ui.Sessions.Count == 1 && ui.Sessions[0].Id == activeSessionId,
                            "the current conversation is the final history entry");
                        Check(await ui.DeleteSessionAsync(ui.Sessions[0]) && ui.IsConnected && ui.CanSend &&
                            ui.ChatItems.Count == 0 && ui.Sessions.Count == 0,
                            "deleting the final active history saves and removes it, then opens a blank conversation");
                        Check(await ui.CloseAsync(), "chat window closes and persists safely");
                        ui = null;
                        completion.TrySetResult();
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    finally
                    {
                        if (ui is not null) await ui.CloseAsync();
                        window?.Close();
                        stop.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(stop.Token);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(ct);
        thread.Join();
        Console.WriteLine("PASS: live chat cards, streaming, scrolling, keyboard, cancellation and structured history");
    }

    static async Task CheckSessionApprovalAsync(FakeModel model, ConcurrentQueue<string> output, string root, string keyName, CancellationToken ct)
    {
        var profile = new AgentProfile { Name = "Session approval", Endpoint = model.Endpoint, Model = "fake-model", KeyEnvironmentVariable = keyName,
            WorkspaceRoot = root, Files = true };
        HarnessSettings Settings() => new(profile.Endpoint, profile.Model, profile.KeyEnvironmentVariable, "", profile);
        StoredConversation stored;
        using (var workspace = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await workspace.InitializeAsync(ct);
            model.EnqueueTool("write_workspace_file", new { path = "session-once.txt", content = "once" });
            var approval = (await workspace.SendAsync("write once", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("once done");
            await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(!workspace.ApproveForSession && File.ReadAllText(Path.Combine(root, "session-once.txt")) == "once", "single approval does not grant session access");

            model.EnqueueTools(
                ("write_workspace_file", new { path = "session-denied.txt", content = "denied" }),
                ("write_workspace_file", new { path = "session-first.txt", content = "first" }),
                ("write_workspace_file", new { path = "session-batch.txt", content = "batch" }));
            var batch = (await workspace.SendAsync("write batch", cancellationToken: ct)).Approvals;
            var denied = batch.Single(a => a.Details.Contains("session-denied.txt"));
            var remaining = await workspace.DecideAsync(denied.Id, false, cancellationToken: ct);
            approval = remaining.Approvals.Single(a => a.Details.Contains("session-first.txt"));
            model.EnqueueText("session enabled");
            await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            Check(workspace.ApproveForSession && File.ReadAllText(Path.Combine(root, "session-first.txt")) == "first" &&
                File.ReadAllText(Path.Combine(root, "session-batch.txt")) == "batch" && !File.Exists(Path.Combine(root, "session-denied.txt")),
                "session approval executes all remaining requests without overriding prior denial");

            model.EnqueueTool("write_workspace_file", new { path = "session-next.txt", content = "session-native-payload-438" });
            model.EnqueueText("automatic write complete");
            var automatic = await workspace.SendAsync("write again", cancellationToken: ct);
            Check(automatic.Approvals.Count == 0 && File.ReadAllText(Path.Combine(root, "session-next.txt")) == "session-native-payload-438", "future request automatically approved in same session");

            using (var schema = JsonDocument.Parse(model.Requests.Last()))
            {
                var nativeRead = schema.RootElement.GetProperty("tools").EnumerateArray().Single(t =>
                    t.GetProperty("function").GetProperty("name").GetString() == "file_access_read").GetProperty("function").GetProperty("parameters");
                var nativeArgs = nativeRead.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "session-next.txt");
                model.EnqueueTool("file_access_read", nativeArgs);
                model.EnqueueText("native automatically read");
                var result = await workspace.SendAsync("read with native tool", cancellationToken: ct);
                using var request = JsonDocument.Parse(model.Requests.Last());
                Check(result.Approvals.Count == 0 && request.RootElement.GetProperty("messages").EnumerateArray().Any(m =>
                    m.GetProperty("role").GetString() == "tool" && m.GetRawText().Contains("session-native-payload-438")), "session approval covers native file access");
            }

            stored = new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding, ProfileSnapshot = profile,
                Session = await workspace.SerializeAsync(ct) };
            workspace.RevokeSessionApproval();
            model.EnqueueTool("write_workspace_file", new { path = "session-revoked.txt", content = "blocked" });
            approval = (await workspace.SendAsync("after revoke", cancellationToken: ct)).Approvals.Single();
            Check(!workspace.ApproveForSession && !File.Exists(Path.Combine(root, "session-revoked.txt")), "revocation restores approval requirement");
            model.EnqueueText("denied");
            await workspace.DecideAsync(approval.Id, false, cancellationToken: ct);
            Check(!File.Exists(Path.Combine(root, "session-revoked.txt")), "rejected request is not executed");
        }
        using (var restored = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await restored.RestoreAsync(stored, profile, ct);
            model.EnqueueTool("write_workspace_file", new { path = "session-restored.txt", content = "blocked" });
            var approval = (await restored.SendAsync("after restore", cancellationToken: ct)).Approvals.Single();
            Check(!restored.ApproveForSession && !File.Exists(Path.Combine(root, "session-restored.txt")), "session approval is not restored from history");
            model.EnqueueText("denied after restore");
            await restored.DecideAsync(approval.Id, false, cancellationToken: ct);
        }
        using (var fresh = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await fresh.InitializeAsync(ct);
            model.EnqueueTool("write_workspace_file", new { path = "session-fresh.txt", content = "blocked" });
            var approval = (await fresh.SendAsync("new session", cancellationToken: ct)).Approvals.Single();
            Check(!fresh.ApproveForSession && !File.Exists(Path.Combine(root, "session-fresh.txt")), "new session requires approval");
            model.EnqueueText("denied in new session");
            await fresh.DecideAsync(approval.Id, false, cancellationToken: ct);
        }
        using (var bounded = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await bounded.InitializeAsync(ct);
            model.EnqueueTool("write_workspace_file", new { path = "session-boundary.txt", content = "ok" });
            var approval = (await bounded.SendAsync("enable approval", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("enabled");
            await bounded.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            var escapeName = Path.GetFileName(root) + "-escape.txt";
            model.EnqueueTool("write_workspace_file", new { path = "../" + escapeName, content = "blocked" });
            try { await bounded.SendAsync("escape", cancellationToken: ct); throw new Exception("FAIL: session authorization bypassed workspace boundary"); }
            catch (UnauthorizedAccessException) { }
            Check(!bounded.ApproveForSession && !File.Exists(Path.Combine(Path.GetDirectoryName(root)!, escapeName)), "boundary failure revokes session approval");
        }
        using (var reset = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await reset.InitializeAsync(ct);
            model.EnqueueTool("write_workspace_file", new { path = "session-reset-start.txt", content = "ok" });
            var approval = (await reset.SendAsync("enable approval", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("enabled");
            await reset.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            await reset.ResetAsync(ct);
            model.EnqueueTool("write_workspace_file", new { path = "session-reset-after.txt", content = "blocked" });
            approval = (await reset.SendAsync("after reset", cancellationToken: ct)).Approvals.Single();
            Check(!reset.ApproveForSession && !File.Exists(Path.Combine(root, "session-reset-after.txt")), "reset clears session approval");
            reset.Cancel();
        }

        var skillRoot = Path.Combine(root, "session-skills", "sample");
        Directory.CreateDirectory(skillRoot);
        await File.WriteAllTextAsync(Path.Combine(skillRoot, "SKILL.md"), "---\nname: sample\ndescription: session skill\n---\nsession-skill-body-684", ct);
        await File.WriteAllTextAsync(Path.Combine(skillRoot, "guide.md"), "session-skill-resource-684", ct);
        var skillProfile = profile with { AutoSkills = true, SkillsDirectory = "session-skills" };
        using (var skills = new HarnessWorkspace(new HarnessSettings(skillProfile.Endpoint, skillProfile.Model, skillProfile.KeyEnvironmentVariable, "", skillProfile), output.Enqueue))
        {
            await skills.InitializeAsync(ct);
            model.EnqueueText("skills ready");
            await skills.SendAsync("discover skills", cancellationToken: ct);
            Dictionary<string, object> SkillArgs(string name)
            {
                using var schema = JsonDocument.Parse(model.Requests.Last());
                var properties = schema.RootElement.GetProperty("tools").EnumerateArray().Single(t =>
                    t.GetProperty("function").GetProperty("name").GetString() == name).GetProperty("function").GetProperty("parameters").GetProperty("properties");
                return properties.EnumerateObject().ToDictionary(p => p.Name, p => (object)(p.Name.Contains("resource", StringComparison.OrdinalIgnoreCase) ? "guide.md" : "sample"));
            }
            var loadArgs = SkillArgs("load_skill");
            var resourceArgs = SkillArgs("read_skill_resource");
            model.EnqueueTool("load_skill", loadArgs);
            var approval = (await skills.SendAsync("load skill", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("skill loaded");
            await skills.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            model.EnqueueTool("read_skill_resource", resourceArgs);
            model.EnqueueText("resource read");
            var result = await skills.SendAsync("read skill resource", cancellationToken: ct);
            using var request = JsonDocument.Parse(model.Requests.Last());
            Check(result.Approvals.Count == 0 && request.RootElement.GetProperty("messages").EnumerateArray().Any(m =>
                m.GetProperty("role").GetString() == "tool" && m.GetRawText().Contains("session-skill-resource-684")), "session approval covers guarded native skills");
            skills.Cancel();
            Check(!skills.ApproveForSession, "cancellation clears session approval");
        }
        using (var looping = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await looping.InitializeAsync(ct);
            model.EnqueueTool("write_workspace_file", new { path = "auto-loop-0.txt", content = "0" });
            var approval = (await looping.SendAsync("start a long tool sequence", cancellationToken: ct)).Approvals.Single();
            for (var i = 1; i <= 9; i++)
                model.EnqueueTool("write_workspace_file", new { path = $"auto-loop-{i}.txt", content = i.ToString() });
            var limitText = new ConcurrentQueue<string>();
            var result = await looping.ApproveSessionAsync(approval.Id, limitText.Enqueue, ct);
            Check(result.Approvals.Single().Details.Contains("auto-loop-9.txt") && !looping.ApproveForSession &&
                Enumerable.Range(0, 9).All(i => File.Exists(Path.Combine(root, $"auto-loop-{i}.txt"))) &&
                !File.Exists(Path.Combine(root, "auto-loop-9.txt")) && string.Concat(limitText).Contains("8 次上限"),
                "session auto-approval stops after eight tool calls and requests individual approval");
            looping.Cancel();
        }
        Console.WriteLine("PASS: session and one-time approvals, revocation, isolation, boundaries and native tools");
    }

    static async Task CheckTerminalLoopAsync(FakeModel model, ConcurrentQueue<string> output, string root, string keyName, CancellationToken ct)
    {
        var profile = new AgentProfile { Name = "Terminal loop", Endpoint = model.Endpoint, Model = "fake-model",
            KeyEnvironmentVariable = keyName, WorkspaceRoot = root, Terminal = true, Files = true };
        HarnessSettings Settings() => new(profile.Endpoint, profile.Model, profile.KeyEnvironmentVariable, root, profile);
        const string repeat = "Add-Content -LiteralPath 'loop-repeat.txt' -Value 'executed'";
        using (var workspace = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await workspace.InitializeAsync(ct);
            model.EnqueueTool("run_terminal", new { command = repeat });
            var approval = (await workspace.SendAsync("start repeated command", cancellationToken: ct)).Approvals.Single();
            model.EnqueueTool("run_terminal", new { command = "  " + repeat + "  " });
            var notices = new ConcurrentQueue<string>();
            var paused = await workspace.ApproveSessionAsync(approval.Id, notices.Enqueue, ct);
            Check(paused.Approvals.Count == 1 && !workspace.ApproveForSession &&
                File.ReadAllLines(Path.Combine(root, "loop-repeat.txt")).Length == 1 && string.Concat(notices).Contains("暂停自动执行"),
                "duplicate terminal command pauses before execution, including the initial manually approved call");

            model.EnqueueTool("run_terminal", new { command = repeat });
            var pausedAgain = await workspace.ApproveSessionAsync(paused.Approvals.Single().Id, cancellationToken: ct);
            Check(pausedAgain.Approvals.Count == 1 && !workspace.ApproveForSession &&
                File.ReadAllLines(Path.Combine(root, "loop-repeat.txt")).Length == 2,
                "explicit approval executes once but does not permit another duplicate across approval resumption");
            model.EnqueueText("repeated execution approved");
            await workspace.DecideAsync(pausedAgain.Approvals.Single().Id, true, cancellationToken: ct);
            Check(File.ReadAllLines(Path.Combine(root, "loop-repeat.txt")).Length == 3, "one-time approval permits the requested repeat");

            model.EnqueueTool("run_terminal", new { command = repeat });
            approval = (await workspace.SendAsync("new user request", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("session enabled again");
            await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            model.EnqueueTool("run_terminal", new { command = repeat });
            model.EnqueueText("new turn completed");
            var fresh = await workspace.SendAsync("another user request", cancellationToken: ct);
            Check(fresh.Approvals.Count == 0 && File.ReadAllLines(Path.Combine(root, "loop-repeat.txt")).Length == 5,
                "a new user turn resets duplicate detection");
        }
        using (var workspace = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await workspace.InitializeAsync(ct);
            model.EnqueueTool("write_workspace_file", new { path = "loop-enable.txt", content = "enabled" });
            var approval = (await workspace.SendAsync("enable approval", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("enabled");
            await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            const string batch = "Add-Content -LiteralPath 'loop-batch.txt' -Value 'executed'";
            model.EnqueueTools(("run_terminal", new { command = batch }), ("run_terminal", new { command = batch }));
            var pending = await workspace.SendAsync("duplicate batch", cancellationToken: ct);
            Check(pending.Approvals.Count == 2 && !workspace.ApproveForSession && !File.Exists(Path.Combine(root, "loop-batch.txt")),
                "duplicate commands in a single batch are not automatically executed");
            var remaining = await workspace.DecideAsync(pending.Approvals[0].Id, true, cancellationToken: ct);
            model.EnqueueText("batch resolved");
            await workspace.DecideAsync(remaining.Approvals.Single().Id, false, cancellationToken: ct);
            Check(File.ReadAllLines(Path.Combine(root, "loop-batch.txt")).Length == 1, "batch decisions execute only the approved command");
        }
        using (var workspace = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await workspace.InitializeAsync(ct);
            model.EnqueueTool("run_terminal", new { command = "Write-Output 'same-result'" });
            var approval = (await workspace.SendAsync("repeated result", cancellationToken: ct)).Approvals.Single();
            model.EnqueueTool("run_terminal", new { command = "Write-Output ('same-' + 'result')" });
            model.EnqueueTool("run_terminal", new { command = "Set-Content -LiteralPath 'loop-result.txt' -Value 'executed'" });
            var pending = await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            Check(pending.Approvals.Count == 1 && !workspace.ApproveForSession && !File.Exists(Path.Combine(root, "loop-result.txt")),
                "two identical nonempty results stop the next terminal command even when commands differ");
            model.EnqueueText("stopped");
            await workspace.DecideAsync(pending.Approvals.Single().Id, false, cancellationToken: ct);
        }
        using (var workspace = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await workspace.InitializeAsync(ct);
            model.EnqueueTool("run_terminal", new { command = "Write-Output 'first-result'" });
            var approval = (await workspace.SendAsync("different results", cancellationToken: ct)).Approvals.Single();
            model.EnqueueTool("run_terminal", new { command = "Write-Output 'second-result'" });
            model.EnqueueTool("run_terminal", new { command = "Set-Content -LiteralPath 'loop-distinct.txt' -Value 'executed'" });
            model.EnqueueText("different commands completed");
            var result = await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            Check(result.Approvals.Count == 0 && File.Exists(Path.Combine(root, "loop-distinct.txt")),
                "distinct commands with different results continue normally");
        }
        using (var workspace = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await workspace.InitializeAsync(ct);
            model.EnqueueTool("run_terminal", new { command = "exit 7" });
            var approval = (await workspace.SendAsync("repeated failure", cancellationToken: ct)).Approvals.Single();
            model.EnqueueTool("run_terminal", new { command = "exit (3 + 4)" });
            model.EnqueueTool("run_terminal", new { command = "Set-Content -LiteralPath 'loop-failure.txt' -Value 'executed'" });
            var pending = await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            Check(pending.Approvals.Count == 1 && !File.Exists(Path.Combine(root, "loop-failure.txt")),
                "identical nonzero exit codes without output stop the next terminal command");
            model.EnqueueText("failure stopped");
            await workspace.DecideAsync(pending.Approvals.Single().Id, false, cancellationToken: ct);
        }
        using (var workspace = new HarnessWorkspace(Settings(), output.Enqueue))
        {
            await workspace.InitializeAsync(ct);
            model.EnqueueTool("run_terminal", new { command = "Set-Content -LiteralPath 'loop-empty-1.txt' -Value '1'" });
            var approval = (await workspace.SendAsync("silent commands", cancellationToken: ct)).Approvals.Single();
            model.EnqueueTool("run_terminal", new { command = "Set-Content -LiteralPath 'loop-empty-2.txt' -Value '2'" });
            model.EnqueueTool("run_terminal", new { command = "Set-Content -LiteralPath 'loop-empty-3.txt' -Value '3'" });
            model.EnqueueText("silent commands completed");
            var result = await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
            Check(result.Approvals.Count == 0 && File.Exists(Path.Combine(root, "loop-empty-3.txt")),
                "distinct successful commands with empty output are not mistaken for a loop");
        }
        Console.WriteLine("Terminal loop prevention checks passed.");
    }

    static async Task CheckTerminalResultAsync(FakeModel model, ConcurrentQueue<string> output, string root, string keyName, CancellationToken ct)
    {
        var profile = new AgentProfile { Name = "Terminal result", Endpoint = model.Endpoint, Model = "fake-model",
            KeyEnvironmentVariable = keyName, WorkspaceRoot = root, Terminal = true, Files = true };
        using var workspace = new HarnessWorkspace(new HarnessSettings(profile.Endpoint, profile.Model,
            profile.KeyEnvironmentVariable, root, profile), output.Enqueue);
        await workspace.InitializeAsync(ct);
        model.EnqueueTool("run_terminal", new { command = "Write-Output 'generic-result-42'; [Environment]::OSVersion.Version.ToString()" });
        var approval = (await workspace.SendAsync("check computer version", cancellationToken: ct)).Approvals.Single();
        Check(approval.Details.Contains("发送给当前模型") && approval.Details.Contains("敏感信息") &&
            !output.Any(s => s.Contains("generic-result-42")), "terminal execution and output disclosure require approval");
        model.EnqueueText("version summarized");
        await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
        using (var request = JsonDocument.Parse(model.Requests.Last()))
        {
            var result = request.RootElement.GetProperty("messages").EnumerateArray().Last(m => m.GetProperty("role").GetString() == "tool").GetRawText();
            Check(result.Contains("generic-result-42") && result.Contains(Environment.OSVersion.Version.ToString()) &&
                result.Contains("Exit code: 0") && output.Any(s => s.Contains("generic-result-42")),
                "approved arbitrary terminal output and exit code reach both model and local log");
        }

        model.EnqueueTool("run_terminal", new { command = $"Write-Output $env:{keyName}; [Console]::Error.WriteLine('failure-detail-42'); exit 7" });
        approval = (await workspace.SendAsync("run failing command", cancellationToken: ct)).Approvals.Single();
        model.EnqueueText("failure summarized");
        await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
        using (var request = JsonDocument.Parse(model.Requests.Last()))
        {
            var result = request.RootElement.GetProperty("messages").EnumerateArray().Last(m => m.GetProperty("role").GetString() == "tool").GetRawText();
            Check(result.Contains("Exit code: 7") && result.Contains("failure-detail-42") && !result.Contains("local-test-key"),
                "failure exit code and stderr reach model while configured API key is redacted");
        }

        model.EnqueueTool("run_terminal", new { command = "Write-Output 'bounded-prefix'; Write-Output ('x' * 10000)" });
        approval = (await workspace.SendAsync("large output", cancellationToken: ct)).Approvals.Single();
        model.EnqueueText("large output summarized");
        await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
        using (var request = JsonDocument.Parse(model.Requests.Last()))
        {
            var result = request.RootElement.GetProperty("messages").EnumerateArray().Last(m => m.GetProperty("role").GetString() == "tool").GetRawText();
            Check(result.Contains("bounded-prefix") && result.Contains("Output truncated at 8192 characters") && result.Length < 8500,
                "large output is bounded and explicitly marked as truncated");
        }

        var marker = Path.Combine(root, "terminal-denied.txt");
        model.EnqueueTool("run_terminal", new { command = "Set-Content -LiteralPath 'terminal-denied.txt' -Value 'not allowed'" });
        approval = (await workspace.SendAsync("deny command", cancellationToken: ct)).Approvals.Single();
        model.EnqueueText("command denied");
        await workspace.DecideAsync(approval.Id, false, cancellationToken: ct);
        Check(!File.Exists(marker), "denied terminal command does not execute");

        model.EnqueueTool("write_workspace_file", new { path = "terminal-session-approval.txt", content = "approved" });
        approval = (await workspace.SendAsync("enable session approval", cancellationToken: ct)).Approvals.Single();
        Check(approval.Details.Contains("后续终端命令") && approval.Details.Contains("输出发送给模型"),
            "non-terminal approval discloses terminal output sharing before session-wide approval");
        model.EnqueueText("session approved");
        await workspace.ApproveSessionAsync(approval.Id, cancellationToken: ct);
        model.EnqueueTool("run_terminal", new { command = "Write-Output 'session-terminal-result-42'" });
        model.EnqueueText("session terminal result summarized");
        var automatic = await workspace.SendAsync("run another command", cancellationToken: ct);
        using (var request = JsonDocument.Parse(model.Requests.Last()))
        {
            var result = request.RootElement.GetProperty("messages").EnumerateArray().Last(m => m.GetProperty("role").GetString() == "tool").GetRawText();
            Check(automatic.Approvals.Count == 0 && result.Contains("session-terminal-result-42"),
                "session-approved terminal output reaches the model without another approval");
        }
        Console.WriteLine("Generic terminal result checks passed.");
    }

    static async Task CheckAutomaticAsync(FakeModel model, ConcurrentQueue<string> output, string root, string keyName, CancellationToken ct)
    {
        var mem = Path.Combine(root, "auto-memory");
        var skills = Path.Combine(root, "auto-skills");
        Directory.CreateDirectory(mem);
        Directory.CreateDirectory(Path.Combine(skills, "example", "references"));
        await File.WriteAllTextAsync(Path.Combine(mem, "facts.txt"), "persistent-memory-v1 local-test-key sk-1234567890abcdef\n{\"api_key\":\"quoted-secret-12345\"}\nAuthorization: Bearer bearer-secret-12345", ct);
        var definition = Path.Combine(skills, "example", "SKILL.md");
        await File.WriteAllTextAsync(definition, "---\nname: example\ndescription: skill-advertisement-v1\n---\nprivate-skill-body-v1", ct);
        await File.WriteAllTextAsync(Path.Combine(skills, "example", "references", "guide.md"), "private-resource-content local-test-key", ct);
        await File.WriteAllTextAsync(Path.Combine(root, "outside.txt"), "outside-resource-secret", ct);
        var profile = new AgentProfile { Name = "Automatic", Endpoint = model.Endpoint, Model = "fake-model", KeyEnvironmentVariable = keyName,
            WorkspaceRoot = root, MemoryDirectory = "auto-memory", SkillsDirectory = "auto-skills" };
        HarnessSettings Settings(AgentProfile p) => new(p.Endpoint, p.Model, p.KeyEnvironmentVariable, p.ScriptsDirectory, p);
        using (var disabled = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await disabled.InitializeAsync(ct);
            model.EnqueueText("disabled");
            await disabled.SendAsync("hello", cancellationToken: ct);
            Check(!model.Requests.Last().Contains("persistent-memory") && !model.Requests.Last().Contains("skill-advertisement") &&
                !model.Requests.Last().Contains("load_skill") && !model.Requests.Last().Contains("search_web"), "automatic capabilities disabled by default");
        }
        using (var disabled = new HarnessWorkspace(Settings(profile with { MemoryDirectory = "missing-auto-memory", SkillsDirectory = "missing-auto-skills" }), output.Enqueue))
        {
            await disabled.InitializeAsync(ct);
            model.EnqueueText("disabled directories not touched");
            await disabled.SendAsync("hello", cancellationToken: ct);
            Check(!Directory.Exists(Path.Combine(root, "missing-auto-memory")), "disabled automatic memory never reads/creates configured missing directory");
            Check(!Directory.Exists(Path.Combine(root, "missing-auto-skills")), "disabled automatic skills never reads/creates configured missing directory");
        }
        foreach (var item in new[]
        {
            (Profile: profile with { AutoSkills = true, SkillsDirectory = "missing-auto-skills/nested" }, Directory: "missing-auto-skills/nested"),
            (Profile: profile with { AutoMemory = true, MemoryDirectory = "missing-auto-memory/nested" }, Directory: "missing-auto-memory/nested"),
            (Profile: profile with { Skills = true, SkillsDirectory = "manual-skills/nested" }, Directory: "manual-skills/nested"),
            (Profile: profile with { FileMemory = true, MemoryDirectory = "manual-memory/nested" }, Directory: "manual-memory/nested")
        })
        {
            using var created = new HarnessWorkspace(Settings(item.Profile), output.Enqueue);
            await created.InitializeAsync(ct);
            Check(Directory.Exists(Path.Combine(root, item.Directory)), "enabled context directory is created recursively");
            model.EnqueueText("empty context ready");
            await created.SendAsync("hello", cancellationToken: ct);
        }
        var newRoot = Path.Combine(root, "new-workspace", "nested");
        using (var created = new HarnessWorkspace(Settings(profile with { WorkspaceRoot = newRoot, AutoMemory = true, AutoSkills = true }), output.Enqueue))
        {
            await created.InitializeAsync(ct);
            Check(Directory.Exists(Path.Combine(newRoot, "auto-memory")) && Directory.Exists(Path.Combine(newRoot, "auto-skills")), "new workspace and enabled context directories are created");
        }
        try
        {
            using var escaped = new HarnessWorkspace(Settings(profile with { AutoMemory = true, MemoryDirectory = "../outside-memory" }), output.Enqueue);
            throw new Exception("FAIL: escaped context directory accepted");
        }
        catch (UnauthorizedAccessException) { }
        var collision = Path.Combine(root, "context-file");
        await File.WriteAllTextAsync(collision, "not a directory", ct);
        try
        {
            using var invalid = new HarnessWorkspace(Settings(profile with { AutoMemory = true, MemoryDirectory = "context-file" }), output.Enqueue);
            throw new Exception("FAIL: file accepted as context directory");
        }
        catch (IOException) { }
        // Default new fields are omitted, preserving the exact historical SHA256 serialization binding.
        var legacyNode = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(profile))!.AsObject();
        foreach (var name in new[] { "AutoMemory", "AutoSkills", "WebSearch", "SearchEndpoint" }) legacyNode.Remove(name);
        var legacyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(legacyNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false }))));
        var legacy = new StoredConversation { ProfileId = profile.Id, Binding = legacyHash, ProfileSnapshot = profile, Session = JsonSerializer.SerializeToElement(new { }) };
        PlatformStore.ValidateBinding(legacy, profile);
        Check(legacyHash == profile.Binding, "legacy default profile binding byte-identical");
        foreach (var changed in new[] { profile with { AutoMemory = true }, profile with { AutoSkills = true }, profile with { WebSearch = true },
            profile with { SearchEndpoint = "https://example.invalid/search" } })
        {
            try { PlatformStore.ValidateBinding(legacy, changed); throw new Exception("FAIL: new capability accepted old binding"); }
            catch (InvalidOperationException) { }
        }
        profile = profile with { AutoMemory = true, AutoSkills = true };
        StoredConversation stored;
        using (var automatic = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await automatic.InitializeAsync(ct);
            model.EnqueueText("automatic");
            await automatic.SendAsync("hello automatic", cancellationToken: ct);
            var request = model.Requests.Last();
            Check(request.Contains("persistent-memory-v1") && request.Contains("skill-advertisement-v1") &&
                !request.Contains("private-skill-body-v1") && !request.Contains("sk-1234567890abcdef") &&
                !request.Contains("quoted-secret-12345") && !request.Contains("bearer-secret-12345"), "real memory injected and native skills advertise metadata only with credential redaction");
            using (var parsed = JsonDocument.Parse(request))
                Check(!parsed.RootElement.GetProperty("messages").GetRawText().Contains("local-test-key"), "automatic memory never sends model key as data");
            Dictionary<string, object> SkillArgs(string name, string? resource = null)
            {
                using var doc = JsonDocument.Parse(model.Requests.Last());
                var props = doc.RootElement.GetProperty("tools").EnumerateArray().Single(t =>
                    t.GetProperty("function").GetProperty("name").GetString() == name).GetProperty("function").GetProperty("parameters").GetProperty("properties");
                return props.EnumerateObject().ToDictionary(p => p.Name, p => (object)(p.Name.Contains("resource", StringComparison.OrdinalIgnoreCase) ? resource! : "example"));
            }
            var loadArgs = SkillArgs("load_skill");
            model.EnqueueTool("load_skill", loadArgs);
            var approval = (await automatic.SendAsync("load example", cancellationToken: ct)).Approvals.Single();
            Check(approval.Task == "load_skill" && approval.Details.Contains("example"), "native skill approval displays SDK arguments");
            model.EnqueueText("skill refused");
            await automatic.DecideAsync(approval.Id, false, cancellationToken: ct);
            Check(!model.Requests.Last().Contains("private-skill-body-v1"), "skill rejected without body disclosure");
            model.EnqueueTool("load_skill", loadArgs);
            approval = (await automatic.SendAsync("load approved", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("skill loaded");
            await automatic.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(model.Requests.Last().Contains("private-skill-body-v1"), "real SDK load_skill approved via one-shot grant");
            using (var schema = JsonDocument.Parse(model.Requests.Last()))
                Check(!schema.RootElement.GetProperty("tools").EnumerateArray().Any(t =>
                    t.GetProperty("function").GetProperty("name").GetString() == "run_skill_script"), "skill execution not advertised; no runner or arbitrary shell enabled");
            var resourceArgs = SkillArgs("read_skill_resource", "references/guide.md");
            model.EnqueueTool("read_skill_resource", resourceArgs);
            approval = (await automatic.SendAsync("read resource", cancellationToken: ct)).Approvals.Single();
            Check(!model.Requests.Last().Contains("private-resource-content"), "skill resource waits for approval");
            model.EnqueueText("resource read");
            await automatic.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(model.Requests.Last().Contains("private-resource-content"), "real SDK resource approved");
            using (var parsed = JsonDocument.Parse(model.Requests.Last()))
                Check(!parsed.RootElement.GetProperty("messages").GetRawText().Contains("local-test-key"), "native resource credentials redacted before model transmission");
            await File.WriteAllTextAsync(Path.Combine(mem, "facts.txt"), "persistent-memory-v2", ct);
            await File.WriteAllTextAsync(definition, "---\nname: example\ndescription: skill-advertisement-v2\n---\nprivate-skill-body-v2", ct);
            model.EnqueueText("refreshed");
            await automatic.SendAsync("refresh files", cancellationToken: ct);
            Check(model.Requests.Last().Contains("persistent-memory-v2") && model.Requests.Last().Contains("skill-advertisement-v2"), "memory and skill metadata refresh next turn");
            Check(!model.Requests.Last().Contains("persistent-memory-v1"), "automatic memory context does not accumulate obsolete directory snapshots in conversation");
            model.EnqueueTool("load_skill", loadArgs);
            approval = (await automatic.SendAsync("changed skill", cancellationToken: ct)).Approvals.Single();
            await File.AppendAllTextAsync(definition, "\nchanged-after-approval", ct);
            model.EnqueueText("changed skill refused");
            await automatic.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(!model.Requests.Last().Contains("changed-after-approval"), "native skill hash grant rejects changed body");
            model.EnqueueTool("read_skill_resource", SkillArgs("read_skill_resource", "../../outside.txt"));
            approval = (await automatic.SendAsync("escape resource", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("escape blocked");
            await automatic.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(!model.Requests.Last().Contains("outside-resource-secret"), "native resource traversal cannot escape");
            model.EnqueueTool("load_skill", loadArgs);
            await automatic.SendAsync("leave unresolved", cancellationToken: ct);
            stored = new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding, ProfileSnapshot = profile, UnsafeToResume = true,
                Session = await automatic.SerializeAsync(ct) };
        }
        using (var resumed = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await resumed.RestoreAsync(stored, profile, ct);
            Check(resumed.PendingApprovals.Count == 0, "native skill approval never restored as grant");
            await File.WriteAllTextAsync(Path.Combine(mem, "facts.txt"), "cross-session-memory", ct);
            model.EnqueueText("cross session");
            await resumed.SendAsync("continue", cancellationToken: ct);
            Check(model.Requests.Last().Contains("cross-session-memory") && model.Requests.Last().Contains("reject", StringComparison.OrdinalIgnoreCase),
                "cross-session memory reloaded and native stale approval rejected");
        }
        using (var fresh = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await fresh.InitializeAsync(ct);
            model.EnqueueText("independent new session");
            await fresh.SendAsync("hello", cancellationToken: ct);
            Check(model.Requests.Last().Contains("cross-session-memory") && !model.Requests.Last().Contains("hello automatic"),
                "independent new session loads persistent user memory without copying chat");
        }
        // Validate corrupt/over-budget directories before any fake model request.
        foreach (var bad in new[] { new string('x', 65537), "\0invalid" })
        {
            await File.WriteAllTextAsync(Path.Combine(mem, "facts.txt"), bad, ct);
            using var invalid = new HarnessWorkspace(Settings(profile with { AutoSkills = false }), output.Enqueue);
            await invalid.InitializeAsync(ct);
            var before = model.Requests.Count;
            try { await invalid.SendAsync("invalid context", cancellationToken: ct); throw new Exception("FAIL: invalid memory accepted"); }
            catch (InvalidDataException) { }
            Check(model.Requests.Count == before, "memory corrupt/over-budget fails explicitly before model");
        }
        await File.WriteAllTextAsync(Path.Combine(mem, "facts.txt"), "safe", ct);
        await File.WriteAllTextAsync(definition, "invalid frontmatter", ct);
        using (var invalid = new HarnessWorkspace(Settings(profile with { AutoMemory = false }), output.Enqueue))
        {
            await invalid.InitializeAsync(ct);
            try { await invalid.SendAsync("bad skill", cancellationToken: ct); throw new Exception("FAIL: corrupt skill accepted"); }
            catch (InvalidDataException) { }
        }
        await File.WriteAllTextAsync(definition, "---\nname: example\ndescription: valid\n---\nbody", ct);
        async Task RefuseDirectory(AgentProfile p, string label)
        {
            using var invalid = new HarnessWorkspace(Settings(p), output.Enqueue);
            await invalid.InitializeAsync(ct);
            var before = model.Requests.Count;
            try { await invalid.SendAsync("invalid directory", cancellationToken: ct); throw new Exception("FAIL: " + label); }
            catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException or DecoderFallbackException or JsonException) { }
            Check(model.Requests.Count == before, label + " fails before metadata/model transmission");
        }
        await File.WriteAllTextAsync(definition, "---\nname: example\ndescription: local-test-key\n---\nbody", ct);
        await RefuseDirectory(profile with { AutoMemory = false }, "credential-bearing skill metadata");
        await File.WriteAllTextAsync(definition, new string('x', 65537), ct);
        await RefuseDirectory(profile with { AutoMemory = false }, "oversize SKILL.md");
        await File.WriteAllTextAsync(definition, "---\nname: example\ndescription: valid\n---\nbody", ct);
        await File.WriteAllTextAsync(Path.Combine(mem, "bad.json"), "{bad-json", ct);
        await RefuseDirectory(profile with { AutoSkills = false }, "malformed JSON memory");
        File.Delete(Path.Combine(mem, "bad.json"));
        var excessive = Path.Combine(root, "excess-context");
        Directory.CreateDirectory(excessive);
        for (int i = 0; i < 129; i++) await File.WriteAllTextAsync(Path.Combine(excessive, i + ".txt"), "x", ct);
        await RefuseDirectory(profile with { AutoSkills = false, MemoryDirectory = "excess-context" }, "memory file-count budget");
        await RefuseDirectory(profile with { AutoMemory = false, SkillsDirectory = "excess-context" }, "skills file-count metadata budget");
        Directory.Delete(excessive, true);
        Directory.CreateDirectory(excessive);
        for (int i = 0; i < 5; i++) await File.WriteAllTextAsync(Path.Combine(excessive, i + ".txt"), new string('a', 60000), ct);
        await RefuseDirectory(profile with { AutoSkills = false, MemoryDirectory = "excess-context" }, "memory total-byte budget");
        Directory.Delete(excessive, true);
        Directory.CreateDirectory(excessive);
        await File.WriteAllBytesAsync(Path.Combine(excessive, "bad.txt"), [0xFF, 0xFF], ct);
        await RefuseDirectory(profile with { AutoSkills = false, MemoryDirectory = "excess-context" }, "invalid UTF8 memory");
        Directory.Delete(excessive, true);
        var junction = Path.Combine(skills, "escape");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                {
                    Arguments = $"/c mklink /J \"{junction}\" \"{mem}\"", UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                })!;
                await process.WaitForExitAsync(ct);
                Check(process.ExitCode == 0, "automatic context test junction created");
            }
            else Directory.CreateSymbolicLink(junction, mem);
            await RefuseDirectory(profile with { AutoMemory = false }, "skills junction escape rejected before native scanning");
            await RefuseDirectory(profile with { AutoSkills = false, MemoryDirectory = "auto-skills" }, "memory junction escape rejected before scanning");
        }
        finally { if (Directory.Exists(junction)) Directory.Delete(junction); }
        var store = new PlatformStore(Path.Combine(root, "automatic-cache"));
        await using var search = new MockSearch();
        profile = profile with { WebSearch = true, SearchEndpoint = search.Endpoint };
        store.SaveProfiles([profile]);
        Check(store.LoadProfiles().Single().Binding == profile.Binding && store.LoadProfiles().Single().AutoSkills &&
            store.LoadProfiles().Single().AutoMemory && store.LoadProfiles().Single().SearchEndpoint == search.Endpoint, "profile automatic flags and search endpoint persist");
        var ui = new AgentWorkspaceViewModel(store);
        Check(ui.EnableAutoMemory && ui.EnableAutoSkills && ui.EnableWebSearch && ui.SearchEndpoint == search.Endpoint, "UI loads automatic capability bindings");
        ui.SaveProfileCommand.Execute(null);
        Check(store.LoadProfiles().Single().Binding == profile.Binding, "UI capture preserves all new config/binding");
        var snapshot = new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding, ProfileSnapshot = profile, Session = JsonSerializer.SerializeToElement(new { }) };
        store.Save(snapshot);
        ui.EnableAutoMemory = false; ui.EnableAutoSkills = false; ui.EnableWebSearch = false; ui.SearchEndpoint = "";
        ui.SaveProfileCommand.Execute(null);
        Check(!ui.EnableAutoMemory && !ui.EnableAutoSkills && !ui.EnableWebSearch && ui.SearchEndpoint == "" &&
            store.Load(snapshot.Id).ProfileSnapshot?.Binding == profile.Binding,
            "history retains its profile snapshot without restoring it into the active configuration");
        var legacyStore = new PlatformStore(Path.Combine(root, "legacy-automatic-cache"));
        File.WriteAllText(Path.Combine(legacyStore.Root, "profiles.json"), JsonSerializer.Serialize(new { Version = 1, Profiles = new[] { legacy.ProfileSnapshot }, SelectedProfileId = legacy.ProfileId }));
        legacyStore.Save(legacy);
        Check(!legacyStore.LoadProfiles().Single().AutoMemory && !legacyStore.LoadProfiles().Single().AutoSkills &&
            legacyStore.Load(legacy.Id).ProfileSnapshot!.Binding == legacyHash, "actual v1 profiles and bound snapshot cache remain restorable");
        PlatformStore.ValidateBinding(legacyStore.Load(legacy.Id), legacyStore.LoadProfiles().Single());
        legacyStore.SaveProfiles(legacyStore.LoadProfiles());
        using (var version = JsonDocument.Parse(File.ReadAllText(Path.Combine(legacyStore.Root, "profiles.json"))))
            Check(version.RootElement.GetProperty("Version").GetInt32() == 2, "profile cache migrated explicitly to v2");
        StoredConversation searchPending;
        var searchProfile = profile with { AutoMemory = false, AutoSkills = false };
        using (var online = new HarnessWorkspace(Settings(profile with { AutoMemory = false, AutoSkills = false }), output.Enqueue))
        {
            await online.InitializeAsync(ct);
            async Task<HarnessApproval> Ask(string query)
            {
                model.EnqueueTool("search_web", new { query });
                return (await online.SendAsync("search", cancellationToken: ct)).Approvals.Single();
            }
            var approval = await Ask("汉字 & a+b?");
            Check(search.Requests.Count == 0 && approval.Details.Contains("汉字"), "search no network before approval and exact query disclosure");
            model.EnqueueText("declined search");
            await online.DecideAsync(approval.Id, false, cancellationToken: ct);
            Check(search.Requests.Count == 0, "rejected search causes zero requests");
            approval = await Ask("汉字 & a+b?");
            model.EnqueueText("search complete");
            await online.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(search.Requests.Count == 1 && search.Requests.Single().Contains("format=json") &&
                search.Requests.Single().Contains(Uri.EscapeDataString("汉字 & a+b?")), "approved SearXNG query encoded and JSON format specified");
            Check(!search.Requests.Single().Contains("local-test-key") && !search.Requests.Single().Contains("Authorization"), "search client sends no model credential");
            Check(model.Requests.Last().Contains("mock-title") && model.Requests.Last().Contains("mock-snippet") &&
                model.Requests.Last().Contains("citation-only") && model.Requests.Last().Contains("mock-engine") &&
                search.Requests.Count == 1, "search returns title URL snippet source without accessing result URL");
            var invoke = typeof(HarnessWorkspace).GetMethod("SearchWebAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var blocked = await (Task<string>)invoke.Invoke(online, ["different query", ct])!;
            Check(blocked.Contains("blocked") && search.Requests.Count == 1, "direct/altered search arguments cannot bypass unused matching grant");
            foreach (var mode in new[] { "failure", "oversize", "chunked-oversize", "redirect", "badjson" })
            {
                search.Mode = mode;
                approval = await Ask(mode);
                var before = search.Requests.Count;
                model.EnqueueText("explicit search failure");
                await online.DecideAsync(approval.Id, true, cancellationToken: ct);
                Check(search.Requests.Count == before + 1 && model.Requests.Last().Contains("Search failed explicitly"),
                    "search " + mode + " fails explicitly with no redirect/result fetch");
            }
            approval = await Ask("unresolved search");
            searchPending = new StoredConversation { ProfileId = searchProfile.Id, Binding = searchProfile.Binding, ProfileSnapshot = searchProfile,
                Session = await online.SerializeAsync(ct), UnsafeToResume = true };
            model.EnqueueText("unresolved declined");
            await online.DecideAsync(approval.Id, false, cancellationToken: ct);
            search.Mode = "delay";
            approval = await Ask("cancel");
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cancel.CancelAfter(TimeSpan.FromMilliseconds(250));
            await ExpectCancellationAsync(online.DecideAsync(approval.Id, true, cancellationToken: cancel.Token), "cancel while waiting for gate");
        }
        using (var restored = new HarnessWorkspace(Settings(searchProfile), output.Enqueue))
        {
            await restored.RestoreAsync(searchPending, searchProfile, ct);
            var before = search.Requests.Count;
            model.EnqueueText("search approval invalidated");
            await restored.SendAsync("resume safely", cancellationToken: ct);
            Check(search.Requests.Count == before && restored.PendingApprovals.Count == 0, "restored search approvals cannot replay network disclosure");
        }
        foreach (var query in new[] { "local-test-key", "api_key=sk-1234567890abcdef", "{\"api_key\":\"quoted-secret-12345\"}", "Bearer bearer-secret-12345", new string('q', 513) })
        {
            using var invalid = new HarnessWorkspace(Settings(profile with { AutoMemory = false, AutoSkills = false }), output.Enqueue);
            await invalid.InitializeAsync(ct);
            model.EnqueueTool("search_web", new { query });
            var before = search.Requests.Count;
            try { await invalid.SendAsync("unsafe search", cancellationToken: ct); throw new Exception("FAIL: unsafe search approval accepted"); }
            catch (ArgumentException) { }
            Check(search.Requests.Count == before, "secret/oversize query refused without search request");
        }
        Console.WriteLine("PASS: automatic memory refresh/persistence/budgets, native skill discovery/approval/resources, SearXNG mock HTTP consent/encoding/failures/cancel and legacy profile binding");
    }

    static void CheckHistoryIsolation(string root)
    {
        var store = new PlatformStore(Path.Combine(root, "history-isolation-cache"));
        var profile = new AgentProfile { Name = "Healthy Agent", WorkspaceRoot = root };
        var second = profile with { Id = Guid.NewGuid(), Name = "Empty Agent" };
        store.SaveProfiles([profile, second], profile.Id);
        var healthy = new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding,
            ProfileSnapshot = profile, Name = "Healthy history", Transcript = "searchable",
            Session = JsonSerializer.SerializeToElement(new { }) };
        store.Save(healthy);
        var brokenId = Guid.NewGuid();
        var brokenPath = Path.Combine(store.Root, brokenId.ToString("N") + ".session.json");
        foreach (var content in new[]
        {
            "{broken-json",
            JsonSerializer.Serialize(healthy with { Id = brokenId, Version = 99 }),
            JsonSerializer.Serialize(healthy with { Id = brokenId, HarnessVersion = "incompatible" }),
            JsonSerializer.Serialize(healthy with { Id = brokenId, Name = null! })
        })
        {
            File.WriteAllText(brokenPath, content);
            Check(store.Search("SEARCHABLE", profile.Id).Single().Id == healthy.Id,
                "one corrupt/incompatible history does not hide healthy record");
            Check(store.SearchIssues.Count == 1 && store.SearchIssues[0].FileName == Path.GetFileName(brokenPath) &&
                !string.IsNullOrWhiteSpace(store.SearchIssues[0].Message), "per-file history diagnostic visible");
            var ui = new AgentWorkspaceViewModel(store);
            Check(ui.Sessions.Single().Id == healthy.Id && ui.HistoryWarning.Contains(Path.GetFileName(brokenPath)) &&
                ui.Status.Contains("警告"), "startup displays history warning without losing healthy sessions");
            ui.SelectedProfile = ui.Profiles.Single(p => p.Id == second.Id);
            ui.DeleteProfileCommand.Execute(null);
            Check(ui.Profiles.Count == 2 && store.LoadProfiles().Count == 2 &&
                ui.Status.Contains("无法确认归属") && ui.Status.Contains(Path.GetFileName(brokenPath)),
                "unattributable history blocks agent deletion regardless of the selected profile");
        }
        File.Delete(brokenPath);
        var invalidName = Path.Combine(store.Root, "invalid.session.json");
        File.WriteAllText(invalidName, "{}");
        Check(store.Search("").Count == 1 && store.SearchIssues.Single().FileName == Path.GetFileName(invalidName),
            "invalid session filename isolated with diagnostic");
        File.Delete(invalidName);
        var recovered = new AgentWorkspaceViewModel(store);
        Check(recovered.HistoryWarning == "" && store.SearchIssues.Count == 0, "repaired histories clear warnings");
        recovered.SelectedProfile = recovered.Profiles.Single(p => p.Id == second.Id);
        recovered.DeleteProfileCommand.Execute(null);
        Check(store.LoadProfiles().Single().Id == profile.Id, "healthy empty agent remains deletable");

        var oldProfile = profile with { Model = "old-model" };
        var oldSession = healthy with { Id = Guid.NewGuid(), Binding = oldProfile.Binding, ProfileSnapshot = oldProfile };
        store.Save(oldSession);
        var storedOldSession = store.Load(oldSession.Id);
        try
        {
            PlatformStore.ValidateBinding(storedOldSession, profile);
            throw new InvalidOperationException("FAIL: changed profile must not resume a bound session");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("绑定的 Agent 配置不同")) { }
        Check(storedOldSession.ProfileSnapshot?.Binding == oldProfile.Binding,
            "historical profile snapshots remain metadata while changed bindings stay blocked");

        var profilesPath = Path.Combine(store.Root, "profiles.json");
        File.Delete(profilesPath);
        Directory.CreateDirectory(profilesPath);
        recovered.NewProfileCommand.Execute(null);
        Check(recovered.Profiles.Count == 1 && recovered.SelectedProfile?.Id == profile.Id &&
            recovered.Status.Contains("新增配置失败"), "failed new-profile save never adds ghost profile");
        Directory.Delete(profilesPath);
        File.WriteAllText(profilesPath, "{broken-profiles");
        var unavailable = new AgentWorkspaceViewModel(store);
        Check(!unavailable.CanEditSettings && !unavailable.CanConnect && unavailable.Status.Contains("加载失败"),
            "failed cache initialization disables persistent operations");
        foreach (var command in new CommunityToolkit.Mvvm.Input.IRelayCommand[]
        {
            unavailable.SaveProfileCommand, unavailable.NewProfileCommand, unavailable.DeleteProfileCommand,
            unavailable.DeleteSessionCommand, unavailable.RenameSessionCommand,
            unavailable.RefreshSessionsCommand
        })
        {
            unavailable.Status = "";
            command.Execute(null);
            Check(unavailable.Status.Contains("缓存不可用") && unavailable.Profiles.Count == 0,
                "unavailable cache commands expose consistent error without ghost profiles");
        }
        Check(File.ReadAllText(profilesPath) == "{broken-profiles", "failed initialization never overwrites damaged cache");
    }

    static async Task CheckCompactionPersistenceAsync(FakeModel model, ConcurrentQueue<string> output, string root, string keyName, CancellationToken ct)
    {
        CheckRequestSourceAttributionSerialization();
        var profile = new AgentProfile { Name = "Compaction checks", Endpoint = model.Endpoint, Model = "fake-model",
            KeyEnvironmentVariable = keyName, WorkspaceRoot = root, ScriptsDirectory = root,
            Compaction = true, ContextTokens = 8192, OutputTokens = 1024 };
        var settings = new HarnessSettings(profile.Endpoint, profile.Model, profile.KeyEnvironmentVariable, profile.ScriptsDirectory, profile);
        using var workspace = new HarnessWorkspace(settings, output.Enqueue);
        await workspace.InitializeAsync(ct);
        model.EnqueueText("first compaction answer");
        await workspace.SendAsync("first compaction question", cancellationToken: ct);
        var session = new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding,
            ProfileSnapshot = profile, Session = await workspace.SerializeAsync(ct) };

        using var restored = new HarnessWorkspace(settings, output.Enqueue);
        await restored.RestoreAsync(session, profile, ct);
        model.EnqueueText("restored compaction answer");
        var result = await restored.SendAsync("continue after compaction restore", cancellationToken: ct);
        var state = await restored.SerializeAsync(ct);
        Check(result.Text.Contains("restored compaction answer") &&
            state.GetProperty("stateBag").GetProperty("AgentModeProvider").GetProperty("currentMode").GetString() == "execute",
            "compaction session serializes, restores, and continues in execute mode");
        Console.WriteLine("Compaction session persistence checks passed.");
    }

    static void CheckRequestSourceAttributionSerialization()
    {
        var factory = typeof(HarnessWorkspace).GetMethod("CreateSessionSerializerOptions", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Session serializer options factory not found.");
        var options = factory.Invoke(null, null) as JsonSerializerOptions
            ?? throw new InvalidOperationException("Session serializer options were not created.");
        var message = new ChatMessage(ChatRole.Assistant, "answer")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["source"] = new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.ChatHistory, "history")
            }
        };

        var json = JsonSerializer.SerializeToElement(message, options);
        var attribution = json.GetProperty("additionalProperties").GetProperty("source");
        Check(attribution.GetProperty("sourceId").GetString() == "history" &&
            attribution.TryGetProperty("sourceType", out var sourceType) && sourceType.ValueKind != JsonValueKind.Null,
            "request source attribution in message additional properties serializes");
        var restored = JsonSerializer.Deserialize<ChatMessage>(json, options);
        Check(restored?.AdditionalProperties?.ContainsKey("source") == true,
            "request source attribution message additional properties deserializes");
    }

    static async Task CheckSessionSerializationWithoutReflectionAsync(FakeModel model, string root, string keyName, CancellationToken ct)
    {
        using var workspace = new HarnessWorkspace(new HarnessSettings(model.Endpoint, "fake-model", keyName, root), _ => { });
        await workspace.InitializeAsync(ct);
        var session = typeof(HarnessWorkspace).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(workspace) as AgentSession
            ?? throw new InvalidOperationException("Harness session was not initialized.");
        var agent = typeof(HarnessWorkspace).GetField("_agent", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(workspace) as AIAgent
            ?? throw new InvalidOperationException("Harness agent was not initialized.");
        var history = agent.GetService<InMemoryChatHistoryProvider>()
            ?? throw new InvalidOperationException("In-memory chat history provider was not registered.");
        history.GetMessages(session).Add(new ChatMessage(ChatRole.Assistant, "attributed response")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [AgentRequestMessageSourceAttribution.AdditionalPropertiesKey] = new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.ChatHistory, "history")
            }
        });
        var state = await workspace.SerializeAsync(ct);
        Check(state.GetProperty("stateBag").GetProperty("AgentModeProvider")
            .GetProperty("currentMode").GetString() == "execute", "session serialization works with reflection disabled");
        Check(state.GetProperty("stateBag").GetProperty("InMemoryChatHistoryProvider").GetProperty("messages")[0]
            .GetProperty("additionalProperties").GetProperty(AgentRequestMessageSourceAttribution.AdditionalPropertiesKey).GetProperty("sourceId").GetString() == "history",
            "session serializes attributed chat messages with production options");
    }

    static async Task CheckPlatformAsync(FakeModel model, ConcurrentQueue<string> output, string root, string keyName, CancellationToken ct)
    {
        var store = new PlatformStore(Path.Combine(root, "platform-cache"));
        var profile = new AgentProfile { Name = "General Agent", Endpoint = model.Endpoint, Model = "fake-model",
            KeyEnvironmentVariable = keyName, WorkspaceRoot = root, ScriptsDirectory = root, Files = true, Terminal = true,
            FileMemory = true, Skills = true,
            Compaction = true, ContextTokens = 8192, OutputTokens = 1024 };
    Directory.CreateDirectory(Path.Combine(root, profile.MemoryDirectory));
    Directory.CreateDirectory(Path.Combine(root, profile.SkillsDirectory));
    File.WriteAllText(Path.Combine(root, profile.SkillsDirectory, "SKILL.md"), "local-skill-approved-payload");
        store.SaveProfiles([profile, profile with { Id = Guid.NewGuid(), Name = "Second Agent" }]);
        Check(store.LoadProfiles().Count == 2, "multiple persistent profiles");
        Check(!File.ReadAllText(Path.Combine(store.Root, "profiles.json")).Contains("local-test-key"), "profiles contain no API secret");
        using (var smallKey = JsonDocument.Parse("{\"stateBag\":{\"a\":\"a\"}}"))
        {
            var scrubbed = PlatformStore.RedactState(smallKey.RootElement, "a");
            Check(scrubbed.GetProperty("stateBag").GetProperty("a").GetString() == "[REDACTED API KEY]", "secret redaction preserves structural JSON keys");
        }
        try { store.SaveProfiles([profile with { Instructions = "local-test-key" }]); throw new Exception("FAIL: secret persisted in instructions"); }
        catch (InvalidOperationException) { }
        try { store.SaveProfiles([profile with { KeyEnvironmentVariable = "sk-secret-value" }]); throw new Exception("FAIL: key accepted instead of variable"); }
        catch (ArgumentException) { }
        HarnessSettings Settings(AgentProfile p) => new(p.Endpoint, p.Model, p.KeyEnvironmentVariable, p.ScriptsDirectory, p);
        StoredConversation stored;
        using (var first = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await first.InitializeAsync(ct);
            var initialState = await first.SerializeAsync(ct);
            Check(initialState.GetProperty("stateBag").GetProperty("AgentModeProvider")
                .GetProperty("currentMode").GetString() == "execute", "new Harness sessions default to execute mode");
            await first.SetModeAsync("execute", ct);
            model.EnqueueText("persistent answer");
            await first.SendAsync("remember platypus-42", cancellationToken: ct);
            stored = new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding, Name = "Context",
                Transcript = "remember platypus-42", Log = "local-test-key", Session = await first.SerializeAsync(ct) };
        Check(stored.Session.GetProperty("stateBag").GetProperty("AgentModeProvider").GetProperty("currentMode").GetString() == "execute", "real external Harness mode switch persisted");
            store.Save(stored);
        }
        Check(store.Load(stored.Id).Log == "[REDACTED API KEY]", "credential redacted from local task cache");
        Check(store.Search("PLATYPUS", profile.Id).Count == 1, "persistent case-insensitive transcript search");
        store.Rename(stored.Id, "Renamed");
        Check(store.Search("Renamed", profile.Id).Count == 1, "rename persisted");
        stored = store.Load(stored.Id);
        foreach (var mismatch in new[] { profile with { Model = "another-model" }, profile with { Id = Guid.NewGuid() }, profile with { Files = false } })
        {
            try { PlatformStore.ValidateBinding(stored, mismatch); throw new Exception("FAIL: binding mismatch accepted"); }
            catch (InvalidOperationException) { }
        }
        var marker = Path.Combine(root, "platform-write.txt");
        HarnessApproval oldApproval;
        using (var resumed = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await resumed.RestoreAsync(stored, profile, ct);
            model.EnqueueText("context resumed");
            await resumed.SendAsync("what did I say?", cancellationToken: ct);
            Check(model.Requests.Last().Contains("remember platypus-42") && model.Requests.Last().Contains("persistent answer"), "real deserialized AgentSession restores context");
            model.EnqueueTool("write_workspace_file", new { path = "platform-write.txt", content = "not-auto-run" });
            var result = await resumed.SendAsync("write a file", cancellationToken: ct);
            oldApproval = result.Approvals.Single();
            Check(!File.Exists(marker), "generic file write individually approved");
            stored = stored with { Session = await resumed.SerializeAsync(ct), UnsafeToResume = true };
            store.Save(stored);
        }
        using (var restoredApproval = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await restoredApproval.RestoreAsync(store.Load(stored.Id), profile, ct);
            Check(restoredApproval.PendingApprovals.Count == 0 && !File.Exists(marker), "pending approval never reconstructed as a grant");
            try { await restoredApproval.DecideAsync(oldApproval.Id, true, cancellationToken: ct); throw new Exception("FAIL: replayed stale approval"); }
            catch (InvalidOperationException) { }
            model.EnqueueText("old authorization rejected");
            await restoredApproval.SendAsync("continue without executing old requests", cancellationToken: ct);
            Check(!File.Exists(marker), "resumed approval binding rejects old request without execution");
            Check(model.Requests.Last().Contains("reject", StringComparison.OrdinalIgnoreCase) ||
                model.Requests.Last().Contains("declin", StringComparison.OrdinalIgnoreCase), "framework records old approval rejection");
            model.EnqueueTool("write_workspace_file", new { path = "platform-write.txt", content = "approved-content" });
            var approval = (await restoredApproval.SendAsync("write", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("done");
            await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(File.ReadAllText(marker) == "approved-content", "approved file write executes once");
            await File.WriteAllTextAsync(marker, "local-read-only-payload", ct);
            try { await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct); throw new Exception("FAIL: approval replay"); }
            catch (InvalidOperationException) { }
            model.EnqueueTool("read_workspace_file", new { path = "platform-write.txt" });
            approval = (await restoredApproval.SendAsync("read", cancellationToken: ct)).Approvals.Single();
            Check(approval.Details.Contains("传输"), "local file upload consent explicit");
            model.EnqueueText("declined file");
            await restoredApproval.DecideAsync(approval.Id, false, cancellationToken: ct);
            Check(!model.Requests.Last().Contains("local-read-only-payload"), "read rejection does not transmit local contents");
            model.EnqueueTool("read_workspace_file", new { path = "platform-write.txt" });
            approval = (await restoredApproval.SendAsync("read now", cancellationToken: ct)).Approvals.Single();
            model.EnqueueText("read done");
            await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(model.Requests.Last().Contains("local-read-only-payload"), "individually approved read transmits file");
            model.EnqueueTool("write_workspace_file", new { path = "platform-write.txt", content = "should-not-write" });
            approval = (await restoredApproval.SendAsync("write changed", cancellationToken: ct)).Approvals.Single();
            await File.WriteAllTextAsync(marker, "external-change", ct);
            model.EnqueueText("blocked changed file");
            await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(File.ReadAllText(marker) == "external-change", "changed file invalidates approval hash");
            using (var schemaRequest = JsonDocument.Parse(model.Requests.Last()))
            {
                var nativeRead = schemaRequest.RootElement.GetProperty("tools").EnumerateArray().Single(t =>
                    t.GetProperty("function").GetProperty("name").GetString() == "file_access_read").GetProperty("function").GetProperty("parameters");
                var nativeArgs = nativeRead.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, _ => "platform-write.txt");
                await File.WriteAllTextAsync(marker, "native-harness-read-payload", ct);
                model.EnqueueTool("file_access_read", nativeArgs);
                approval = (await restoredApproval.SendAsync("native harness file read", cancellationToken: ct)).Approvals.Single();
                Check(!model.Requests.Last().Contains("native-harness-read-payload"), "native file read waits for transmission approval");
                model.EnqueueText("native read done");
                await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
                Check(model.Requests.Last().Contains("native-harness-read-payload"), "real Harness FileAccessProvider approved read");
            }
            model.EnqueueTool("write_memory_file", new { path = "remember.txt", content = "memory-persisted-value" });
            approval = (await restoredApproval.SendAsync("save explicit memory", cancellationToken: ct)).Approvals.Single();
            Check(!File.Exists(Path.Combine(root, profile.MemoryDirectory, "remember.txt")), "memory write requires approval");
            model.EnqueueText("memory saved");
            await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(File.ReadAllText(Path.Combine(root, profile.MemoryDirectory, "remember.txt")) == "memory-persisted-value", "approved workspace memory persists");
            model.EnqueueTool("read_skill_file", new { path = "SKILL.md" });
            approval = (await restoredApproval.SendAsync("load specific skill", cancellationToken: ct)).Approvals.Single();
            Check(!model.Requests.Last().Contains("local-skill-approved-payload"), "skills never scanned/transmitted automatically");
            model.EnqueueText("skill read");
            await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
            Check(model.Requests.Last().Contains("local-skill-approved-payload"), "specific skill transmission approved");
            if (OperatingSystem.IsWindows())
            {
                model.EnqueueTool("run_terminal", new { command = "Write-Output 'terminal-local-only'" });
                approval = (await restoredApproval.SendAsync("terminal", cancellationToken: ct)).Approvals.Single();
                model.EnqueueText("terminal done");
                await restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
                Check(output.Any(s => s.Contains("terminal-local-only")) && model.Requests.Any(s =>
                {
                    using var request = JsonDocument.Parse(s);
                    return request.RootElement.GetProperty("messages").EnumerateArray().Any(m =>
                        m.GetProperty("role").GetString() == "tool" && m.GetRawText().Contains("terminal-local-only"));
                }),
                    "approved terminal output is returned in tool result");
                var started = Path.Combine(root, "terminal-started");
                model.EnqueueTool("run_terminal", new { command = "Set-Content (Join-Path (Get-Location) 'terminal-started') 'ok'; Start-Sleep -Seconds 30; Set-Content (Join-Path (Get-Location) 'terminal-finished') 'bad'" });
                approval = (await restoredApproval.SendAsync("cancel terminal", cancellationToken: ct)).Approvals.Single();
                var running = restoredApproval.DecideAsync(approval.Id, true, cancellationToken: ct);
                try
                {
                    await WaitForFileAsync(started, ct);
                    restoredApproval.Cancel();
                    await ExpectCancellationAsync(running, "cancel generic terminal");
                    Check(!File.Exists(Path.Combine(root, "terminal-finished")), "cancelled terminal cannot finish");
                }
                finally { restoredApproval.Cancel(); try { await running; } catch (OperationCanceledException) { } }
            }
        }
        foreach (var path in new[] { ".." + Path.DirectorySeparatorChar + "escape.txt", Path.Combine(root, "absolute.txt") })
        {
            try { WorkspaceBoundary.Resolve(root, path); throw new Exception("FAIL: workspace escape accepted"); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException) { }
        }
        var link = Path.Combine(root, "junction-check");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                {
                    Arguments = $"/c mklink /J \"{link}\" \"{store.Root}\"", UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                })!;
                await process.WaitForExitAsync(ct);
                Check(process.ExitCode == 0, "test junction creation");
            }
            else Directory.CreateSymbolicLink(link, store.Root);
            try { WorkspaceBoundary.Resolve(root, Path.Combine("junction-check", "profiles.json")); throw new Exception("FAIL: symlink escape accepted"); }
            catch (UnauthorizedAccessException) { }
            #pragma warning disable MAAI001
            var nativeStore = new Microsoft.Agents.AI.FileSystemAgentFileStore(root);
            #pragma warning restore MAAI001
            try { await nativeStore.ReadAsync(Path.Combine("junction-check", "profiles.json"), ct); throw new Exception("FAIL: native file store symlink escape"); }
            catch (ArgumentException) { }
            try { await nativeStore.ReadAsync(".." + Path.DirectorySeparatorChar + "escape.txt", ct); throw new Exception("FAIL: native file store traversal"); }
            catch (ArgumentException) { }
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
        Check(!TaskCatalog.Get("linux-package").Supports(NativeHost.Windows, "linux-x64"), "Linux packaging rejects Windows hosts");
        Check(TaskCatalog.Get("linux-package").Supports(NativeHost.Linux, "linux-arm64"), "Linux arm64 catalog");
        Check(!TaskCatalog.Get("linux-package").Supports(NativeHost.MacOS, "linux-x64"), "Linux wrapper rejects macOS");
        Check(TaskCatalog.Get("macos-package").Supports(NativeHost.MacOS, "osx-arm64") &&
            !TaskCatalog.Get("macos-package").Supports(NativeHost.Windows, "osx-arm64"), "macOS native host restriction");
        Check(!TaskCatalog.Get("marketserver").Supports(NativeHost.Windows, "linux-x64"), "MarketServer cross-AOT not falsely supported");
        if (OperatingSystem.IsWindows())
        {
            try { TaskCatalog.Get("macos-package").Prepare(new PluginRequest("macos-package", root, "osx-arm64", "1.0.0", "1.0.1", "")); throw new Exception("FAIL: macOS work started on Windows"); }
            catch (PlatformNotSupportedException) { }
            var scripts = Path.Combine(root, "win-package"); Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "publish-upgrade-package-win-x64.ps1"), "exit 0");
            File.WriteAllText(Path.Combine(scripts, "publish-config.json"), JsonSerializer.Serialize(new { RootDirectory = root }));
            var injection = "https://host/'; Write-Output 'injected";
            var command = TaskCatalog.Get("windows-package").Prepare(new PluginRequest("windows-package", root, "win-x64", "1.0.0", "1.0.1", injection));
            Check(command.Arguments.Last() == injection && !command.Arguments.Contains("-Command"), "plugin parameters use argument boundaries, not shell interpolation");
            var aotIndex = command.Arguments.ToList().IndexOf("-PublishAot");
            Check(aotIndex >= 0 && command.Arguments[aotIndex + 1] == "true", "manual packaging defaults to Native AOT");
            var nonAotCommand = TaskCatalog.Get("windows-package").Prepare(new PluginRequest("windows-package", root, "win-x64", "1.0.0", "1.0.1", "", PublishAot: false));
            aotIndex = nonAotCommand.Arguments.ToList().IndexOf("-PublishAot");
            Check(aotIndex >= 0 && nonAotCommand.Arguments[aotIndex + 1] == "false", "manual packaging forwards the non-AOT selection");
            try { TaskCatalog.Get("linux-package").Prepare(new PluginRequest("linux-package", root, "linux-x64", "1.0.0", "1.0.1", "")); throw new Exception("FAIL: Linux packaging started on Windows"); }
            catch (PlatformNotSupportedException) { }
        }
        store.Delete(stored.Id);
        Check(store.Search("", profile.Id).Count == 0, "session deletion persisted");
        Check(!Directory.EnumerateFiles(store.Root, "*.writing").Any(), "atomic cache staging files cleaned");
        var unsupported = stored with { Id = Guid.NewGuid(), Version = 99 };
        store.Save(unsupported);
        try { store.Load(unsupported.Id); throw new Exception("FAIL: unknown cache version accepted"); }
        catch (InvalidDataException) { }
        store.Delete(unsupported.Id);
        var uiStore = new PlatformStore(Path.Combine(root, "ui-cache"));
        uiStore.SaveProfiles([profile], profile.Id);
        var ui = new AgentWorkspaceViewModel(uiStore);
        Check(ui.Endpoint == profile.Endpoint && ui.SelectedProfile?.Id == profile.Id, "startup loads last configured profile without re-entry");
        await ui.InitializeOnStartupAsync();
        Check(ui.CanSend && ui.CanType && ui.CanEditSettings && ui.IsConnected && ui.Sessions.Count == 0,
            "startup initializes an editable conversation without creating an empty history item");
        Check(ui.ModeText == "自动执行", "startup uses execute mode (shown as " + ui.ModeText + ")");
        ui.Input = "first UI question";
        model.EnqueueText("first UI answer");
        await ui.SendCommand.ExecuteAsync(null);
        Check(ui.IsConnected && ui.Sessions.Count == 1 && ui.Sessions.Single().Name == "first UI question",
            "first UI question creates the titled persistent conversation");
        await ui.DisconnectCommand.ExecuteAsync(null);
        Check(!ui.IsConnected && ui.Sessions.Count == 1, "UI disconnect retains history");
        ui.SelectedSession = ui.Sessions.Single();
        var uiSessionId = ui.SelectedSession.Id;
        ui.Model = "changed-model";
        ui.SaveProfileCommand.Execute(null);
        ui.SelectedSession = ui.Sessions.Single();
        await ui.RestoreSessionCommand.ExecuteAsync(null);
        Check(!ui.IsConnected && ui.Status.Contains("绑定"), "UI rejects wrong-model history restore before network");
        ui.Model = profile.Model;
        ui.SaveProfileCommand.Execute(null);
        ui.SelectedSession = ui.Sessions.Single();
        await ui.RestoreSessionCommand.ExecuteAsync(null);
        Check(ui.IsConnected && ui.Sessions.Single().Id == uiSessionId, "UI selection restores same persisted session");
        Check(await ui.CloseAsync(), "window close cancels and safely persists session");
        StoredConversation partialApprovalSession;
        using (var batch = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await batch.InitializeAsync(ct);
            model.EnqueueTools(("write_workspace_file", new { path = "batch-first.txt", content = "one" }),
                ("write_workspace_file", new { path = "batch-second.txt", content = "two" }));
            var approvals = (await batch.SendAsync("two separately approved writes", cancellationToken: ct)).Approvals;
            Check(approvals.Count == 2, "multiple independent approval requests");
            var undecided = await batch.DecideAsync(approvals[0].Id, true, cancellationToken: ct);
            Check(undecided.Approvals.Count == 1 && !File.Exists(Path.Combine(root, "batch-first.txt")), "partial approval does not execute batch");
            partialApprovalSession = new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding,
                Session = await batch.SerializeAsync(ct), UnsafeToResume = true };
            store.Save(partialApprovalSession);
        }
        using (var restoredBatch = new HarnessWorkspace(Settings(profile), output.Enqueue))
        {
            await restoredBatch.RestoreAsync(store.Load(partialApprovalSession.Id), profile, ct);
            model.EnqueueText("old batch denied");
            await restoredBatch.SendAsync("continue chat only", cancellationToken: ct);
            Check(!File.Exists(Path.Combine(root, "batch-first.txt")) && !File.Exists(Path.Combine(root, "batch-second.txt")),
                "restart never replays even previously approved partial batch");
        }
        store.Delete(partialApprovalSession.Id);
        var backgroundProfile = profile with { Id = Guid.NewGuid(), BackgroundResearch = true, Compaction = false };
        await using (var background = new HarnessWorkspace(Settings(backgroundProfile), output.Enqueue))
        {
            await background.InitializeAsync(ct);
            model.EnqueueText("background configured");
            await background.SendAsync("show available delegation", cancellationToken: ct);
            using var request = JsonDocument.Parse(model.Requests.Last());
            var schema = request.RootElement.GetProperty("tools").EnumerateArray().Single(t =>
                t.GetProperty("function").GetProperty("name").GetString() == "background_agents_start_task").GetProperty("function").GetProperty("parameters");
            var args = new Dictionary<string, string>();
            foreach (var prop in schema.GetProperty("properties").EnumerateObject())
                args[prop.Name] = prop.Name.Contains("agent", StringComparison.OrdinalIgnoreCase) ? "Researcher" : "Reason about two plus two without tools.";
            model.EnqueueTool("background_agents_start_task", args);
            model.EnqueueText("background reasoning finished");
            model.EnqueueText("background reasoning finished");
            await background.SendAsync("delegate reasoning to Researcher", cancellationToken: ct);
            while (background.HasBackgroundWork) await Task.Delay(20, ct);
            var backgroundState = await background.SerializeAsync(ct);
            Check(backgroundState.GetRawText().Contains("background reasoning finished"), "actual named background agent completes");
            Check(model.Requests.Any(r =>
            {
                using var data = JsonDocument.Parse(r);
                return data.RootElement.GetProperty("messages").EnumerateArray().Any(m =>
                    m.GetProperty("role").GetString() == "system" && m.GetRawText().Contains("reasoning-only")) &&
                    (!data.RootElement.TryGetProperty("tools", out var tools) || tools.GetArrayLength() == 0);
            }), "background agent has no external side-effect tools");
            await background.StopBackgroundAsync();
            Check(!background.HasBackgroundWork, "background runtime released before shutdown");
        }
        Console.WriteLine("PASS: profiles, real context restore, CRUD/search, binding, approval replay/denial, files, terminal, boundary and host catalog");
    }

static async Task CheckHarnessAsync(HarnessWorkspace workspace, FakeModel model, ConcurrentQueue<string> output, string root, CancellationToken ct)
{
    var marker = Path.Combine(root, "executed.txt");
    async Task<HarnessApproval> RequestApprovalAsync()
    {
        model.EnqueueTool();
        var result = await workspace.SendAsync("run upgrade", cancellationToken: ct);
        Check(result.Approvals.Count == 1 && workspace.State == HarnessState.AwaitingApproval, "approval required");
        Check(result.Approvals[0].PublishRootDirectory == root, "approval displays configured root");
        Check(!File.Exists(marker), "no execution before approval");
        return result.Approvals[0];
    }

    var approval = await RequestApprovalAsync();
    model.EnqueueText("rejected");
    await workspace.DecideAsync(approval.Id, false, cancellationToken: ct);
    Check(!File.Exists(marker) && workspace.State == HarnessState.Ready, "rejection does not execute");

    approval = await RequestApprovalAsync();
    model.EnqueueText("done");
    await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
    Check(File.Exists(marker), "approval executes script");
    Check(output.Any(s => s.Contains("local-only-secret")), "script output captured locally");
    Check(!model.Requests.Any(s => s.Contains("local-only-secret")), "script output not sent to model");
    File.Delete(marker);

    approval = await RequestApprovalAsync();
    await File.WriteAllTextAsync(Path.Combine(root, "publish-config.json"), JsonSerializer.Serialize(new { RootDirectory = root, changed = true }), ct);
    model.EnqueueText("blocked");
    await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
    Check(!File.Exists(marker) && output.Any(s => s.Contains("changed after approval")), "changed configuration blocks execution");
    Check(model.Requests.Last().Contains("Execution failed"), "blocked execution reports failure to model");

    approval = await RequestApprovalAsync();
    await WriteScriptAsync(root, "Set-Content -Path (Join-Path $PSScriptRoot 'executed.txt') -Value 'unexpected'\nexit 0", ct);
    model.EnqueueText("blocked script");
    await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
    Check(!File.Exists(marker), "changed script blocks execution");
    Check(model.Requests.Last().Contains("Execution failed"), "changed script reports failure to model");

    await WriteScriptAsync(root, "Write-Output 'failure-output'\nexit 7", ct);
    approval = await RequestApprovalAsync();
    model.EnqueueText("failed");
    await workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
    Check(output.Any(s => s.Contains("退出代码 7")), "nonzero exit reported locally");
    Check(model.Requests.Last().Contains("Execution failed") && !model.Requests.Any(s => s.Contains("failure-output")), "nonzero exit reports failure without logs");

    approval = await RequestApprovalAsync();
    workspace.Cancel();
    Check(workspace.State == HarnessState.Cancelled && workspace.PendingApprovals.Count == 0 && !File.Exists(marker), "cancel pending approval");
    await workspace.ResetAsync(ct);

    await WriteScriptAsync(root, "Set-Content -Path (Join-Path $PSScriptRoot 'cancel-started') -Value 'started'\nStart-Sleep -Seconds 30\nSet-Content -Path (Join-Path $PSScriptRoot 'cancel-finished') -Value 'finished'", ct);
    approval = await RequestApprovalAsync();
    var running = workspace.DecideAsync(approval.Id, true, cancellationToken: ct);
    try
    {
        await WaitForFileAsync(Path.Combine(root, "cancel-started"), ct);
        workspace.Cancel();
        await ExpectCancellationAsync(running, "cancel running script");
        Check(workspace.State == HarnessState.Cancelled && !File.Exists(Path.Combine(root, "cancel-finished")), "cancel terminates script");
    }
    finally
    {
        workspace.Cancel();
        try { await running; } catch (OperationCanceledException) { }
    }
    model.EnqueueText("continued after stopped task");
    var continued = await workspace.SendAsync("continue after stopping task", cancellationToken: ct);
    Check(continued.Text.Contains("continued after stopped task") && workspace.State == HarnessState.Ready &&
        !File.Exists(Path.Combine(root, "cancel-finished")), "cancelled task session accepts a follow-up message without resuming the cancelled task");
    Check(!model.Requests.Any(s => s.Contains("local-only-secret") || s.Contains("failure-output")), "no script logs sent in later requests");
    Console.WriteLine("PASS: Harness approvals, isolation, failures and cancellation");
}

static Task WriteScriptAsync(string root, string body, CancellationToken ct) =>
    File.WriteAllTextAsync(Path.Combine(root, "upgrade-win-x64.ps1"), "param($CurrentVersion,$NextVersion,$OssHost)\n" + body, ct);

static async Task WaitForFileAsync(string path, CancellationToken ct)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
    deadline.CancelAfter(TimeSpan.FromSeconds(15));
    while (!File.Exists(path)) await Task.Delay(50, deadline.Token);
}

static async Task ExpectCancellationAsync(Task task, string name)
{
    try { await task; }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("FAIL: " + name);
}

static async Task CheckGateAsync(string root, CancellationToken ct)
{
    var firstScript = Path.Combine(root, "gate-first.ps1");
    var secondScript = Path.Combine(root, "gate-second.ps1");
    var cancelledScript = Path.Combine(root, "gate-cancelled.ps1");
    await File.WriteAllTextAsync(firstScript, "param($CurrentVersion,$NextVersion,$OssHost)\nSet-Content (Join-Path $PSScriptRoot 'gate-first-started') 'started'\nfor ($i = 0; $i -lt 400; $i++) { if (Test-Path (Join-Path $PSScriptRoot 'gate-release')) { exit 0 }; Start-Sleep -Milliseconds 50 }\nexit 9", ct);
    await File.WriteAllTextAsync(secondScript, "param($CurrentVersion,$NextVersion,$OssHost)\nSet-Content (Join-Path $PSScriptRoot 'gate-second-started') 'started'\nexit 0", ct);
    await File.WriteAllTextAsync(cancelledScript, "param($CurrentVersion,$NextVersion,$OssHost)\nSet-Content (Join-Path $PSScriptRoot 'gate-cancelled-started') 'started'\nexit 0", ct);
    using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
    using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var progress = new SyncProgress();
    Task<int> Run(string script, CancellationToken token) => new PowerShellTaskRunner().RunAsync(script, "1.0.0", "1.0.1", "", progress, token);
    var first = Run(firstScript, stop.Token);
    Task<int>? second = null;
    Task<int>? cancelled = null;
    try
    {
        await WaitForFileAsync(Path.Combine(root, "gate-first-started"), ct);
        second = Run(secondScript, stop.Token);
        cancelled = Run(cancelledScript, waiting.Token);
        await Task.Delay(750, ct);
        Check(!second.IsCompleted && !File.Exists(Path.Combine(root, "gate-second-started")), "global gate prevents concurrent tasks");
        waiting.Cancel();
        await ExpectCancellationAsync(cancelled.WaitAsync(TimeSpan.FromSeconds(5), ct), "cancel while waiting for gate");
        Check(!File.Exists(Path.Combine(root, "gate-cancelled-started")), "cancelled waiter did not execute");
        await File.WriteAllTextAsync(Path.Combine(root, "gate-release"), "release", ct);
        Check(await first == 0 && await second == 0, "gate releases and next task succeeds");
        Check(File.Exists(Path.Combine(root, "gate-second-started")) && !File.Exists(Path.Combine(root, "gate-cancelled-started")), "only noncancelled waiter executes");
    }
    finally
    {
        stop.Cancel();
        waiting.Cancel();
        foreach (var task in new[] { first, second, cancelled }.Where(t => t is not null))
        {
            try { await task!; } catch (OperationCanceledException) { }
        }
    }
    Console.WriteLine("PASS: global PowerShell gate and waiting cancellation");
}

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
}

sealed class CollectingProgress : IProgress<string>
{
    private readonly ConcurrentQueue<string> _chunks = new();
    public string Text => string.Concat(_chunks);
    public void Report(string value) => _chunks.Enqueue(value);
}

sealed class SyncProgress : IProgress<string>
{
    public void Report(string value) { }
}

sealed class MockSearch : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _server;
    private readonly ConcurrentQueue<string> _requests = new();
    public IReadOnlyCollection<string> Requests => _requests.ToArray();
    public string Endpoint { get; }
    public string Mode { get; set; } = "success";
    public MockSearch()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        Endpoint = $"http://127.0.0.1:{port}/search";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _server = ServeAsync();
    }
    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (_stop.IsCancellationRequested && ex is HttpListenerException or ObjectDisposedException) { break; }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { break; }
            _requests.Enqueue(context.Request.RawUrl + "\n" + context.Request.Headers);
            try
            {
                if (Mode == "delay") await Task.Delay(30000, _stop.Token);
                context.Response.StatusCode = Mode == "failure" ? 503 : Mode == "redirect" ? 302 : 200;
                if (Mode == "redirect") context.Response.RedirectLocation = Endpoint + "/redirect-target";
                context.Response.ContentType = "application/json";
                var body = Mode is "oversize" or "chunked-oversize" ? new string('x', 262145) : Mode == "badjson" ? "{bad-json" :
                    JsonSerializer.Serialize(new { results = new[] { new { title = "mock-title", url = Endpoint + "/citation-only",
                        content = "mock-snippet", engine = "mock-engine" } } });
                var bytes = Encoding.UTF8.GetBytes(body);
                if (Mode == "chunked-oversize") context.Response.SendChunked = true;
                else context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, _stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException or ObjectDisposedException) { }
            finally
            {
                try { context.Response.Close(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Close();
        try { await _server; } finally { _stop.Dispose(); }
    }
}

sealed class FakeModel : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly ConcurrentQueue<string[]> replies = new();
    private readonly ConcurrentQueue<string> requests = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task server;
    public string Endpoint { get; }
    public IReadOnlyCollection<string> Requests => requests.ToArray();

    public FakeModel()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        Endpoint = $"http://127.0.0.1:{port}/v1/";
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        server = ServeAsync();
    }

    public void EnqueueText(params string[] chunks) => replies.Enqueue(chunks.Select((text, index) => Chunk(new { role = index == 0 ? "assistant" : null, content = text }, null)).Append(Chunk(new { }, "stop")).Append("data: [DONE]\n\n").ToArray());

    public void EnqueueTool()
    {
        var delta = new { role = "assistant", tool_calls = new[] { new { index = 0, id = "call_" + Guid.NewGuid().ToString("N"), type = "function", function = new { name = "run_packaging_task", arguments = JsonSerializer.Serialize(new { task = "upgrade", currentVersion = "1.0.0", nextVersion = "1.0.1", ossHost = "" }) } } } };
        replies.Enqueue([Chunk(delta, null), Chunk(new { }, "tool_calls"), "data: [DONE]\n\n"]);
    }
    public void EnqueueTool(string name, object arguments)
    {
        var delta = new { role = "assistant", tool_calls = new[] { new { index = 0, id = "call_" + Guid.NewGuid().ToString("N"),
            type = "function", function = new { name, arguments = JsonSerializer.Serialize(arguments) } } } };
        replies.Enqueue([Chunk(delta, null), Chunk(new { }, "tool_calls"), "data: [DONE]\n\n"]);
    }
    public void EnqueueTools(params (string Name, object Arguments)[] calls)
    {
        var delta = new { role = "assistant", tool_calls = calls.Select((call, index) => new { index,
            id = "call_" + Guid.NewGuid().ToString("N"), type = "function",
            function = new { name = call.Name, arguments = JsonSerializer.Serialize(call.Arguments) } }).ToArray() };
        replies.Enqueue([Chunk(delta, null), Chunk(new { }, "tool_calls"), "data: [DONE]\n\n"]);
    }

    public void AssertHealthy()
    {
        if (server.IsFaulted) throw new InvalidOperationException("Fake model failed", server.Exception);
        if (!replies.IsEmpty) throw new InvalidOperationException("Unused fake model replies");
    }

    private async Task ServeAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (HttpListenerException) when (stop.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested) { break; }
            using var reader = new StreamReader(context.Request.InputStream);
            requests.Enqueue(await reader.ReadToEndAsync());
            if (!replies.TryDequeue(out var reply))
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
                throw new InvalidOperationException("Unexpected model request");
            }
            using var requestData = JsonDocument.Parse(requests.Last());
            if (!requestData.RootElement.TryGetProperty("stream", out var streaming) || !streaming.GetBoolean())
            {
                var text = new StringBuilder();
                foreach (var chunk in reply.Where(s => !s.Contains("[DONE]")))
                {
                    using var parsed = JsonDocument.Parse(chunk["data: ".Length..].Trim());
                    var delta = parsed.RootElement.GetProperty("choices")[0].GetProperty("delta");
                    if (delta.TryGetProperty("content", out var content)) text.Append(content.GetString());
                }
                context.Response.ContentType = "application/json";
                var body = JsonSerializer.Serialize(new { id = "chatcmpl-" + Guid.NewGuid().ToString("N"),
                    @object = "chat.completion", created = 1, model = "fake-model",
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = text.ToString() }, finish_reason = "stop" } } });
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body), stop.Token);
                context.Response.Close();
                continue;
            }
            context.Response.ContentType = "text/event-stream";
            foreach (var chunk in reply)
            {
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(chunk), stop.Token);
                await context.Response.OutputStream.FlushAsync(stop.Token);
            }
            context.Response.Close();
        }
    }

    private static string Chunk(object delta, string? finish) => "data: " + JsonSerializer.Serialize(new { id = "chatcmpl-test", @object = "chat.completion.chunk", created = 1, model = "fake-model", choices = new[] { new { index = 0, delta, finish_reason = finish } } }) + "\n\n";

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Close();
        try { await server; }
        finally { stop.Dispose(); }
    }
}
