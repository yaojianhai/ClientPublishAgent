using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClientPublishAgent.ViewModels;

namespace ClientPublishAgent.Views;

public partial class AgentView : UserControl
{
	private AgentWorkspaceViewModel? _messagesOwner;
	private readonly HashSet<ChatEntryViewModel> _observedEntries = [];
	private bool _attached;
	private bool _followTail = true;
	private bool _scrollQueued;

	public AgentView() : this(new AgentWorkspaceViewModel()) { }

	/// <summary>Creates the chat view with the supplied workspace and its message stream.</summary>
	public AgentView(AgentWorkspaceViewModel viewModel)
	{
		ArgumentNullException.ThrowIfNull(viewModel);
		InitializeComponent();
		DataContextChanged += (_, _) => BindMessages();
		DataContext = viewModel;
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		_attached = true;
		BindMessages();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		_attached = false;
		UnbindMessages();
		base.OnDetachedFromVisualTree(e);
	}

	private void BindMessages()
	{
		UnbindMessages();
		if (!_attached || DataContext is not AgentWorkspaceViewModel viewModel) return;
		_messagesOwner = viewModel;
		viewModel.ChatItems.CollectionChanged += MessagesChanged;
		viewModel.PropertyChanged += WorkspaceChanged;
		foreach (var entry in viewModel.ChatItems) Observe(entry);
		_followTail = true;
		QueueScroll();
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

	private async void ChatInputKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
		e.Handled = true;
		if (DataContext is AgentWorkspaceViewModel viewModel && viewModel.CanSend)
			await viewModel.SendCommand.ExecuteAsync(null);
	}
}
