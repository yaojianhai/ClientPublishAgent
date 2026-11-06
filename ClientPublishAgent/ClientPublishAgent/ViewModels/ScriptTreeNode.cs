using System.Collections.ObjectModel;

namespace ClientPublishAgent.ViewModels;

public sealed class ScriptTreeNode(string name, string? relativePath, bool isDirectory)
{
	public string Name { get; } = name;
	public string Icon => IsDirectory ? "📁" : "📄";
	public string? RelativePath { get; } = relativePath;
	public bool IsDirectory { get; } = isDirectory;
	public ObservableCollection<ScriptTreeNode> Children { get; } = [];
}
