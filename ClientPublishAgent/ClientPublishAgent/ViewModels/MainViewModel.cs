using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    private readonly MacSigningSettingsStore _signingSettings;
    private bool _signingSettingsReady;
    private CancellationTokenSource? _cancellation;

    public string[] Architectures { get; } = ["x64", "arm64"];
    public string[] ReleaseTypes { get; } = ["升级包", "安装包", "升级+安装包"];

    public string SelectedSystem => TaskCatalog.Host switch
    {
        NativeHost.Windows => "Windows",
        NativeHost.MacOS => "macOS",
        _ => "Linux"
    };
    private string _selectedArchitecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    public string SelectedArchitecture
    {
        get => _selectedArchitecture;
        set
        {
            if (value is not ("x64" or "arm64")) throw new ArgumentOutOfRangeException(nameof(value));
            if (SetProperty(ref _selectedArchitecture, value))
            {
                RefreshVersions();
            }
        }
    }

    private string _selectedReleaseType = "升级+安装包";
    public string SelectedReleaseType
    {
        get => _selectedReleaseType;
        set => SetProperty(ref _selectedReleaseType, value);
    }
    private string _currentVersion = "";
    public string CurrentVersion
    {
        get => _currentVersion;
        private set => SetProperty(ref _currentVersion, value);
    }
    private string _nextVersion = "";
    public string NextVersion
    {
        get => _nextVersion;
        private set => SetProperty(ref _nextVersion, value);
    }
    private string _versionSourceStatus = "";
    public string VersionSourceStatus
    {
        get => _versionSourceStatus;
        private set => SetProperty(ref _versionSourceStatus, value);
    }
    [ObservableProperty] public partial string SigningIdentity { get; set; } = Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY") ?? "";
    [ObservableProperty] public partial string NotaryKeychainProfile { get; set; } = Environment.GetEnvironmentVariable("APPLE_NOTARY_PROFILE") ?? "";
    [ObservableProperty] public partial string SigningSettingsStatus { get; set; } = "";
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
    public MainViewModel() : this(new MacSigningSettingsStore()) { }

    public MainViewModel(MacSigningSettingsStore signingSettings)
    {
        _signingSettings = signingSettings;
        try
        {
            var saved = _signingSettings.Load();
            if (saved is not null)
            {
                SigningIdentity = saved.SigningIdentity ?? throw new InvalidDataException("签名身份缺失。");
                NotaryKeychainProfile = saved.NotaryKeychainProfile ?? throw new InvalidDataException("公证配置名称缺失。");
            }
            _signingSettingsReady = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            SigningSettingsStatus = "读取 macOS 签名配置失败，自动保存已暂停：" + ex.Message;
        }
        WindowsPublishConfigStore.ConfigurationChanged += PublishConfigurationChanged;
        _runner.InputAvailabilityChanged += available => Dispatcher.UIThread.Invoke(() =>
        {
            IsScriptInputAvailable = available;
            if (!available) ScriptInput = "";
        });
        RefreshVersions();
    }

    partial void OnSigningIdentityChanged(string value) => SaveSigningSettings();
    partial void OnNotaryKeychainProfileChanged(string value) => SaveSigningSettings();

    private void SaveSigningSettings()
    {
        if (!_signingSettingsReady) return;
        try
        {
            _signingSettings.Save(SigningIdentity ?? "", NotaryKeychainProfile ?? "");
            SigningSettingsStatus = "签名身份和公证配置名称已保存到本机。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            SigningSettingsStatus = "保存 macOS 签名配置失败：" + ex.Message;
        }
    }

    private bool TryGetPluginSelection(out string plugin, out string runtime, out string reason)
    {
        var host = TaskCatalog.Host;
        plugin = host switch
        {
            NativeHost.Windows => "windows-package",
            NativeHost.Linux => "linux-package",
            NativeHost.MacOS => "macos-package",
            _ => ""
        };
        runtime = (host, SelectedArchitecture) switch
        {
            (NativeHost.Windows, "x64") => "win-x64",
            (NativeHost.Windows, "arm64") => "win-arm64",
            (NativeHost.Linux, "x64") => "linux-x64",
            (NativeHost.Linux, "arm64") => "linux-arm64",
            (NativeHost.MacOS, "x64") => "osx-x64",
            (NativeHost.MacOS, "arm64") => "osx-arm64",
            _ => ""
        };
        if (plugin.Length == 0)
        {
            reason = "当前运行系统不支持手工打包。";
            return false;
        }
        if (runtime.Length == 0)
        {
            reason = SelectedArchitecture.Length == 0
                ? "当前运行进程架构不支持发布。"
                : $"当前主机不支持在此执行 {SelectedSystem} {SelectedArchitecture} 原生发布。";
            return false;
        }
        if (!TaskCatalog.Get(plugin).Supports(TaskCatalog.Host, runtime))
        {
            reason = $"当前发布脚本不支持 {SelectedSystem} {SelectedArchitecture} 目标。";
            return false;
        }
        reason = "";
        return true;
    }

    private void PublishConfigurationChanged() => Dispatcher.UIThread.Post(() =>
    {
        RefreshVersions();
    });

    private void RefreshVersions()
    {
        if (!TryGetPluginSelection(out var plugin, out var runtime, out var reason))
        {
            CurrentVersion = "";
            NextVersion = "";
            VersionSourceStatus = reason;
            return;
        }

        try
        {
            var outputRoot = TaskCatalog.GetDesktopOutputRoot(plugin, DesktopScriptsDirectory, runtime);
            var versions = PackagingVersionReader.GetVersionsOrDefault(outputRoot, out var hasHistory);
            if (!hasHistory)
            {
                CurrentVersion = versions.CurrentVersion;
                NextVersion = versions.NextVersion;
                VersionSourceStatus = "未找到历史增量包，使用初始版本 1.0.0，下次版本 1.0.1。";
                return;
            }

            CurrentVersion = versions.CurrentVersion;
            NextVersion = versions.NextVersion;
            VersionSourceStatus = "已从最近输出的增量包自动读取当前版本，并计算下次版本。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or ArgumentException or NotSupportedException)
        {
            CurrentVersion = "";
            NextVersion = "";
            VersionSourceStatus = "自动读取版本失败：" + ex.Message;
        }
    }

    public async Task RunAfterConfirmationAsync()
    {
        IsConfirmed = true;
        try { await RunCommand.ExecuteAsync(null); }
        finally { IsConfirmed = false; }
    }

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
            if (IsMacOSSelected && SelectedReleaseType != "升级包" &&
                string.IsNullOrWhiteSpace(SigningIdentity) != string.IsNullOrWhiteSpace(NotaryKeychainProfile))
                throw new InvalidOperationException("macOS 完整分发要求同时填写 Developer ID 签名身份和 notarytool 钥匙串配置名称；两项都留空时生成未签名测试包。");

            var current = ParseVersion(CurrentVersion, "当前版本");
            var next = ParseVersion(NextVersion, "目标版本");
            if (next < current)
                throw new InvalidOperationException("目标版本不能小于当前版本；版本相同时将跳过升级包生成，只打包安装包。");

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
                    CurrentVersion.Trim(), NextVersion.Trim(), "", SourceDirectory, packageType, PublishAot,
                    SigningIdentity.Trim(), NotaryKeychainProfile.Trim()));
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
