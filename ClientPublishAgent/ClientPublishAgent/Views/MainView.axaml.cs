
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClientPublishAgent.ViewModels;

namespace ClientPublishAgent.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void ScriptLogTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox log) log.CaretIndex = log.Text?.Length ?? 0;
    }

    private async void RunTaskClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            viewModel.Status = "无法打开打包确认窗口。";
            return;
        }

        var confirmed = false;
        var dialog = new Window
        {
            Title = "确认执行打包任务",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancelButton = new Button { Content = "取消", MinWidth = 80 };
        cancelButton.Click += (_, _) => dialog.Close();
        var proceedButton = new Button { Content = "继续执行", MinWidth = 90 };
        proceedButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "请确认已检查并备份发布目录。打包脚本可能清理暂存目录，并更新旧版本基线和差分产物。是否继续？",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, proceedButton }
                }
            }
        };
        await dialog.ShowDialog(owner);
        if (confirmed) await viewModel.RunAfterConfirmationAsync();
    }
}