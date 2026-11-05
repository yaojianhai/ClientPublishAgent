using System.Globalization;
using System.Resources;

namespace ClientPublishAgent.Resources;

internal static class ChatText
{
	private static readonly ResourceManager Manager = new("ClientPublishAgent.Resources.ChatText", typeof(ChatText).Assembly);
	private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? throw new MissingManifestResourceException(name);
	public static string Chat => Get(nameof(Chat));
	public static string You => Get(nameof(You));
	public static string Agent => Get(nameof(Agent));
	public static string Notice => Get(nameof(Notice));
	public static string History => Get(nameof(History));
	public static string AllowOnce => Get(nameof(AllowOnce));
	public static string AllowSession => Get(nameof(AllowSession));
	public static string Reject => Get(nameof(Reject));
	public static string AllowedOnce => Get(nameof(AllowedOnce));
	public static string AllowedSession => Get(nameof(AllowedSession));
	public static string Rejected => Get(nameof(Rejected));
	public static string Expired => Get(nameof(Expired));
	public static string Waiting => Get(nameof(Waiting));
	public static string ToolRequest => Get(nameof(ToolRequest));
	public static string ToolDetails => Get(nameof(ToolDetails));
	public static string SessionWarning => Get(nameof(SessionWarning));
	public static string Revoke => Get(nameof(Revoke));
	public static string DefaultApproval => Get(nameof(DefaultApproval));
	public static string SessionApproval => Get(nameof(SessionApproval));
	public static string Thinking => Get(nameof(Thinking));
	public static string EmptyTitle => Get(nameof(EmptyTitle));
	public static string EmptyHint => Get(nameof(EmptyHint));
	public static string InputHint => Get(nameof(InputHint));
	public static string InputKeys => Get(nameof(InputKeys));
	public static string Send => Get(nameof(Send));
	public static string Stop => Get(nameof(Stop));
	public static string Latest => Get(nameof(Latest));
	public static string Logs => Get(nameof(Logs));
	public static string LogHint => Get(nameof(LogHint));
	public static string ScriptInputHint => Get(nameof(ScriptInputHint));
	public static string ScriptInputPlaceholder => Get(nameof(ScriptInputPlaceholder));
	public static string ScriptInputSend => Get(nameof(ScriptInputSend));
	public static string ScriptInputError => Get(nameof(ScriptInputError));
	public static string Parameters => Get(nameof(Parameters));
}
