using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClientPublishAgent.Services;
using ClientPublishAgent.Services.Harness;
using ClientPublishAgent.Resources;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientPublishAgent.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly PowerShellTaskRunner _runner = new();
    private readonly PackagingPathsStore _pathsStore = new();
    private CancellationTokenSource? _cancellation;

    public string[] Systems { get; } = ["Windows", "Linux", "macOS"];
    public string[] Architectures { get; } = ["x64", "arm64"];
    public string[] ReleaseTypes { get; } = ["升级包", "安装包", "升级+安装包"];

    private string _selectedSystem = "Windows";
    public string SelectedSystem
    {
        get => _selectedSystem;
        set
        {
            if (SetProperty(ref _selectedSystem, value))
            {
                OnPropertyChanged(nameof(SelectionStatus));
                OnPropertyChanged(nameof(IsMacOSSelected));
                OnPropertyChanged(nameof(OutputDirectory));
            }
        }
    }

    private string _selectedArchitecture = "x64";
    public string SelectedArchitecture
    {
        get => _selectedArchitecture;
        set
        {
            if (SetProperty(ref _selectedArchitecture, value))
            {
                OnPropertyChanged(nameof(SelectionStatus));
                OnPropertyChanged(nameof(OutputDirectory));
            }
        }
    }

    private string _selectedReleaseType = "升级+安装包";
    public string SelectedReleaseType
    {
        get => _selectedReleaseType;
        set
        {
            if (SetProperty(ref _selectedReleaseType, value)) OnPropertyChanged(nameof(SelectionStatus));
        }
    }
    [ObservableProperty] public partial string CurrentVersion { get; set; } = "";
    [ObservableProperty] public partial string NextVersion { get; set; } = "";
    [ObservableProperty] public partial string OssHost { get; set; } = "";
    [ObservableProperty] public partial string SigningIdentity { get; set; } = Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY") ?? "";
    [ObservableProperty] public partial bool IsConfirmed { get; set; }
    [ObservableProperty] public partial bool PublishAot { get; set; } = true;
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "准备就绪";
    [ObservableProperty] public partial string Log { get; set; } = "";
    [ObservableProperty] public partial string ScriptInput { get; set; } = "";
    [ObservableProperty] public partial bool IsScriptInputAvailable { get; set; }

    public string DesktopScriptsDirectory => Path.Combine(AppContext.BaseDirectory, "Scripts", "Desktop");
    public string MarketServerScriptsDirectory => Path.Combine(AppContext.BaseDirectory, "Scripts", "MarketServer");
    public string SourceDirectory => PackagingPathProvider.SourceDirectory;
    public bool IsMacOSSelected => SelectedSystem == "macOS";
    public string OutputDirectory
    {
        get
        {
            var (plugin, runtime) = SelectedSystem switch
            {
                "Windows" => ("windows-package", "win-" + SelectedArchitecture),
                "Linux" => ("linux-package", "linux-" + SelectedArchitecture),
                "macOS" => ("macos-package", "osx-" + SelectedArchitecture),
                _ => ("", "")
            };
            if (plugin.Length == 0) return "请选择有效的系统。";
            try
            {
                return TaskCatalog.GetDesktopOutputRoot(plugin, DesktopScriptsDirectory, runtime);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or ArgumentException or NotSupportedException)
            {
                return "无法解析打包输出目录：" + ex.Message;
            }
        }
    }

    public string SelectionStatus => TryGetPluginSelection(out _, out _, out var reason)
        ? $"将先执行 {SelectedSystem} {SelectedArchitecture} {(PublishAot ? "Native AOT" : "非 AOT")} 发布，再生成{SelectedReleaseType}{(IsMacOSSelected ? (string.IsNullOrWhiteSpace(SigningIdentity) ? "（未配置签名）" : "（使用 Developer ID 签名）") : "")}。"
        : reason;

    public MainViewModel()
    {
        PackagingPathsStore.SourceDirectoryChanged += SourceDirectoryChanged;
        WindowsPublishConfigStore.ConfigurationChanged += PublishConfigurationChanged;
        _runner.InputAvailabilityChanged += available => Dispatcher.UIThread.Invoke(() =>
        {
            IsScriptInputAvailable = available;
            if (!available) ScriptInput = "";
        });
        SelectedSystem = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        SelectedArchitecture = architecture;
    }

    private bool TryGetPluginSelection(out string plugin, out string runtime, out string reason)
    {
        plugin = SelectedSystem switch
        {
            "Windows" => "windows-package",
            "Linux" => "linux-package",
            "macOS" => "macos-package",
            _ => ""
        };
        runtime = SelectedSystem switch
        {
            "Windows" => "win-" + SelectedArchitecture,
            "Linux" => "linux-" + SelectedArchitecture,
            "macOS" => "osx-" + SelectedArchitecture,
            _ => ""
        };
        if (plugin.Length == 0)
        {
            reason = "请选择有效的系统。";
            return false;
        }
        if (!TaskCatalog.Get(plugin).Supports(TaskCatalog.Host, runtime))
        {
            reason = SelectedSystem == "Windows" && SelectedArchitecture == "arm64"
                ? "当前 Windows 发布脚本及随包原生依赖仅支持 x64。"
                : $"当前主机不支持在此执行 {SelectedSystem} {SelectedArchitecture} 发布。";
            return false;
        }
        reason = "";
        return true;
    }

    private void SourceDirectoryChanged(string _) => OnPropertyChanged(nameof(SourceDirectory));

    private void PublishConfigurationChanged() => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(OutputDirectory)));

    partial void OnPublishAotChanged(bool value) => OnPropertyChanged(nameof(SelectionStatus));

    partial void OnSigningIdentityChanged(string value) => OnPropertyChanged(nameof(SelectionStatus));

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        try
        {
            if (!IsConfirmed)
                throw new InvalidOperationException("请先确认已检查发布目录及备份。");
            if (!TryGetPluginSelection(out var plugin, out var runtime, out var selectionError))
                throw new PlatformNotSupportedException(selectionError);

            var current = ParseVersion(CurrentVersion, "当前版本");
            var next = ParseVersion(NextVersion, "目标版本");
            if (next < current)
                throw new InvalidOperationException("目标版本不能小于当前版本；版本相同时将跳过升级包生成，只打包安装包。");
            if (OssHost.Length > 2048 || System.Linq.Enumerable.Any(OssHost, char.IsControl))
                throw new ArgumentException("Invalid OSS address.");

            var sourceRoot = PackagingPathProvider.ResolveRepositoryRoot(SourceDirectory);
            if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("源码仓库目录不存在：" + sourceRoot);
            var project = Path.Combine(SourceDirectory, "WarrenqF1.Desktop", "WarrenqF1.Desktop.csproj");
            if (!File.Exists(project))
                throw new FileNotFoundException("打包脚本还需要目标项目源码和依赖，未找到项目文件：" + project, project);

            {
                var scriptsRoot = DesktopScriptsDirectory;
                var packageType = SelectedReleaseType switch
                {
                    "升级包" => "upgrade",
                    "安装包" => "installer",
                    _ => "upgrade-installer"
                };
                var command = TaskCatalog.Get(plugin).Prepare(new PluginRequest(plugin, scriptsRoot, runtime,
                    CurrentVersion.Trim(), NextVersion.Trim(), OssHost.Trim(), SourceDirectory, packageType, PublishAot, SigningIdentity.Trim()));
                IsRunning = true; IsConfirmed = false;
                _cancellation = new CancellationTokenSource();
                AppendLog("输出根目录：" + command.OutputRoot);
                Status = "运行跨平台插件；可取消";
                var pluginCode = await RunPluginCommandAsync(command, new Progress<string>(AppendOutput), _cancellation.Token);
                _cancellation.Token.ThrowIfCancellationRequested();
                Status = $"插件退出代码：{pluginCode}，请检查日志/产物。";
                return;
            }
            throw new InvalidOperationException("未能准备发布插件任务。");
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

    private async Task<int> RunPluginCommandAsync(PluginCommand command, IProgress<string> output, CancellationToken cancellationToken)
    {
        if (command.ScriptPath is null)
            return await _runner.RunProcessAsync(command.Executable, command.Arguments, command.Directory,
                output, cancellationToken, interactive: true);

        var sourceDirectory = command.SourceDirectory ?? SourceDirectory;
        var arguments = command.Arguments;
        return await _runner.RunProcessAsync(command.Executable, arguments, command.Directory,
            output, cancellationToken, interactive: true,
            environment: new Dictionary<string, string?>
            {
                ["CLIENT_PUBLISH_SOURCE_DIRECTORY"] = sourceDirectory
            });
    }

    private Task<int> RunPowerShellScriptAsync(string scriptPath, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var fullArguments = new List<string> { "-NoProfile" };
        if (OperatingSystem.IsWindows()) fullArguments.AddRange(["-ExecutionPolicy", "Bypass"]);
        fullArguments.AddRange(["-File", scriptPath, "-CurrentVersion", CurrentVersion.Trim(), "-NextVersion", NextVersion.Trim()]);
        if (!string.IsNullOrWhiteSpace(OssHost)) fullArguments.AddRange(["-OssHost", OssHost.Trim()]);
        return _runner.RunProcessAsync(PowerShellTaskRunner.ResolvePowerShell(), fullArguments,
            Path.GetDirectoryName(scriptPath)!, progress, cancellationToken, interactive: true,
            environment: new Dictionary<string, string?>
            {
                ["CLIENT_PUBLISH_SOURCE_DIRECTORY"] = SourceDirectory
            });
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
        WindowsPublishConfigStore.ConfigurationChanged -= PublishConfigurationChanged;
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
