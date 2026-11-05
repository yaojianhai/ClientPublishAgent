using Avalonia.Controls;
using ClientPublishAgent.ViewModels;

namespace ClientPublishAgent.Views;

public partial class MainWindow : Window
{
    private bool _closedSafely;
    private bool _closing;
    public MainWindow()
    {
        InitializeComponent();
        Closing += async (_, e) =>
        {
            if (_closedSafely) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            IsEnabled = false;
            try
            {
                if (DataContext is MainViewModel manual) await manual.CloseAsync();
                if (AgentWorkspace.DataContext is AgentWorkspaceViewModel agent && !await agent.CloseAsync()) return;
                _closedSafely = true;
                Close();
            }
            finally { _closing = false; if (!_closedSafely) IsEnabled = true; }
        };
    }
}