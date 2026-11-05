namespace ClientPublishAgent.Services;

public sealed record PackagingTask(string Name, string ScriptName, string? PluginId = null, string Runtime = "")
{
	public override string ToString() => Name;
}
