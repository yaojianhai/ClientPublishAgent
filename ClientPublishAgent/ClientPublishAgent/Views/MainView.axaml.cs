
using Avalonia;
using Avalonia.Controls;

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
}