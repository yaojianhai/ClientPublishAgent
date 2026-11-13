using System;
using System.IO;
using Avalonia.Threading;
using ClientPublishAgent.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientPublishAgent.ViewModels;

public partial class PackagingPathsViewModel : ViewModelBase
{
	private readonly PackagingPathsStore _store = new();
	private readonly WindowsPublishConfigStore _publishConfigStore = new();
	[ObservableProperty] public partial string SourceDirectory { get; set; } = "";
	[ObservableProperty] public partial string PublishRootDirectory { get; set; } = "";
	[ObservableProperty] private string _linuxPublishRootDirectory = "";
	[ObservableProperty] private string _macOSPublishRootDirectory = "";
	[ObservableProperty] public partial string Status { get; set; } = "";
	public bool IsWindowsHost => OperatingSystem.IsWindows();
	public bool IsLinuxHost => OperatingSystem.IsLinux();
	public bool IsMacOSHost => OperatingSystem.IsMacOS();

	public PackagingPathsViewModel()
	{
		SourceDirectory = _store.SourceDirectory;
		PublishRootDirectory = _publishConfigStore.RootDirectory;
		LinuxPublishRootDirectory = _publishConfigStore.LinuxRootDirectory;
		MacOSPublishRootDirectory = _publishConfigStore.MacOSRootDirectory;
		PackagingPathsStore.SourceDirectoryChanged += HandleSourceDirectoryChanged;
	}

	private void HandleSourceDirectoryChanged(string path) => Dispatcher.UIThread.Post(() => SourceDirectory = path);

	[RelayCommand]
	private void Save()
	{
		try
		{
			_store.SaveSourceDirectory(SourceDirectory);
			SourceDirectory = _store.SourceDirectory;
			Status = "src 目录配置已保存，手工打包和 Agent 打包将共用此路径。";
		}
		catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
		{
			Status = "保存失败：" + ex.Message;
		}
	}

	[RelayCommand]
	private void SaveLinuxPublishRoot()
	{
		try
		{
			_publishConfigStore.SaveLinuxRootDirectory(LinuxPublishRootDirectory);
			LinuxPublishRootDirectory = _publishConfigStore.LinuxRootDirectory;
			Status = "Linux 打包输出根目录已保存。运行时目录会按 linux-x64/linux-arm64 区分。";
		}
		catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			Status = "保存失败：" + ex.Message;
		}
	}

	[RelayCommand]
	private void SaveMacOSPublishRoot()
	{
		try
		{
			_publishConfigStore.SaveMacOSRootDirectory(MacOSPublishRootDirectory);
			MacOSPublishRootDirectory = _publishConfigStore.MacOSRootDirectory;
			Status = "macOS 打包输出根目录已保存。运行时目录会按 osx-x64/osx-arm64 区分。";
		}
		catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			Status = "保存失败：" + ex.Message;
		}
	}

	[RelayCommand]
	private void SavePublishRoot()
	{
		try
		{
			_publishConfigStore.SaveRootDirectory(PublishRootDirectory);
			PublishRootDirectory = _publishConfigStore.RootDirectory;
			Status = "Windows 打包输出根目录已保存到 publish-config.json。";
		}
		catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			Status = "保存失败：" + ex.Message;
		}
	}
}
