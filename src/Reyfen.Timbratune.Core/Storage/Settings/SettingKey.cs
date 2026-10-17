using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Reyfen.Timbratune.Core.Storage.Settings;

/// <summary>
/// Where a setting lives. Secrets are never settings: they go to a secure store (REFACTOR-PLAN §6).
/// </summary>
public enum SettingScope
{
    /// <summary>This device only (settings.device.json).</summary>
    Device,

    /// <summary>Follows the account to other devices once sync exists (settings.synced.json, the local copy).</summary>
    Synced,
}

/// <summary>A setting without its type: what the registry and the service need to know.</summary>
public interface ISettingKey
{
    /// <summary>Namespaced by feature ("recording.liveWindowSeconds", "shell.theme"); never reused for something else.</summary>
    string Id { get; }

    SettingScope Scope { get; }
}

/// <summary>
/// One setting: its id, default, scope and how its value is written (source-generated JSON, so
/// trim/AOT safe). Each feature declares its own keys and registers them; there is no central list.
/// </summary>
public sealed record SettingKey<T>(string Id, T Default, SettingScope Scope, JsonTypeInfo<T> Json) : ISettingKey;

/// <summary>JSON for the value types settings use; features with their own value types add a context of their own.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(double?))]
public sealed partial class SettingsJson : JsonSerializerContext;
