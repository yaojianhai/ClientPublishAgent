using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ClientPublishAgent.Services.Harness;
using ClientPublishAgent.ViewModels;
using ClientPublishAgent.Views;

namespace ClientPublishAgent;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            AgentWorkspaceViewModel viewModel => new AgentView(viewModel),
            MainViewModel viewModel => new MainView { DataContext = viewModel },
            PackagingPathsViewModel viewModel => new PackagingPathsView { DataContext = viewModel },
            ScriptManagerViewModel viewModel => new ScriptManagerView { DataContext = viewModel },
            _ => new TextBlock { Text = "Not Found: " + param.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}