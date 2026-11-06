using System.Globalization;
using System.Resources;

namespace ClientPublishAgent.Resources;

internal static class ScriptText
{
	private static readonly ResourceManager Manager = new("ClientPublishAgent.Resources.ScriptText", typeof(ScriptText).Assembly);
	private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? throw new MissingManifestResourceException(name);
	public static string DirectoryMissing => Get(nameof(DirectoryMissing));
	public static string FileCount => Get(nameof(FileCount));
	public static string LoadFailed => Get(nameof(LoadFailed));
	public static string FileTooLarge => Get(nameof(FileTooLarge));
	public static string Saved => Get(nameof(Saved));
	public static string SaveFailed => Get(nameof(SaveFailed));
	public static string Loaded => Get(nameof(Loaded));
	public static string InvalidPath => Get(nameof(InvalidPath));
	public static string LinkedPath => Get(nameof(LinkedPath));
}
