using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClientPublishAgent.Services.Harness;
using ClientPublishAgent.ViewModels;

namespace ClientPublishAgent.Views;

public partial class AgentView : UserControl
{
	private AgentWorkspaceViewModel? _messagesOwner;
	private readonly HashSet<ChatEntryViewModel> _observedEntries = [];
	private bool _attached;
	private bool _followTail = true;
	private bool _scrollQueued;
	private readonly DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromMinutes(1) };

	public AgentView() : this(new AgentWorkspaceViewModel()) { }

	/// <summary>Creates the chat view with the supplied workspace and its message stream.</summary>
	public AgentView(AgentWorkspaceViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(viewModel);
		InitializeComponent();
		_historyTimer.Tick += (_, _) => UpdateHistoryClock();
		ChatInput.AddHandler(InputElement.KeyDownEvent, ChatInputKeyDown, RoutingStrategies.Tunnel);
		DataContextChanged += (_, _) => BindMessages();
		DataContext = viewModel;
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		_attached = true;
		BindMessages();
		_historyTimer.Start();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		_attached = false;
		_historyTimer.Stop();
		UnbindMessages();
		base.OnDetachedFromVisualTree(e);
	}

	private void BindMessages()
	{
		UnbindMessages();
		if (!_attached || DataContext is not AgentWorkspaceViewModel viewModel) return;
		_messagesOwner = viewModel;
		UpdateHistoryClock();
		WorkspaceGrid.ColumnDefinitions[4].Width = viewModel.IsDetailsVisible ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
		viewModel.ChatItems.CollectionChanged += MessagesChanged;
		viewModel.PropertyChanged += WorkspaceChanged;
		foreach (var entry in viewModel.ChatItems) Observe(entry);
		_followTail = true;
		QueueScroll();
	}

	private void UpdateHistoryClock()
	{
		if (DataContext is AgentWorkspaceViewModel viewModel) viewModel.HistoryNow = DateTimeOffset.UtcNow;
	}

	private void UnbindMessages()
	{
		if (_messagesOwner is not null)
		{
			_messagesOwner.ChatItems.CollectionChanged -= MessagesChanged;
			_messagesOwner.PropertyChanged -= WorkspaceChanged;
		}
		foreach (var entry in _observedEntries) entry.PropertyChanged -= EntryChanged;
		_observedEntries.Clear();
		_messagesOwner = null;
	}

	private void Observe(ChatEntryViewModel entry)
	{
		if (_observedEntries.Add(entry)) entry.PropertyChanged += EntryChanged;
	}

	private void MessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			foreach (var entry in _observedEntries) entry.PropertyChanged -= EntryChanged;
			_observedEntries.Clear();
			_followTail = true;
		}
		if (e.OldItems is not null)
			foreach (ChatEntryViewModel entry in e.OldItems)
				if (_observedEntries.Remove(entry)) entry.PropertyChanged -= EntryChanged;
		if (e.NewItems is not null)
			foreach (ChatEntryViewModel entry in e.NewItems)
			{
				Observe(entry);
				if (entry.IsUser) _followTail = true;
			}
		QueueScroll();
	}

	private void EntryChanged(object? sender, PropertyChangedEventArgs e) => QueueScroll();
	private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(AgentWorkspaceViewModel.IsBusy)) QueueScroll();
		if (e.PropertyName == nameof(AgentWorkspaceViewModel.IsDetailsVisible))
			WorkspaceGrid.ColumnDefinitions[4].Width = _messagesOwner?.IsDetailsVisible == true ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
		if (e.PropertyName == nameof(AgentWorkspaceViewModel.IsConnected) && _messagesOwner?.IsConnected == true) ChatInput.Focus();
	}

	private void QueueScroll()
	{
		JumpToLatest.IsVisible = !_followTail;
		if (!_attached || !_followTail || _scrollQueued) return;
		_scrollQueued = true;
		// Message measurement must finish before scrolling to its updated extent.
		Dispatcher.UIThread.Post(() =>
		{
			_scrollQueued = false;
			if (_attached && _followTail && ChatScroll.Viewport.Height > 0) ChatScroll.ScrollToEnd();
		}, DispatcherPriority.Loaded);
	}

	private void ChatScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		if (e.OffsetDelta.Y < 0 && e.ExtentDelta.Y >= 0 && e.ViewportDelta.Y == 0)
			_followTail = ChatScroll.Extent.Height - ChatScroll.Viewport.Height - ChatScroll.Offset.Y <= 32;
		if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
		{
			QueueScroll();
			return;
		}
		if (e.OffsetDelta.Y != 0)
		{
			_followTail = ChatScroll.Extent.Height - ChatScroll.Viewport.Height - ChatScroll.Offset.Y <= 32;
			JumpToLatest.IsVisible = !_followTail;
		}
	}

	private void JumpToLatestClicked(object? sender, RoutedEventArgs e)
	{
		_followTail = true;
		QueueScroll();
	}

	private async void SessionItemClicked(object? sender, RoutedEventArgs e)
	{
		if (sender is not Control { DataContext: StoredConversation session } || DataContext is not AgentWorkspaceViewModel viewModel) return;
		viewModel.SelectedSession = session;
		await viewModel.SwitchToSessionAsync(session);
	}

	private async void RenameSessionClicked(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem { DataContext: StoredConversation session } || DataContext is not AgentWorkspaceViewModel viewModel) return;
		viewModel.SelectedSession = session;
		var nameInput = new TextBox { Text = session.Name, MinWidth = 280 };
		var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
		var dialog = new Window
		{
			Title = "重命名历史会�?",
			Width = 400,
			Height = 190,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner
		};
		var saveButton = new Button { Content = "保存", MinWidth = 80 };
		saveButton.Click += (_, _) =>
		{
			if (viewModel.RenameSession(session, nameInput.Text ?? "")) dialog.Close();
			else error.Text = viewModel.Status;
		};
		var cancelButton = new Button { Content = "取消", MinWidth = 80 };
		cancelButton.Click += (_, _) => dialog.Close();
		dialog.Content = new StackPanel
		{
			Margin = new Thickness(18),
			Spacing = 10,
			Children =
			{
				new TextBlock { Text = "会话名称" },
				nameInput,
				error,
				new StackPanel
				{
					Orientation = Avalonia.Layout.Orientation.Horizontal,
					HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
					Spacing = 8,
					Children = { cancelButton, saveButton }
				}
			}
		};
		if (TopLevel.GetTopLevel(this) is not Window owner)
		{
			viewModel.Status = "无法打开重命名窗口�??";
			return;
		}
		await dialog.ShowDialog(owner);
		nameInput.Focus();
	}

	private async void DeleteSessionClicked(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem { DataContext: StoredConversation session } || DataContext is not AgentWorkspaceViewModel viewModel) return;
		viewModel.SelectedSession = session;
		await viewModel.DeleteSessionAsync(session);
	}

	private async void ChatInputKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
		e.Handled = true;
		if (DataContext is AgentWorkspaceViewModel viewModel && viewModel.CanSend)
			await viewModel.SendCommand.ExecuteAsync(null);
	}

	private async void ScriptInputKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
		e.Handled = true;
		if (DataContext is AgentWorkspaceViewModel viewModel && viewModel.IsScriptInputAvailable)
			await viewModel.SendScriptInputCommand.ExecuteAsync(null);
	}

	private void ScriptLogTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (sender is TextBox log) log.CaretIndex = log.Text?.Length ?? 0;
	}
}
