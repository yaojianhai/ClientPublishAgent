using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClientPublishAgent.Services;
using ClientPublishAgent.Resources;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientPublishAgent.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly PowerShellTaskRunner _runner = new();
    private CancellationTokenSource? _cancellation;

    public PackagingTask[] Tasks { get; } =
    [
        new("发布 + 升级包", "publish-upgrade-win-x64.ps1"),
        new("发布 + 升级包 + 安装包", "publish-upgrade-package-win-x64.ps1"),
        new("仅生成升级包（使用已有发布目录）", "upgrade-win-x64.ps1"),
        new("Windows x64 完整打包 · Scripts 根目录", "", "windows-package", "win-x64"),
        new("Linux x64 完整打包 · Linux/Windows WSL Debian", "", "linux-package", "linux-x64"),
        new("Linux arm64 完整打包 · Linux/Windows WSL Debian", "", "linux-package", "linux-arm64"),
        new("macOS arm64 完整打包 · 仅 macOS", "", "macos-package", "osx-arm64"),
        new("macOS x64 完整打包 · 仅 macOS", "", "macos-package", "osx-x64"),
        new("MarketServer Linux x64 AOT · 原生 Linux / 仓库根目录", "", "marketserver", "linux-x64")
    ];

    [ObservableProperty] public partial PackagingTask? SelectedTask { get; set; }
    [ObservableProperty] public partial string ScriptsDirectory { get; set; } = "";
    [ObservableProperty] public partial string CurrentVersion { get; set; } = "";
    [ObservableProperty] public partial string NextVersion { get; set; } = "";
    [ObservableProperty] public partial string OssHost { get; set; } = "";
    [ObservableProperty] public partial bool IsConfirmed { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "准备就绪";
    [ObservableProperty] public partial string Log { get; set; } = "";
    [ObservableProperty] public partial string ScriptInput { get; set; } = "";
    [ObservableProperty] public partial bool IsScriptInputAvailable { get; set; }

    public MainViewModel()
    {
        _runner.InputAvailabilityChanged += available => Dispatcher.UIThread.Invoke(() =>
        {
            IsScriptInputAvailable = available;
            if (!available) ScriptInput = "";
        });
        var plugin = OperatingSystem.IsMacOS() ? "macos-package" : "linux-package";
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        SelectedTask = OperatingSystem.IsWindows() ? Tasks[0] :
            Array.Find(Tasks, task => task.PluginId == plugin && task.Runtime.EndsWith(architecture, StringComparison.Ordinal));
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        try
        {
            if (!IsConfirmed)
                throw new InvalidOperationException("请先确认已检查发布目录及备份。");
            if (SelectedTask is null)
                throw new InvalidOperationException("请选择打包任务。");

            var current = ParseVersion(CurrentVersion, "当前版本");
            var next = ParseVersion(NextVersion, "目标版本");
            if (next <= current)
                throw new InvalidOperationException("目标版本必须大于当前版本，避免脚本跳过差分并更新基线。");
            if (string.IsNullOrWhiteSpace(ScriptsDirectory) || !Path.IsPathFullyQualified(ScriptsDirectory))
                throw new ArgumentException("请填写绝对路径。");
            if (OssHost.Length > 2048 || System.Linq.Enumerable.Any(OssHost, char.IsControl))
                throw new ArgumentException("Invalid OSS address.");

            var directory = Path.GetFullPath(ScriptsDirectory?.Trim() ?? "");
            if (SelectedTask.PluginId is { } plugin)
            {
                var command = TaskCatalog.Get(plugin).Prepare(new PluginRequest(plugin, directory, SelectedTask.Runtime,
                    CurrentVersion.Trim(), NextVersion.Trim(), OssHost.Trim()));
                IsRunning = true; IsConfirmed = false;
                _cancellation = new CancellationTokenSource();
                AppendLog("输出根目录：" + command.OutputRoot);
                Status = "运行跨平台插件；可取消";
                var code = await _runner.RunProcessAsync(command.Executable, command.Arguments, command.Directory,
                    new Progress<string>(AppendOutput), _cancellation.Token, interactive: true);
                _cancellation.Token.ThrowIfCancellationRequested();
                Status = $"插件退出代码：{code}，请检查日志/产物。";
                return;
            }
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("旧 Windows 任务只支持 Windows。请选择对应原生平台插件。");
            var script = Path.Combine(directory, SelectedTask.ScriptName);
            if (!File.Exists(script))
                throw new FileNotFoundException("找不到任务脚本", script);
            IsRunning = true;
            Status = "正在检查配置";
            _cancellation = new CancellationTokenSource();
            var configPath = Path.Combine(directory, "publish-config.json");
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath, _cancellation.Token));
            if (!config.RootElement.TryGetProperty("RootDirectory", out var rootElement) ||
                rootElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(rootElement.GetString()) ||
                !Path.IsPathFullyQualified(rootElement.GetString()!))
                throw new InvalidOperationException("publish-config.json 中必须配置绝对路径 RootDirectory。");

            Log = "";
            AppendLog($"发布根目录：{rootElement.GetString()}");
            AppendLog($"执行任务：{SelectedTask.Name}");
            AppendLog("首次发布或 OldRelease 为空时，脚本会跳过差分包生成；请检查脚本输出。");
            IsConfirmed = false;
            Status = "正在运行";
            var progress = new Progress<string>(AppendOutput);
            int exitCode = await _runner.RunAsync(script, CurrentVersion.Trim(), NextVersion.Trim(),
                OssHost?.Trim() ?? "", progress, _cancellation.Token);
            _cancellation.Token.ThrowIfCancellationRequested();
            Status = exitCode == 0 ? "脚本执行完成，请检查日志及产物" : $"脚本执行失败（退出代码 {exitCode}）";
            AppendLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消；脚本可能已修改发布目录，请检查后再重试";
            AppendLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or Win32Exception or NotSupportedException)
        {
            Status = "任务未完成";
            AppendLog($"错误：{ex.Message}");
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private async Task SendScriptInputAsync()
    {
        if (!IsScriptInputAvailable) return;
        var input = ScriptInput;
        ScriptInput = "";
        try { await _runner.SendInputAsync(input); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Status = string.Format(ChatText.ScriptInputError, ex.Message);
        }
    }
    public async Task CloseAsync()
    {
        _cancellation?.Cancel();
        while (IsRunning) await Task.Delay(30);
    }

    private static Version ParseVersion(string? value, string label)
    {
        value = value?.Trim() ?? "";
        if (!Regex.IsMatch(value, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$") ||
            !Version.TryParse(value, out var version))
            throw new ArgumentException($"{label}必须为三段数字版本号，例如 1.2.3。");
        return version;
    }

    private void AppendLog(string line)
    {
        AppendOutput(line + Environment.NewLine);
    }

    private void AppendOutput(string text)
    {
        const int maxLength = 100_000;
        var updated = Log + text;
        Log = updated.Length <= maxLength ? updated : updated[^maxLength..];
    }
}
