using System;
using ClientPublishAgent.Resources;
using ClientPublishAgent.Services.Harness;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientPublishAgent.ViewModels;

public sealed partial class ChatEntryViewModel : ViewModelBase
{
	private readonly AgentWorkspaceViewModel _owner;
	internal ChatEntryViewModel(AgentWorkspaceViewModel owner, string role, string title, string text, HarnessApproval? approval = null)
	{
		_owner = owner;
		Role = role;
		Title = title;
		Text = text;
		Approval = approval;
		IsPendingApproval = approval is not null;
		DecisionText = approval is not null ? ChatText.Waiting : "";
	}

	public string Role { get; }
	public string Title { get; }
	public HarnessApproval? Approval { get; }
	public bool IsUser => Role == "user";
	public bool IsApproval => Approval is not null;
	public IAsyncRelayCommand<ChatEntryViewModel> AllowOnceCommand => _owner.AllowOnceCommand;
	public IAsyncRelayCommand<ChatEntryViewModel> AllowSessionCommand => _owner.AllowSessionCommand;
	public IAsyncRelayCommand<ChatEntryViewModel> DenyToolCommand => _owner.DenyToolCommand;
	[ObservableProperty] public partial string Text { get; set; }
	[ObservableProperty] public partial bool IsPendingApproval { get; set; }
	[ObservableProperty] public partial string DecisionText { get; set; }

	internal void Resolve(string decision)
	{
		DecisionText = decision;
		IsPendingApproval = false;
	}

	internal StoredChatMessage Snapshot() => IsApproval
		? new("notice", Title, IsPendingApproval ? ChatText.Expired : DecisionText)
		: new(Role, Title, Text);
}
