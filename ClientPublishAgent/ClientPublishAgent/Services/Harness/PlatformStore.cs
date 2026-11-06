using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net;

namespace ClientPublishAgent.Services.Harness;

public sealed record AgentProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Personal Assistant";
    public string Instructions { get; init; } = "You are a general personal assistant. Reply in the user's language. Never claim tool success without evidence.";
    public string Endpoint { get; init; } = "";
    public string Model { get; init; } = "";
    public string KeyEnvironmentVariable { get; init; } = "";
    public string WorkspaceRoot { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string ScriptsDirectory { get; init; } = "";
    public string MarketServerRoot { get; init; } = "";
    public bool Files { get; init; }
    public bool Terminal { get; init; }
    public bool Packaging { get; init; }
    public bool FileMemory { get; init; }
    public bool Skills { get; init; }
    public string MemoryDirectory { get; init; } = ".agent-memory";
    public string SkillsDirectory { get; init; } = ".agent-skills";
    public bool BackgroundResearch { get; init; }
    public bool Telemetry { get; init; }
    public bool Compaction { get; init; }
    public int ContextTokens { get; init; } = 32000;
    public int OutputTokens { get; init; } = 4096;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoMemory { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoSkills { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool WebSearch { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchEndpoint { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Binding => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(this, PlatformJsonContext.Default.AgentProfile))));
    public override string ToString() => Name;
}

public sealed record StoredConversation
{
    public int Version { get; init; } = 1;
    public string HarnessVersion { get; init; } = "1.27.0";
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProfileId { get; init; }
    public string Binding { get; init; } = "";
    public AgentProfile? ProfileSnapshot { get; init; }
    public string Name { get; init; } = "New conversation";
    public DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;
    public JsonElement Session { get; init; }
    public string Transcript { get; init; } = "";
    public IReadOnlyList<StoredChatMessage>? Messages { get; init; }
    public string Log { get; init; } = "";
    public bool UnsafeToResume { get; init; }
    public override string ToString() => $"{Name} · {Updated.LocalDateTime:g}" + (UnsafeToResume ? " · 审批已失效" : "");
}

public sealed record SessionSearchIssue(string FileName, string Message);
public sealed record StoredChatMessage(string Role, string Title, string Text);

public sealed class PlatformStore
{
    private readonly string _root;
    public string Root => _root;
    public Guid? LastSelectedProfileId { get; private set; }
    public IReadOnlyList<SessionSearchIssue> SearchIssues { get; private set; } = [];
    public PlatformStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalAgentPlatform"));
        WorkspaceBoundary.CheckNoLinks(_root);
        Directory.CreateDirectory(_root);
        WorkspaceBoundary.CheckNoLinks(_root);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public List<AgentProfile> LoadProfiles()
    {
        var path = Path.Combine(_root, "profiles.json");
        if (!File.Exists(path)) return [new AgentProfile()];
        var data = JsonSerializer.Deserialize(File.ReadAllText(path), PlatformJsonContext.Default.ProfilesDocument) ?? throw new InvalidDataException("Invalid profiles cache.");
        if (data.Version is not (1 or 2)) throw new InvalidDataException("Unsupported profiles cache version.");
        if (data.Profiles.Count == 0 || data.Profiles.Select(p => p.Id).Distinct().Count() != data.Profiles.Count)
            throw new InvalidDataException("Invalid profile identities.");
        LastSelectedProfileId = data.SelectedProfileId;
        return data.Profiles;
    }
    public void SaveProfiles(IEnumerable<AgentProfile> profiles, Guid? selectedProfileId = null)
    {
        var list = profiles.ToList();
        if (list.Count == 0 || list.Select(p => p.Id).Distinct().Count() != list.Count) throw new ArgumentException("Profile identities must be nonempty and unique.");
        foreach (var profile in list) ValidateProfile(profile);
        var selected = selectedProfileId ?? LastSelectedProfileId ?? list.FirstOrDefault()?.Id;
        foreach (var profile in list)
        {
            var secret = ReadKey(profile.KeyEnvironmentVariable);
            if (!string.IsNullOrEmpty(secret) && JsonSerializer.SerializeToElement(profile, PlatformJsonContext.Default.AgentProfile).EnumerateObject().Any(p =>
                p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Contains(secret, StringComparison.Ordinal)))
                throw new InvalidOperationException("配置中出现密钥值，拒绝保存。仅填写密钥环境变量名。");
        }
        AtomicWrite(Path.Combine(_root, "profiles.json"), new ProfilesDocument(2, list, selected), PlatformJsonContext.Default.ProfilesDocument);
        LastSelectedProfileId = selected;
    }
    public static void ValidateProfile(AgentProfile profile)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile identity/name required.");
        if (profile.KeyEnvironmentVariable.Length > 128 || (profile.KeyEnvironmentVariable.Length > 0 &&
            !Regex.IsMatch(profile.KeyEnvironmentVariable, @"^[A-Za-z_][A-Za-z0-9_]*$")))
            throw new ArgumentException("密钥只能填写环境变量名，不能填写密钥值。");
        if (profile.Endpoint.Length > 0 && (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip)))))
            throw new ArgumentException("Endpoint must be HTTPS or loopback HTTP, without credentials/query/fragment.");
        if (!Path.IsPathFullyQualified(profile.WorkspaceRoot)) throw new ArgumentException("Workspace root must be absolute.");
        if (profile.FileMemory || profile.AutoMemory) WorkspaceBoundary.Resolve(profile.WorkspaceRoot, profile.MemoryDirectory);
        if (profile.Skills || profile.AutoSkills) WorkspaceBoundary.Resolve(profile.WorkspaceRoot, profile.SkillsDirectory);
        if (profile.SearchEndpoint is not null) SearxSearch.ValidateEndpoint(profile.SearchEndpoint);
        if (profile.WebSearch && profile.SearchEndpoint is null) throw new ArgumentException("Configure a SearXNG JSON search endpoint first.");
        var key = ReadKey(profile.KeyEnvironmentVariable);
        if (!string.IsNullOrEmpty(key) && JsonSerializer.SerializeToElement(profile, PlatformJsonContext.Default.AgentProfile).EnumerateObject().Any(p =>
            p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Contains(key, StringComparison.Ordinal)))
            throw new InvalidOperationException("配置中出现密钥值，拒绝保存或绑定。仅填写密钥环境变量名。");
    }
    public static string? ReadKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(key) && OperatingSystem.IsWindows())
            key = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
        return key;
    }
    public static string Redact(string text, string? key) =>
        string.IsNullOrEmpty(key) ? text : text.Replace(key, "[REDACTED API KEY]", StringComparison.Ordinal);
    public static JsonElement RedactState(JsonElement state, string? key)
    {
        if (string.IsNullOrEmpty(key)) return state.Clone();
        JsonNode? Scrub(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var property in obj.ToArray())
                {
                    var replacement = Scrub(property.Value);
                    if (!ReferenceEquals(replacement, property.Value)) obj[property.Key] = replacement;
                }
            else if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++)
                {
                    var replacement = Scrub(array[i]);
                    if (!ReferenceEquals(replacement, array[i])) array[i] = replacement;
                }
            else if (node is JsonValue value && value.TryGetValue<string>(out var text))
                return JsonValue.Create(Redact(text, key));
            return node;
        }
        return JsonSerializer.SerializeToElement(Scrub(JsonNode.Parse(state.GetRawText())), PlatformJsonContext.Default.JsonNode);
    }
    public void Save(StoredConversation conversation)
    {
        if (conversation.Session.ValueKind != JsonValueKind.Object) throw new InvalidDataException("A real serialized AgentSession is required.");
        if (conversation.ProfileSnapshot is { } snapshot)
        {
            ValidateProfile(snapshot);
            ValidateBinding(conversation, snapshot);
        }
        var profile = LoadProfiles().SingleOrDefault(p => p.Id == conversation.ProfileId);
        if (profile is not null)
        {
            var key = ReadKey(profile.KeyEnvironmentVariable);
            conversation = conversation with { Transcript = Redact(conversation.Transcript, key), Log = Redact(conversation.Log, key),
                Messages = conversation.Messages?.Select(m => m with { Title = Redact(m.Title, key), Text = Redact(m.Text, key) }).ToArray(),
                Session = RedactState(conversation.Session, key) };
        }
        AtomicWrite(SessionPath(conversation.Id), conversation with { Updated = DateTimeOffset.UtcNow }, PlatformJsonContext.Default.StoredConversation);
    }
    public StoredConversation Load(Guid id)
    {
        WorkspaceBoundary.CheckNoLinks(SessionPath(id));
        var data = JsonSerializer.Deserialize(File.ReadAllText(SessionPath(id)), PlatformJsonContext.Default.StoredConversation)
            ?? throw new InvalidDataException("Invalid conversation cache.");
        if (data.Version != 1 || data.HarnessVersion != "1.27.0" || data.Id != id || data.Session.ValueKind != JsonValueKind.Object ||
            data.ProfileId == Guid.Empty || data.Name is null || data.Transcript is null ||
            data.Messages?.Any(m => m is null || m.Role is null || m.Title is null || m.Text is null) == true)
            throw new InvalidDataException("Unsupported or invalid conversation cache.");
        return data;
    }
    public IReadOnlyList<StoredConversation> Search(string query, Guid? profile = null)
    {
        var issues = new List<SessionSearchIssue>();
        var sessions = new List<StoredConversation>();
        SearchIssues = issues.AsReadOnly();
        foreach (var path in Directory.EnumerateFiles(_root, "*.session.json"))
        {
            try
            {
                if (!Guid.TryParseExact(Path.GetFileName(path)[..^".session.json".Length], "N", out var id))
                    throw new InvalidDataException("Invalid session filename.");
                var session = Load(id);
                if ((!profile.HasValue || session.ProfileId == profile) &&
                    (session.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     session.Transcript.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    sessions.Add(session);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                issues.Add(new SessionSearchIssue(Path.GetFileName(path), ex.Message));
            }
        }
        return sessions.OrderByDescending(s => s.Updated).ToArray();
    }
    public void Rename(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Conversation name is required.");
        Save(Load(id) with { Name = name.Trim() });
    }
    public void Delete(Guid id) => File.Delete(SessionPath(id));
    public static void ValidateBinding(StoredConversation session, AgentProfile profile)
    {
        if (session.ProfileId != profile.Id || session.Binding != profile.Binding)
            throw new InvalidOperationException("历史会话绑定的 Agent 配置不同。请恢复原配置，或创建新会话；不会将历史发送给其他模型。");
    }
    private string SessionPath(Guid id) => Path.Combine(_root, id.ToString("N") + ".session.json");
    private static void AtomicWrite<T>(string path, T data, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        WorkspaceBoundary.CheckNoLinks(path);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".writing";
        try
        {
            using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                JsonSerializer.Serialize(file, data, typeInfo);
                file.Flush(true);
            }
            File.Move(staging, path, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    internal sealed record ProfilesDocument(int Version, List<AgentProfile> Profiles, Guid? SelectedProfileId = null);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(AgentProfile))]
[System.Text.Json.Serialization.JsonSerializable(typeof(StoredConversation))]
[System.Text.Json.Serialization.JsonSerializable(typeof(PlatformStore.ProfilesDocument))]
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonNode))]
internal partial class PlatformJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

public static class WorkspaceBoundary
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new ArgumentException("Only workspace-relative paths are allowed.");
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new UnauthorizedAccessException("Path escapes workspace.");
        CheckNoLinks(root);
        CheckNoLinks(path);
        return path;
    }
    public static void CheckNoLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
        {
            try
            {
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Symlinks/junctions are not permitted: " + part);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
