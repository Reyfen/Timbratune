using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Euphonia.Core.Models;

namespace Euphonia.Core.Json;

// Source-generated (trim/AOT-friendly, which matters for the future mobile
// heads). snake_case matches the files analyze.py and the Electron app wrote.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(List<Recording>))]
[JsonSerializable(typeof(RecordingDetail))]
[JsonSerializable(typeof(List<ReferenceVoice>))]
internal partial class EuphoniaJsonContext : JsonSerializerContext;

public static class EuphoniaJson
{
    // recordings.json is pretty-printed (indent=2, like analyze.py);
    // analysis/<id>.json is compact.
    private static readonly EuphoniaJsonContext Indented = new(new JsonSerializerOptions(EuphoniaJsonContext.Default.Options)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private static readonly EuphoniaJsonContext Compact = new(new JsonSerializerOptions(EuphoniaJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static List<Recording> ReadRecordings(string json) =>
        JsonSerializer.Deserialize(json, Compact.ListRecording) ?? [];

    public static string WriteRecordings(List<Recording> recordings) =>
        JsonSerializer.Serialize(recordings, Indented.ListRecording);

    public static RecordingDetail? ReadDetail(string json) =>
        JsonSerializer.Deserialize(json, Compact.RecordingDetail);

    public static string WriteDetail(RecordingDetail detail) =>
        JsonSerializer.Serialize(detail, Compact.RecordingDetail);

    public static List<ReferenceVoice> ReadReferences(string json) =>
        JsonSerializer.Deserialize(json, Compact.ListReferenceVoice) ?? [];
}
