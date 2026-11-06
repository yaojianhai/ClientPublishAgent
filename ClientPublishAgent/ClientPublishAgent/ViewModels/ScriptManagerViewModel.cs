using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using ClientPublishAgent.Resources;
using CommunityToolkit.Mvvm.Input;

namespace ClientPublishAgent.ViewModels;

public partial class ScriptManagerViewModel : ViewModelBase
{
	private const int MaxScriptBytes = 1_048_576;
	private static readonly string[] EditableExtensions = [".ps1", ".bat", ".cmd", ".json", ".nsi", ".nsh", ".md", ".xml", ".config", ".props", ".targets", ".csproj", ".ini", ".txt", ".cpp", ".rtf"];
	private readonly string _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Scripts"));
	private Encoding _encoding = new UTF8Encoding(false, true);
	private bool _loaded;

	public ObservableCollection<string> ScriptFiles { get; } = [];
	public ObservableCollection<ScriptTreeNode> ScriptTree { get; } = [];
	private string? _selectedScript;
	private ScriptTreeNode? _selectedTreeNode;
	private string _scriptContent = "";
	private string _status = "";

	public string ScriptsRoot => _root;
	public bool CanSave => _loaded;
	public ScriptTreeNode? SelectedTreeNode
	{
		get => _selectedTreeNode;
		set
		{
			if (SetProperty(ref _selectedTreeNode, value) && value is { IsDirectory: false, RelativePath: not null })
				SelectedScript = value.RelativePath;
		}
	}
	public string? SelectedScript
	{
		get => _selectedScript;
		set
		{
			if (SetProperty(ref _selectedScript, value)) OnSelectedScriptChanged(value);
		}
	}
	public string ScriptContent { get => _scriptContent; set => SetProperty(ref _scriptContent, value); }
	public string Status { get => _status; set => SetProperty(ref _status, value); }

	public ScriptManagerViewModel() => RefreshScripts();

	private void OnSelectedScriptChanged(string? newValue)
	{
		_loaded = false;
		OnPropertyChanged(nameof(CanSave));
		if (string.IsNullOrWhiteSpace(newValue))
		{
			ScriptContent = "";
			return;
		}
		LoadScript(newValue);
	}

	[RelayCommand]
	private void RefreshScripts()
	{
		var selected = SelectedScript;
		ScriptFiles.Clear();
		ScriptTree.Clear();
		SelectedScript = null;
		if (!Directory.Exists(_root))
		{
			Status = string.Format(ScriptText.DirectoryMissing, _root);
			return;
		}
		try
		{
			if ((File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException(ScriptText.LinkedPath);
			Visit(_root);
			BuildTree(_root, ScriptTree);
			if (selected is not null && ScriptFiles.Contains(selected, StringComparer.Ordinal)) SelectedScript = selected;
			if (SelectedScript is null) Status = string.Format(ScriptText.FileCount, ScriptFiles.Count, _root);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Status = string.Format(ScriptText.LoadFailed, ex.Message);
		}
	}

	private void BuildTree(string directory, ObservableCollection<ScriptTreeNode> destination)
	{
		foreach (var subdirectory in Directory.EnumerateDirectories(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
		{
			if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0) continue;
			var folder = new ScriptTreeNode(Path.GetFileName(subdirectory), null, true);
			BuildTree(subdirectory, folder.Children);
			if (folder.Children.Count > 0) destination.Add(folder);
		}
		foreach (var file in Directory.EnumerateFiles(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
		{
			if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || !EditableExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
			var info = new FileInfo(file);
			if (info.Length <= MaxScriptBytes)
			{
				var relative = Path.GetRelativePath(_root, file);
				destination.Add(new ScriptTreeNode(Path.GetFileName(file), relative, false));
			}
		}
	}

	[RelayCommand]
	private void SaveScript()
	{
		if (!CanSave || SelectedScript is null) return;
		string? temporary = null;
		try
		{
			var path = ResolveSafePath(SelectedScript);
			WorkspaceBoundary(path);
			var bytes = _encoding.GetBytes(ScriptContent);
			var preamble = _encoding.GetPreamble();
			if ((long)bytes.Length + preamble.Length > MaxScriptBytes) throw new InvalidDataException(ScriptText.FileTooLarge);
			temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
			using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				stream.Write(preamble);
				stream.Write(bytes);
			}
			if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
			WorkspaceBoundary(path);
			File.Move(temporary, path, true);
			temporary = null;
			Status = string.Format(ScriptText.Saved, SelectedScript);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
		{
			Status = string.Format(ScriptText.SaveFailed, ex.Message);
		}
		finally
		{
			if (temporary is not null)
			{
				try { File.Delete(temporary); }
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					Status = string.Format(ScriptText.SaveFailed, ex.Message);
				}
			}
		}
	}

	private void LoadScript(string relative)
	{
		try
		{
			var path = ResolveSafePath(relative);
			WorkspaceBoundary(path);
			var info = new FileInfo(path);
			if (info.Length > MaxScriptBytes) throw new InvalidDataException(ScriptText.FileTooLarge);
			var bytes = File.ReadAllBytes(path);
			if (bytes.Length > MaxScriptBytes) throw new InvalidDataException(ScriptText.FileTooLarge);
			(_encoding, var preamble) = DetectEncoding(bytes);
			ScriptContent = _encoding.GetString(bytes, preamble, bytes.Length - preamble);
			_loaded = true;
			OnPropertyChanged(nameof(CanSave));
			Status = string.Format(ScriptText.Loaded, relative);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or DecoderFallbackException)
		{
			ScriptContent = "";
			Status = string.Format(ScriptText.LoadFailed, ex.Message);
		}
	}

	private static (Encoding Encoding, int Preamble) DetectEncoding(byte[] bytes)
	{
		if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)) return (new UTF8Encoding(true, true), Encoding.UTF8.Preamble.Length);
		if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble)) return (new UnicodeEncoding(false, true, true), Encoding.Unicode.Preamble.Length);
		if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble)) return (new UnicodeEncoding(true, true, true), Encoding.BigEndianUnicode.Preamble.Length);
		try
		{
			var utf8 = new UTF8Encoding(false, true);
			_ = utf8.GetString(bytes);
			return (utf8, 0);
		}
		catch (DecoderFallbackException)
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			var systemEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage,
				EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
			_ = systemEncoding.GetString(bytes);
			return (systemEncoding, 0);
		}
	}

	private void Visit(string directory)
	{
		foreach (var file in Directory.EnumerateFiles(directory))
		{
			if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || !EditableExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
			var info = new FileInfo(file);
			if (info.Length <= MaxScriptBytes) ScriptFiles.Add(Path.GetRelativePath(_root, file));
		}
		foreach (var subdirectory in Directory.EnumerateDirectories(directory))
		{
			if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0) Visit(subdirectory);
		}
	}

	private string ResolveSafePath(string relative)
	{
		if (Path.IsPathRooted(relative)) throw new UnauthorizedAccessException(ScriptText.InvalidPath);
		var path = Path.GetFullPath(Path.Combine(_root, relative));
		var prefix = Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar;
		if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
			!EditableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
			throw new UnauthorizedAccessException(ScriptText.InvalidPath);
		return path;
	}

	private void WorkspaceBoundary(string path)
	{
		for (var current = path; current is not null; current = Path.GetDirectoryName(current))
		{
			if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException(ScriptText.LinkedPath);
			if (string.Equals(current, _root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
		}
		throw new UnauthorizedAccessException(ScriptText.InvalidPath);
	}
}
