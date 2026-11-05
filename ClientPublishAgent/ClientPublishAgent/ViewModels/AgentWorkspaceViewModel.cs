using System;
using System.ClientModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClientPublishAgent.Services.Harness;
using ClientPublishAgent.Services;
using ClientPublishAgent.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientPublishAgent.ViewModels;

public partial class AgentWorkspaceViewModel : ViewModelBase
{
	private HarnessWorkspace? _workspace;
	private readonly PlatformStore? _store;
	private StoredConversation? _current;
	private Task? _operation;
	private bool _closing;
	private bool _restoredUnresolved;
	private readonly StringBuilder _transcript = new();
	private readonly StringBuilder _localLog = new();
	private ChatEntryViewModel? _assistantEntry;
	public ObservableCollection<ChatEntryViewModel> ChatItems { get; } = [];
	public bool IsChatEmpty => ChatItems.Count == 0;
	public ObservableCollection<AgentProfile> Profiles { get; } = [];
	public ObservableCollection<StoredConversation> Sessions { get; } = [];
	[ObservableProperty] public partial AgentProfile? SelectedProfile { get; set; }
	[ObservableProperty] public partial StoredConversation? SelectedSession { get; set; }
	[ObservableProperty] public partial string ProfileName { get; set; } = "Personal Assistant";
	[ObservableProperty] public partial string Instructions { get; set; } = "";
	[ObservableProperty] public partial string WorkspaceRoot { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
	[ObservableProperty] public partial bool EnableFiles { get; set; }
	[ObservableProperty] public partial bool EnableTerminal { get; set; }
	[ObservableProperty] public partial bool EnablePackaging { get; set; }
	[ObservableProperty] public partial bool EnableMemory { get; set; }
	[ObservableProperty] public partial bool EnableSkills { get; set; }
	[ObservableProperty] public partial bool EnableAutoMemory { get; set; }
	[ObservableProperty] public partial bool EnableAutoSkills { get; set; }
	[ObservableProperty] public partial bool EnableWebSearch { get; set; }
	[ObservableProperty] public partial string SearchEndpoint { get; set; } = "";
	[ObservableProperty] public partial string MemoryDirectory { get; set; } = ".agent-memory";
	[ObservableProperty] public partial string SkillsDirectory { get; set; } = ".agent-skills";
	[ObservableProperty] public partial string MarketServerRoot { get; set; } = "";
	[ObservableProperty] public partial bool EnableBackground { get; set; }
	[ObservableProperty] public partial bool EnableTelemetry { get; set; }
	[ObservableProperty] public partial bool EnableCompaction { get; set; }
	[ObservableProperty] public partial int ContextTokens { get; set; } = 32000;
	[ObservableProperty] public partial int OutputTokens { get; set; } = 4096;
	[ObservableProperty] public partial string Search { get; set; } = "";
	[ObservableProperty] public partial string HistoryWarning { get; set; } = "";
	[ObservableProperty] public partial string SessionName { get; set; } = "New conversation";
	[ObservableProperty] public partial string HarnessInfo { get; set; } = "";
	[ObservableProperty] public partial string ModeText { get; set; } = "未初始化";
	[ObservableProperty] public partial string TodoText { get; set; } = "暂无 todo";
	public string CapabilityCatalog => "工作区：真实 Harness FileAccessProvider + 隔离 FileSystemAgentFileStore（读/写/列表/搜索/替换/删除需授权），另有带内容 hash 的简化读写工具。默认逐次审批，也可在聊天中允许当前会话的所有已启用工具。\n" + string.Join("\n", TaskCatalog.Plugins.Select(p => p.Id + " · " + p.Description)) +
		"\n手动 memory / skills 需授权。独立自动 memory：每轮重新读取指定目录 UTF-8 md/txt/json 文本并送到配置模型，跨会话持久，不写聊天。独立自动 skills：真实 AgentSkillsProvider + 受限 SDK source，每轮刷新 SKILL.md 元数据广告；load_skill/read_skill_resource 需授权并受 hash 锁定，无脚本 runner、不可执行。\n自动目录预算：8 层、256 entries、128 files、64KiB/文件、256KiB 总量；symlink/junction 拒绝，损坏/超限显式失败。请手工创建目录。内容不可信，已识别凭据脱敏。\n联网搜索：用户配置 SearXNG JSON /search endpoint（服务须启用 JSON）；适用于 DeepSeek/OpenAI-compatible。授权后 query 发给该服务，不附带模型密钥，不跟随 redirect、不访问结果 URL。只返回前10条标题/URL/片段/来源，外部内容不可信。\nOTel：仅 opt-in 本地 instrumentation，无默认 exporter。\n背景 Researcher：仅推理，无文件/终端/联网工具。\nTodo / plan / execute：真实 Harness providers；模式直接切换并持久保存。";

	public AgentWorkspaceViewModel() : this(null) { }
	public AgentWorkspaceViewModel(PlatformStore? store)
	{
		ChatItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsChatEmpty));
		try
		{
			_store = store ?? new PlatformStore();
			foreach (var profile in _store.LoadProfiles()) Profiles.Add(profile);
			SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _store.LastSelectedProfileId) ?? Profiles[0];
			Status = "配置已加载；缓存：" + _store.Root + "（含敏感对话；请保护用户账户/启用磁盘加密）";
			RefreshHistory();
		}
		catch (Exception ex) { _store = null; Status = "本地缓存加载失败（不会覆盖损坏数据）：" + ex.Message; }
	}

	partial void OnSelectedProfileChanged(AgentProfile? value)
	{
		if (value is null) return;
		Endpoint = value.Endpoint; Model = value.Model; KeyEnvironmentVariable = value.KeyEnvironmentVariable;
		ScriptsDirectory = value.ScriptsDirectory; ProfileName = value.Name; Instructions = value.Instructions;
		MarketServerRoot = value.MarketServerRoot;
		WorkspaceRoot = value.WorkspaceRoot; EnableFiles = value.Files; EnableTerminal = value.Terminal;
		EnablePackaging = value.Packaging; EnableBackground = value.BackgroundResearch; EnableTelemetry = value.Telemetry;
		EnableMemory = value.FileMemory; EnableSkills = value.Skills; MemoryDirectory = value.MemoryDirectory; SkillsDirectory = value.SkillsDirectory;
		EnableAutoMemory = value.AutoMemory; EnableAutoSkills = value.AutoSkills;
		EnableWebSearch = value.WebSearch; SearchEndpoint = value.SearchEndpoint ?? "";
		EnableCompaction = value.Compaction; ContextTokens = value.ContextTokens; OutputTokens = value.OutputTokens;
		RefreshHistory();
	}
	partial void OnSelectedSessionChanged(StoredConversation? value) { if (value is not null) SessionName = value.Name; }
	partial void OnSearchChanged(string value) => RefreshHistory();
	private AgentProfile CaptureProfile() => new()
	{
		Id = SelectedProfile?.Id ?? Guid.NewGuid(), Name = ProfileName.Trim(), Instructions = Instructions,
		Endpoint = Endpoint.Trim(), Model = Model.Trim(), KeyEnvironmentVariable = KeyEnvironmentVariable.Trim(),
		ScriptsDirectory = ScriptsDirectory.Trim(), WorkspaceRoot = WorkspaceRoot.Trim(), Files = EnableFiles,
		MarketServerRoot = MarketServerRoot.Trim(),
		FileMemory = EnableMemory, Skills = EnableSkills, MemoryDirectory = MemoryDirectory.Trim(), SkillsDirectory = SkillsDirectory.Trim(),
		AutoMemory = EnableAutoMemory, AutoSkills = EnableAutoSkills, WebSearch = EnableWebSearch,
		SearchEndpoint = string.IsNullOrWhiteSpace(SearchEndpoint) ? null : SearchEndpoint.Trim(),
		Terminal = EnableTerminal, Packaging = EnablePackaging, BackgroundResearch = EnableBackground,
		Telemetry = EnableTelemetry, Compaction = EnableCompaction, ContextTokens = ContextTokens, OutputTokens = OutputTokens
	};
	[RelayCommand]
	private void SaveProfile() => TrySaveProfile();
	private bool TrySaveProfile()
	{
		if (!RequireStore() || !CanEditSettings) return false;
		try
		{
			var profile = CaptureProfile();
			if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Agent 名称不能为空。");
			if (profile.KeyEnvironmentVariable.Length > 128 || (profile.KeyEnvironmentVariable.Length > 0 &&
				!System.Text.RegularExpressions.Regex.IsMatch(profile.KeyEnvironmentVariable, @"^[A-Za-z_][A-Za-z0-9_]*$")))
				throw new ArgumentException("仅填写环境变量名，不是密钥。");
			var next = Profiles.Where(p => p.Id != profile.Id).Append(profile).ToList();
			_store!.SaveProfiles(next, profile.Id);
			Profiles.Clear(); foreach (var p in next) Profiles.Add(p);
			SelectedProfile = profile;
			Status = "Agent 配置已保存（不含密钥）；配置变更后旧会话不能误恢复。";
			return true;
		}
		catch (Exception ex) { Status = "保存配置失败：" + ex.Message; return false; }
	}
	[RelayCommand]
	private void NewProfile()
	{
		if (!RequireStore() || !CanEditSettings) return;
		try
		{
			var profile = new AgentProfile { Name = "New Agent" };
			_store!.SaveProfiles(Profiles.Append(profile), profile.Id);
			Profiles.Add(profile); SelectedProfile = profile;
			Status = "新 Agent 已保存。";
		}
		catch (Exception ex) { Status = "新增配置失败：" + ex.Message; }
	}
	[RelayCommand]
	private void DeleteProfile()
	{
		if (!RequireStore() || !CanEditSettings || SelectedProfile is null) return;
		try
		{
			var history = _store!.Search("", SelectedProfile.Id);
			if (_store.SearchIssues.Count > 0)
				throw new InvalidOperationException("存在损坏或不兼容历史，无法确认归属，禁止删除 Agent。请先修复历史缓存：" +
					string.Join("；", _store.SearchIssues.Select(i => i.FileName + "：" + i.Message)));
			if (history.Count > 0) throw new InvalidOperationException("请先逐个删除此 Agent 的历史会话。");
			if (Profiles.Count < 2) throw new InvalidOperationException("至少保留一个 Agent。");
			var next = Profiles.Where(p => p.Id != SelectedProfile.Id).ToArray();
			_store.SaveProfiles(next, next[0].Id);
			Profiles.Clear(); foreach (var p in next) Profiles.Add(p); SelectedProfile = Profiles[0];
			Status = "Agent 已删除。";
		}
		catch (Exception ex) { Status = "删除失败：" + ex.Message; }
	}
	private void RefreshHistory()
	{
		if (!RequireStore()) return;
		try
		{
			var history = _store!.Search(Search ?? "", SelectedProfile?.Id);
			Sessions.Clear(); foreach (var s in history) Sessions.Add(s);
			HistoryWarning = _store.SearchIssues.Count == 0 ? "" : "警告：部分历史缓存无法加载，已隔离；无法确认归属，禁止删除 Agent。\n" +
				string.Join("\n", _store.SearchIssues.Select(i => i.FileName + "：" + i.Message));
			if (HistoryWarning.Length > 0) Status = HistoryWarning;
		}
		catch (Exception ex) { HistoryWarning = Status = "历史缓存读取失败：" + ex.Message; }
	}
	private bool RequireStore()
	{
		if (_store is not null) return true;
		Status = "本地缓存不可用：无法保存、新增、删除或恢复配置/会话；不会覆盖损坏数据。请修复缓存后重启。";
		return false;
	}
	[RelayCommand] private void RefreshSessions() => RefreshHistory();
	[RelayCommand]
	private void RestoreBoundProfile()
	{
		if (!RequireStore() || !CanEditSettings || SelectedSession is null) return;
		try
		{
			var session = _store!.Load(SelectedSession.Id);
			var original = session.ProfileSnapshot ?? throw new InvalidOperationException("此会话没有绑定配置快照，请手工恢复原配置。");
			PlatformStore.ValidateBinding(session, original);
			var next = Profiles.Where(p => p.Id != original.Id).Append(original).ToList();
			_store.SaveProfiles(next, original.Id);
			Profiles.Clear(); foreach (var p in next) Profiles.Add(p);
			SelectedProfile = original;
			SelectedSession = session;
			Status = "已用此历史快照替换该 Agent 当前配置（只含密钥变量名）。其他历史需分别恢复其原配置；请点击恢复上下文。";
		}
		catch (Exception ex) { Status = "恢复绑定配置失败：" + ex.Message; }
	}
	[RelayCommand]
	private void RenameSession()
	{
		if (!RequireStore() || IsBusy || SelectedSession is null) return;
		try
		{
			_store!.Rename(SelectedSession.Id, SessionName);
			if (_current?.Id == SelectedSession.Id) _current = _current with { Name = SessionName.Trim() };
			RefreshHistory(); Status = "历史已重命名。";
		}
		catch (Exception ex) { Status = "重命名失败：" + ex.Message; }
	}
	[RelayCommand]
	private void DeleteSession()
	{
		if (!RequireStore() || IsBusy || SelectedSession is null) return;
		try
		{
			if (_current?.Id == SelectedSession.Id) throw new InvalidOperationException("请先断开此会话，再删除。");
			_store!.Delete(SelectedSession.Id); RefreshHistory(); Status = "历史已永久删除。";
		}
		catch (Exception ex) { Status = "删除失败：" + ex.Message; }
	}

	public ObservableCollection<HarnessApproval> Approvals { get; } = [];

	[ObservableProperty] public partial string Endpoint { get; set; } = "";
	[ObservableProperty] public partial string Model { get; set; } = "";
	[ObservableProperty] public partial string KeyEnvironmentVariable { get; set; } = "";
	[ObservableProperty] public partial string ScriptsDirectory { get; set; } = "";
	[ObservableProperty] public partial string Input { get; set; } = "";
	[ObservableProperty] public partial string Conversation { get; set; } = "";
	[ObservableProperty] public partial string TaskLog { get; set; } = "";
	[ObservableProperty] public partial string Status { get; set; } = "请配置模型服务并创建会话";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanConnect))]
	[NotifyPropertyChangedFor(nameof(CanEditSettings))]
	[NotifyPropertyChangedFor(nameof(CanSend))]
	[NotifyPropertyChangedFor(nameof(CanDecide))]
	[NotifyPropertyChangedFor(nameof(CanCancel))]
	[NotifyPropertyChangedFor(nameof(CanDisconnect))]
	[NotifyPropertyChangedFor(nameof(CanRevokeSessionApproval))]
	[NotifyCanExecuteChangedFor(nameof(AllowOnceCommand))]
	[NotifyCanExecuteChangedFor(nameof(AllowSessionCommand))]
	[NotifyCanExecuteChangedFor(nameof(DenyToolCommand))]
	public partial bool IsBusy { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanConnect))]
	[NotifyPropertyChangedFor(nameof(CanEditSettings))]
	[NotifyPropertyChangedFor(nameof(CanSend))]
	[NotifyPropertyChangedFor(nameof(CanCancel))]
	[NotifyPropertyChangedFor(nameof(CanDisconnect))]
	public partial bool IsConnected { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanSend))]
	[NotifyPropertyChangedFor(nameof(CanDecide))]
	[NotifyPropertyChangedFor(nameof(CanCancel))]
	public partial bool HasApproval { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ApprovalDetails))]
	[NotifyPropertyChangedFor(nameof(CanDecide))]
	public partial HarnessApproval? SelectedApproval { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanRevokeSessionApproval))]
	[NotifyPropertyChangedFor(nameof(ApprovalModeText))]
	public partial bool SessionApprovalEnabled { get; set; }
	public string ApprovalModeText => SessionApprovalEnabled ? ChatText.SessionApproval : ChatText.DefaultApproval;

	public bool CanConnect => _store is not null && !IsBusy && !IsConnected && !_closing;
	public bool CanEditSettings => _store is not null && !IsBusy && !IsConnected && !_closing;
	public bool CanSend => IsConnected && !IsBusy && !HasApproval && !_closing && _workspace?.State == HarnessState.Ready;
	public bool CanDecide => !IsBusy && !_closing && SelectedApproval is not null;
	public bool CanCancel => (IsConnected && IsBusy) || HasApproval || (_workspace?.HasBackgroundWork == true);
	public bool CanDisconnect => IsConnected && !IsBusy && !_closing;
	public bool CanRevokeSessionApproval => SessionApprovalEnabled && !IsBusy && !_closing;
	public string ApprovalDetails => SelectedApproval is { } item
		? $"工具：{item.Task}\n目录：{item.PublishRootDirectory}" +
		  (string.IsNullOrEmpty(item.CurrentVersion) ? "" : $"\n当前版本：{item.CurrentVersion}\n目标版本：{item.NextVersion}\n地址：{item.OssHost}") + $"\n\n{item.Details}"
		: "没有待审批的工具请求。";

	[RelayCommand]
	private async Task ConnectAsync()
		=> await OpenAsync(false);
	[RelayCommand]
	private async Task RestoreSessionAsync()
		=> await OpenAsync(true);
	private async Task OpenAsync(bool restore)
	{
		if (!RequireStore() || !CanConnect) return;
		if (restore && SelectedSession is null) { Status = "请选择历史会话。"; return; }
		var selectedHistory = SelectedSession;
		if (!TrySaveProfile()) return;
		IsBusy = true;
		Status = "正在创建 Harness 会话";
		try
		{
			var profile = CaptureProfile();
			var stored = restore ? _store!.Load(selectedHistory!.Id) : null;
			if (stored is not null) PlatformStore.ValidateBinding(stored, profile);
			var settings = new HarnessSettings(profile.Endpoint, profile.Model, profile.KeyEnvironmentVariable, profile.ScriptsDirectory, profile);
			_workspace = new HarnessWorkspace(settings, line => Dispatcher.UIThread.Invoke(() => AppendTaskLog(line)));
			if (stored is not null) await _workspace.RestoreAsync(stored, profile);
			else await _workspace.InitializeAsync();
			_current = stored ?? new StoredConversation { ProfileId = profile.Id, Binding = profile.Binding, ProfileSnapshot = profile, Name = SessionName };
			_restoredUnresolved = stored?.UnsafeToResume == true;
			_transcript.Clear().Append(stored?.Transcript ?? "");
			_localLog.Clear().Append(stored?.Log ?? "");
			Conversation = TrimLog(stored?.Transcript ?? ""); TaskLog = TrimLog(stored?.Log ?? "");
			ChatItems.Clear();
			_assistantEntry = null;
			SessionApprovalEnabled = false;
			if (stored?.Messages is { } messages)
				foreach (var message in messages) ChatItems.Add(new ChatEntryViewModel(this, message.Role, message.Title, message.Text));
			else if (!string.IsNullOrEmpty(stored?.Transcript))
				ChatItems.Add(new ChatEntryViewModel(this, "notice", ChatText.History, stored.Transcript));
			IsConnected = true;
			await SaveCurrentAsync();
			Status = stored?.UnsafeToResume == true
				? "历史已恢复；未完成审批/授权与背景任务已失效，下一轮 Harness 拒绝旧审批/标记旧背景任务 lost，绝不自动恢复执行。"
				: "持久会话已就绪；发送消息时连接模型服务。";
		}
		catch (Exception ex)
		{
			_workspace?.Dispose();
			_workspace = null;
			IsConnected = false;
			Status = $"打开会话失败：{ex.Message}";
		}
		finally { IsBusy = false; OnPropertyChanged(nameof(CanSend)); }
	}

	[RelayCommand]
	private async Task SendAsync()
	{
		if (!CanSend || _workspace is null) return;
		var message = Input?.Trim() ?? "";
		if (message.Length == 0) { Status = "请输入消息"; return; }
		Input = "";
		ChatItems.Add(new ChatEntryViewModel(this, "user", ChatText.You, _workspace.RedactLocal(message)));
		_assistantEntry = null;
		AppendTranscript($"\n\n你：{message}\n\nAgent：");
		await RunAsync(() => _workspace.SendAsync(message, StreamText));
	}
	[RelayCommand] private Task PlanModeAsync() => ChangeModeAsync("plan");
	[RelayCommand] private Task ExecuteModeAsync() => ChangeModeAsync("execute");
	private async Task ChangeModeAsync(string mode)
	{
		if (!CanSend || _workspace is null) return;
		IsBusy = true;
		try
		{
			await _workspace.SetModeAsync(mode);
			AppendConversation("\n[用户切换 Harness 模式：" + mode + "]\n");
			await SaveCurrentAsync();
			Status = "Harness 模式已切换并保存：" + mode;
		}
		catch (Exception ex) { Status = "模式切换/保存失败：" + ex.Message; }
		finally { IsBusy = false; }
	}

	[RelayCommand]
	private Task ApproveAsync() => DecideAsync(true);

	[RelayCommand(CanExecute = nameof(CanDecideCard))]
	private Task AllowOnceAsync(ChatEntryViewModel entry) => DecideCardAsync(entry, true, false);
	[RelayCommand(CanExecute = nameof(CanDecideCard))]
	private Task AllowSessionAsync(ChatEntryViewModel entry) => DecideCardAsync(entry, true, true);
	[RelayCommand(CanExecute = nameof(CanDecideCard))]
	private Task DenyToolAsync(ChatEntryViewModel entry) => DecideCardAsync(entry, false, false);
	private bool CanDecideCard(ChatEntryViewModel? entry) => !IsBusy && !_closing && entry?.IsPendingApproval == true &&
		Approvals.Any(a => a.Id == entry.Approval?.Id);

	private async Task DecideCardAsync(ChatEntryViewModel entry, bool approved, bool session)
	{
		if (!CanDecideCard(entry) || _workspace is null || entry.Approval is not { } approval) return;
		var deciding = ChatItems.Where(c => c.IsPendingApproval && (session || c.Approval?.Id == approval.Id)).ToArray();
		foreach (var card in deciding)
		{
			card.Resolve(session ? ChatText.AllowedSession : approved ? ChatText.AllowedOnce : ChatText.Rejected);
			var pending = Approvals.Single(a => a.Id == card.Approval!.Id);
			Approvals.Remove(pending);
		}
		SelectedApproval = Approvals.FirstOrDefault();
		HasApproval = Approvals.Count > 0;
		_assistantEntry = null;
		AppendTranscript($"\n\n[{entry.DecisionText}：{approval.Task}]\nAgent：");
		await RunAsync(() => session ? _workspace.ApproveSessionAsync(approval.Id, StreamText) : _workspace.DecideAsync(approval.Id, approved, StreamText));
	}

	[RelayCommand]
	private async Task ApproveSessionAsync()
	{
		if (!CanDecide || _workspace is null || SelectedApproval is null) return;
		var entry = ChatItems.FirstOrDefault(c => c.Approval?.Id == SelectedApproval.Id);
		if (entry is not null) await DecideCardAsync(entry, true, true);
	}

	[RelayCommand]
	private void RevokeSessionApproval()
	{
		if (!CanRevokeSessionApproval || _workspace is null) return;
		_workspace.RevokeSessionApproval();
		SessionApprovalEnabled = false;
		AppendConversation("\n[已撤销当前会话的自动授权，后续请求需要审批。]\n");
		Status = "已撤销当前会话自动授权。";
	}

	[RelayCommand]
	private Task RejectAsync() => DecideAsync(false);

	private async Task DecideAsync(bool approved)
	{
		if (!CanDecide || _workspace is null || SelectedApproval is null) return;
		var entry = ChatItems.FirstOrDefault(c => c.Approval?.Id == SelectedApproval.Id);
		if (entry is not null) await DecideCardAsync(entry, approved, false);
	}

	private async Task RunAsync(Func<Task<HarnessResult>> action)
	{
		IsBusy = true;
		Status = "Agent 正在处理；可取消任务";
		try
		{
			var running = action();
			SessionApprovalEnabled = _workspace?.ApproveForSession == true;
			_operation = running;
			var result = await running;
			_restoredUnresolved = false;
			Approvals.Clear();
			foreach (var approval in result.Approvals)
			{
				Approvals.Add(approval);
				if (!ChatItems.Any(c => c.Approval?.Id == approval.Id))
					ChatItems.Add(new ChatEntryViewModel(this, "approval", approval.Task,
						_workspace!.RedactLocal(string.Format(ChatText.Parameters, approval.PublishRootDirectory, approval.CurrentVersion,
							approval.NextVersion, approval.OssHost, approval.Details)), approval));
			}
			SelectedApproval = Approvals.Count > 0 ? Approvals[0] : null;
			HasApproval = Approvals.Count > 0;
			Status = HasApproval ? "等待逐项人工审批" : "Agent 已完成当前轮次";
		}
		catch (OperationCanceledException)
		{
			ExpireApprovalCards();
			Approvals.Clear(); SelectedApproval = null; HasApproval = false;
			Status = "已取消。已执行的文件/终端/发布操作可能已产生副作用；请检查本地文件及日志，再断开/恢复会话。";
			AppendConversation("\n[已取消；未完成授权不会恢复自动执行。]\n");
		}
		catch (Exception ex)
		{
			ExpireApprovalCards();
			Approvals.Clear(); SelectedApproval = null; HasApproval = false;
			Status = $"Agent 任务失败：{ex.Message}。请断开并重新创建会话。";
			AppendConversation("\n[任务失败：" + ex.Message + "]\n");
		}
		finally
		{
			SessionApprovalEnabled = _workspace?.ApproveForSession == true;
			await Dispatcher.UIThread.InvokeAsync(() => { });
			try { await SaveCurrentAsync(); }
			catch (Exception ex) { Status = "本地历史保存失败：" + ex.Message + "；请勿关闭应用，重试断开以保存。"; }
			_operation = null;
			IsBusy = false; OnPropertyChanged(nameof(CanSend));
		}
	}

	[RelayCommand]
	private void Cancel()
	{
		if (_workspace is null) return;
		_workspace.Cancel();
		ExpireApprovalCards();
		SessionApprovalEnabled = false;
		if (!IsBusy)
		{
			Approvals.Clear(); SelectedApproval = null; HasApproval = false;
			Status = "已取消待审批请求，请断开并重新创建会话。";
			OnPropertyChanged(nameof(CanSend));
		}
	}

	[RelayCommand]
	private async Task DisconnectAsync()
	{
		if (!CanDisconnect || _workspace is null) return;
		try
		{
			if (_workspace.HasBackgroundWork) _restoredUnresolved = true;
			await _workspace.StopBackgroundAsync(); await SaveCurrentAsync();
		}
		catch (Exception ex) { Status = "保存失败，未断开：" + ex.Message; return; }
		_workspace.Dispose();
		ExpireApprovalCards();
		_workspace = null;
		SessionApprovalEnabled = false;
		_current = null;
		Approvals.Clear(); SelectedApproval = null; HasApproval = false;
		IsConnected = false;
		Status = "已断开；历史已保留，可选择恢复或新建。";
	}

	private async Task SaveCurrentAsync()
	{
		if (_workspace is null || _current is null) return;
		if (!RequireStore()) throw new InvalidOperationException(Status);
		var state = await _workspace.SerializeAsync();
		var unsafeState = _workspace.State != HarnessState.Ready || _restoredUnresolved || _workspace.HasBackgroundWork;
		var transcript = _workspace.RedactLocal(_transcript.ToString());
		var log = _workspace.RedactLocal(_localLog.ToString());
		_transcript.Clear().Append(transcript); _localLog.Clear().Append(log);
		Conversation = TrimLog(transcript); TaskLog = TrimLog(log);
		_current = _current with { Session = state, Transcript = transcript, Log = log,
			Messages = ChatItems.Select(c => c.Snapshot()).Select(m => m with { Title = _workspace.RedactLocal(m.Title), Text = _workspace.RedactLocal(m.Text) }).ToArray(),
			UnsafeToResume = unsafeState, Updated = DateTimeOffset.UtcNow };
		_store!.Save(_current);
		HarnessInfo = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
		if (state.TryGetProperty("stateBag", out var bag))
		{
			ModeText = bag.TryGetProperty("AgentModeProvider", out var mode) && mode.TryGetProperty("currentMode", out var value)
				? value.GetString() ?? "未初始化" : "未初始化";
			TodoText = bag.TryGetProperty("TodoProvider", out var todo) && todo.TryGetProperty("items", out var items) && items.GetArrayLength() > 0
				? string.Join(Environment.NewLine, items.EnumerateArray().Select(item =>
					(item.TryGetProperty("isComplete", out var done) && done.GetBoolean() ? "✓ " : "□ ") +
					(item.TryGetProperty("title", out var title) ? title.GetString() : item.GetRawText())))
				: "暂无 todo；模型可使用真实 Harness todos 工具管理任务。";
		}
		RefreshHistory();
	}
	public async Task<bool> CloseAsync()
	{
		_closing = true;
		_workspace?.Cancel();
		ExpireApprovalCards();
		while (IsBusy) await Task.Delay(30);
		try
		{
			if (_workspace is not null) await _workspace.StopBackgroundAsync();
			await SaveCurrentAsync();
			_workspace?.Dispose(); _workspace = null;
			return true;
		}
		catch (Exception ex) { Status = "关闭时保存失败，窗口保留：" + ex.Message; _closing = false; return false; }
	}

	private void StreamText(string text) => Dispatcher.UIThread.Invoke(() =>
	{
		if (_assistantEntry is null)
		{
			_assistantEntry = new ChatEntryViewModel(this, "assistant", ChatText.Agent, "");
			ChatItems.Add(_assistantEntry);
		}
		_assistantEntry.Text = _workspace?.RedactLocal(_assistantEntry.Text + text) ?? (_assistantEntry.Text + text);
		AppendTranscript(text);
	});
	private void AppendConversation(string text)
	{
		_assistantEntry = null;
		ChatItems.Add(new ChatEntryViewModel(this, "notice", ChatText.Notice, _workspace?.RedactLocal(text.Trim()) ?? text.Trim()));
		AppendTranscript(text);
	}
	private void ExpireApprovalCards()
	{
		foreach (var entry in ChatItems.Where(c => c.IsPendingApproval)) entry.Resolve(ChatText.Expired);
	}
	private void AppendTranscript(string text)
	{
		text = _workspace?.RedactLocal(text) ?? text;
		_transcript.Append(text); Conversation = TrimLog(Conversation + text);
	}
	private void AppendTaskLog(string line)
	{
		line = (_workspace?.RedactLocal(line) ?? line) + Environment.NewLine;
		_localLog.Append(line); TaskLog = TrimLog(TaskLog + line);
	}
	private static string TrimLog(string text) => text.Length <= 200_000 ? text : text[^200_000..];
	private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or
		InvalidOperationException or ArgumentException or NotSupportedException or HttpRequestException or
		ClientResultException or JsonException or System.ComponentModel.Win32Exception;
}
