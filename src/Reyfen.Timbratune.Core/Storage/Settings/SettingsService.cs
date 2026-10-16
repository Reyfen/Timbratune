using System.Text.Json;
using System.Text.Json.Nodes;
using Reyfen.Timbratune.Core.Diagnostics;

namespace Reyfen.Timbratune.Core.Storage.Settings;

/// <summary>
/// Settings that persist between sessions. Features read a setting once and subscribe to
/// <see cref="Changed"/>; settings are never read on the analysis' hot path.
/// </summary>
public interface ISettings
{
    T Get<T>(SettingKey<T> key);

    /// <summary>Changes a setting at once in memory; it is saved shortly after (debounced).</summary>
    void Set<T>(SettingKey<T> key, T value);

    /// <summary>Raised on the thread that called <see cref="Set{T}"/>, after the change.</summary>
    event Action<ISettingKey>? Changed;
}

/// <summary>
/// The settings, held in memory as loaded at start-up, one JSON document per scope:
/// <code>{ "schemaVersion": 1, "values": { "shell.theme": "dark", … } }</code>
/// Writes update memory at once and are saved after <see cref="SaveDelay"/> on a background
/// timer (atomically, by the store). Keys this version doesn't know (from a newer version on
/// another device, or a feature that is off) are kept and written back unchanged. A damaged
/// file starts from the defaults (the store keeps it as .bak). Older documents are migrated on load.
/// </summary>
public sealed class SettingsService : ISettings, IDisposable
{
    /// <summary>The first schema version; each migration adds one.</summary>
    public const int FirstSchemaVersion = 1;

    public const string DeviceDocument = "settings.device.json", SyncedDocument = "settings.synced.json";

    /// <summary>Words that mark a secret: such ids are refused (secrets belong in a secure store).</summary>
    private static readonly string[] SecretWords = ["token", "secret", "password", "apikey"];

    private readonly ISettingsStore _store;
    private readonly IAppLog _log;
    private readonly IReadOnlyList<Func<JsonObject, JsonObject>> _migrations;
    private readonly object _gate = new();
    private readonly Dictionary<SettingScope, Document> _documents = [];
    private readonly Dictionary<string, ISettingKey> _keys = new(StringComparer.Ordinal);
    private readonly Timer _timer;
    private bool _disposed;

    /// <param name="keys">Every feature's registered keys: checked for clashes and secret-looking ids.</param>
    /// <param name="migrations">
    /// Index n turns a version n+1 document into version n+2 (the whole document, "values" and all).
    /// There are none yet; the app writes version <see cref="FirstSchemaVersion"/> + their number.
    /// </param>
    public SettingsService(ISettingsStore store, IEnumerable<ISettingKey> keys, TimeSpan? saveDelay = null,
        IReadOnlyList<Func<JsonObject, JsonObject>>? migrations = null, IAppLog? log = null)
    {
        _store = store;
        _log = log ?? NullAppLog.Instance;
        _migrations = migrations ?? [];
        SaveDelay = saveDelay ?? TimeSpan.FromMilliseconds(500);
        foreach (var key in keys) Register(key);
        foreach (var scope in Enum.GetValues<SettingScope>()) _documents[scope] = LoadDocument(NameOf(scope));
        _timer = new Timer(_ => Flush());
    }

    public TimeSpan SaveDelay { get; }

    /// <summary>The version this app writes.</summary>
    public int CurrentSchemaVersion => FirstSchemaVersion + _migrations.Count;

    public event Action<ISettingKey>? Changed;

    public T Get<T>(SettingKey<T> key)
    {
        lock (_gate)
        {
            var document = _documents[key.Scope];
            if (document.Cache.TryGetValue(key.Id, out var cached)) return (T)cached!;
            var value = key.Default;
            if (document.Values[key.Id] is { } node)
            {
                try
                {
                    value = node.Deserialize(key.Json)!;
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
                {
                    // Written in another shape (another version): the default, until set again.
                }
            }
            document.Cache[key.Id] = value;
            return value;
        }
    }

    public void Set<T>(SettingKey<T> key, T value)
    {
        lock (_gate)
        {
            var document = _documents[key.Scope];
            if (document.Cache.TryGetValue(key.Id, out var old) && Equals(old, value) && document.Values.ContainsKey(key.Id)) return;
            document.Values[key.Id] = JsonSerializer.SerializeToNode(value, key.Json);
            document.Cache[key.Id] = value;
            document.Dirty = true;
            if (!_disposed) _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke(key);
    }

    /// <summary>Saves what changed now (also on shutdown); safe to call any time.</summary>
    public void Flush()
    {
        List<(string Name, JsonObject Document)> pending = [];
        lock (_gate)
        {
            foreach (var (scope, document) in _documents)
            {
                if (!document.Dirty) continue;
                document.Dirty = false;
                pending.Add((NameOf(scope), document.ToJson()));
            }
        }
        foreach (var (name, json) in pending)
        {
            try
            {
                _store.Save(name, json);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Can't write now (disk full, locked): the values stay in memory and are retried on the next change.
                lock (_gate) MarkDirty(name);
                _log.Log(AppLogLevel.Warning, "settings", e, $"saving {name} failed");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
        Flush();
    }

    private void Register(ISettingKey key)
    {
        var lower = key.Id.ToLowerInvariant();
        if (SecretWords.Any(lower.Contains))
            throw new ArgumentException($"Setting \"{key.Id}\" looks like a secret; secrets don't belong in settings.");
        if (_keys.TryGetValue(key.Id, out var existing) && !ReferenceEquals(existing, key))
            throw new ArgumentException($"Setting \"{key.Id}\" is registered twice.");
        _keys[key.Id] = key;
    }

    private void MarkDirty(string name)
    {
        foreach (var (scope, document) in _documents)
            if (NameOf(scope) == name) document.Dirty = true;
    }

    private static string NameOf(SettingScope scope) => scope == SettingScope.Device ? DeviceDocument : SyncedDocument;

    private Document LoadDocument(string name)
    {
        var json = _store.Load(name);
        if (json is null) return new Document(new JsonObject(), CurrentSchemaVersion);
        var stored = json["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : FirstSchemaVersion;
        var version = Math.Max(stored, FirstSchemaVersion);
        for (; version < CurrentSchemaVersion; version++) json = _migrations[version - FirstSchemaVersion](json);
        var values = json["values"] as JsonObject ?? new JsonObject();
        json.Remove("values");
        // A newer app's document keeps its version: that app migrates it, not this one.
        return new Document(values, version) { Dirty = version != stored };
    }

    private sealed class Document(JsonObject values, int version)
    {
        public JsonObject Values { get; } = values;
        public Dictionary<string, object?> Cache { get; } = new(StringComparer.Ordinal);
        public int Version { get; } = version;
        public bool Dirty { get; set; }

        public JsonObject ToJson() => new()
        {
            ["schemaVersion"] = Version,
            ["values"] = Values.DeepClone(),
        };
    }
}
