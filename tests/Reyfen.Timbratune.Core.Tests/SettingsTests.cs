using System.Text.Json.Nodes;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Core.Storage.Settings;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>REFACTOR-PLAN.md §5a.4: settings that persist between sessions.</summary>
public sealed class SettingsTests : IDisposable
{
    private static readonly SettingKey<string> Theme = new("shell.theme", "system", SettingScope.Device, SettingsJson.Default.String);
    private static readonly SettingKey<double> Window = new("recording.liveWindowSeconds", 0, SettingScope.Device, SettingsJson.Default.Double);
    private static readonly SettingKey<bool> Tips = new("shell.tipsDismissed", false, SettingScope.Synced, SettingsJson.Default.Boolean);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private FileSystemDataStore Data => new(_root);

    private SettingsService Open(TimeSpan? delay = null, IReadOnlyList<Func<JsonObject, JsonObject>>? migrations = null) =>
        new(new DataStoreSettingsStore(Data), [Theme, Window, Tips], delay ?? TimeSpan.FromHours(1), migrations);

    private string Read(string name) => File.ReadAllText(Path.Combine(_root, name));

    [Fact]
    public void DefaultsUntilSetThenKeptAcrossSessions()
    {
        using (var settings = Open())
        {
            Assert.Equal("system", settings.Get(Theme));
            Assert.Equal(0, settings.Get(Window));
            settings.Set(Theme, "dark");
            settings.Set(Window, 30);
            settings.Set(Tips, true);
            Assert.Equal("dark", settings.Get(Theme));
        } // disposing saves what is pending

        using var again = Open();
        Assert.Equal("dark", again.Get(Theme));
        Assert.Equal(30, again.Get(Window));
        Assert.True(again.Get(Tips));
        Assert.Contains("\"shell.theme\": \"dark\"", Read(SettingsService.DeviceDocument));
        Assert.Contains("\"shell.tipsDismissed\": true", Read(SettingsService.SyncedDocument)); // each scope its own file
        Assert.DoesNotContain("tipsDismissed", Read(SettingsService.DeviceDocument));
    }

    [Fact]
    public void ChangesAreAnnouncedOnceAndSavedAfterTheDelay()
    {
        using var settings = Open(TimeSpan.FromMilliseconds(100));
        var changed = new List<string>();
        settings.Changed += key => changed.Add(key.Id);
        settings.Set(Theme, "light");
        settings.Set(Theme, "light"); // unchanged: no event, no write
        Assert.Equal(["shell.theme"], changed);
        Assert.False(File.Exists(Path.Combine(_root, SettingsService.DeviceDocument))); // not at once: debounced
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(Path.Combine(_root, SettingsService.DeviceDocument)) && DateTime.UtcNow < deadline) Thread.Sleep(20);
        Assert.Contains("\"light\"", Read(SettingsService.DeviceDocument));
    }

    [Fact]
    public void UnknownKeysAreKeptAndWrittenBack()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, SettingsService.DeviceDocument),
            """{ "schemaVersion": 1, "values": { "future.feature": { "nested": [1, 2] }, "shell.theme": "light" } }""");
        using (var settings = Open())
        {
            Assert.Equal("light", settings.Get(Theme));
            settings.Set(Window, 10);
        }
        var saved = JsonNode.Parse(Read(SettingsService.DeviceDocument))!;
        Assert.Equal(2, saved["values"]!["future.feature"]!["nested"]![1]!.GetValue<int>());
        Assert.Equal(10, saved["values"]!["recording.liveWindowSeconds"]!.GetValue<double>());
    }

    [Fact]
    public void ADamagedFileStartsFromTheDefaultsAndIsKeptAsBak()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, SettingsService.DeviceDocument), "{ not json");
        using var settings = Open();
        Assert.Equal("system", settings.Get(Theme));
        Assert.Equal("{ not json", Read(SettingsService.DeviceDocument + ".bak"));
        Assert.False(File.Exists(Path.Combine(_root, SettingsService.DeviceDocument)));
    }

    [Fact]
    public void AValueOfAnotherShapeReadsAsTheDefault()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, SettingsService.DeviceDocument),
            """{ "schemaVersion": 1, "values": { "recording.liveWindowSeconds": "ten" } }""");
        using var settings = Open();
        Assert.Equal(0, settings.Get(Window));
    }

    [Fact]
    public void OlderDocumentsAreMigratedAndNewerOnesKeepTheirVersion()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, SettingsService.DeviceDocument),
            """{ "schemaVersion": 1, "values": { "theme": "dark" } }""");
        // Version 1 → 2: "theme" was renamed "shell.theme".
        Func<JsonObject, JsonObject> rename = doc =>
        {
            var values = (JsonObject)doc["values"]!;
            if (values["theme"] is { } old)
            {
                values.Remove("theme");
                values["shell.theme"] = old.DeepClone();
            }
            doc["schemaVersion"] = 2;
            return doc;
        };
        using (var settings = Open(migrations: [rename]))
        {
            Assert.Equal(2, settings.CurrentSchemaVersion);
            Assert.Equal("dark", settings.Get(Theme));
        }
        var saved = JsonNode.Parse(Read(SettingsService.DeviceDocument))!; // a migrated document is saved
        Assert.Equal(2, saved["schemaVersion"]!.GetValue<int>());

        // An older app (no migrations) opening it keeps version 2, so the newer app won't migrate it again.
        using (var older = Open())
            older.Set(Window, 60);
        Assert.Equal(2, JsonNode.Parse(Read(SettingsService.DeviceDocument))!["schemaVersion"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("account.token")]
    [InlineData("patreon.clientSecret")]
    [InlineData("sync.password")]
    public void SecretLookingIdsAreRefused(string id) =>
        Assert.Throws<ArgumentException>(() =>
            new SettingsService(new DataStoreSettingsStore(Data), [new SettingKey<string>(id, "", SettingScope.Device, SettingsJson.Default.String)]));

    [Fact]
    public void TwoKeysWithOneIdAreRefused() =>
        Assert.Throws<ArgumentException>(() => new SettingsService(new DataStoreSettingsStore(Data),
            [Theme, new SettingKey<string>(Theme.Id, "x", SettingScope.Device, SettingsJson.Default.String)]));
}
