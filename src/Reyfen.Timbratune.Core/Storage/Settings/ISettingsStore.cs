using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reyfen.Timbratune.Core.Storage.Settings;

/// <summary>Loads and saves one settings document by name ("settings.device.json", …).</summary>
public interface ISettingsStore
{
    /// <summary>The document, or null when there is none yet or it was unreadable (then it is set aside as "name.bak").</summary>
    JsonObject? Load(string name);

    /// <summary>Saves the document atomically (the previous version survives a crash mid-write).</summary>
    void Save(string name, JsonObject document);
}

/// <summary>Settings documents as JSON files in an <see cref="IDataStore"/>.</summary>
public sealed class DataStoreSettingsStore(IDataStore data) : ISettingsStore
{
    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    public JsonObject? Load(string name)
    {
        if (!data.Exists(name)) return null;
        try
        {
            using var stream = data.OpenRead(name);
            if (JsonNode.Parse(stream) is JsonObject document) return document;
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
            return null; // unreadable right now (locked): defaults this time, the file stays as it is
        }
        // Damaged: keep it for whoever wants to look, start from the defaults.
        try
        {
            data.Move(name, name + ".bak");
        }
        catch (IOException)
        {
        }
        return null;
    }

    public void Save(string name, JsonObject document) =>
        data.Write(name, stream =>
        {
            using var writer = new Utf8JsonWriter(stream, Indented);
            document.WriteTo(writer);
        });
}
