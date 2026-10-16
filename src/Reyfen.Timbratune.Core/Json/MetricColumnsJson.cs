using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Json;

// The compatibility layer of the trend and phrase columns (REFACTOR-PLAN §2.3): in memory they are
// arrays by metric index of MetricRegistry.Default; in detail.json each metric with that column is
// a property named by its MetricInfo.Key, today's v2 names first in today's order, then any newer
// metric's in registry order. Unknown names (a metric this version doesn't have) are skipped.

internal static class MetricColumnsJson
{
    // detail.json's order before the registry (v2): kept so existing files are written unchanged.
    private static readonly string[] TrendOrder =
        ["mean_hz", "pitch_sd_hz", "melody_st", "loudness_db", "offset_hz", "f2_hz", "f3_hz", "weight_db", "hnr_db", "jitter_pct"];
    private static readonly string[] PhraseOrder = ["mean_hz", "melody_st", "offset_hz", "f2_hz", "weight_db"];

    public static readonly Layout Trend = new(MetricRegistry.Default, MetricColumns.Trend, TrendOrder);
    public static readonly Layout Phrase = new(MetricRegistry.Default, MetricColumns.Phrase, PhraseOrder);

    /// <summary>Which metrics are saved, under which names, in which order.</summary>
    public sealed class Layout
    {
        public Layout(MetricRegistry registry, MetricColumns column, string[] legacyOrder)
        {
            Count = registry.Count;
            var saved = registry.Metrics.Where(m => m.Info.Columns.HasFlag(column)).ToList();
            var ordered = legacyOrder.Select(key => saved.FirstOrDefault(m => m.Info.Key == key)).OfType<RegisteredMetric>()
                .Concat(saved.Where(m => !legacyOrder.Contains(m.Info.Key)));
            Columns = ordered.Select(m => (JsonEncodedText.Encode(m.Info.Key), m.Id.Index)).ToArray();
            ByKey = saved.ToDictionary(m => m.Info.Key, m => m.Id.Index, StringComparer.Ordinal);
        }

        public int Count { get; }
        public (JsonEncodedText Key, int Index)[] Columns { get; }
        public Dictionary<string, int> ByKey { get; }
    }

    public static void WriteColumns(Utf8JsonWriter writer, double?[] values, Layout layout)
    {
        foreach (var (key, index) in layout.Columns)
        {
            if (index < values.Length && values[index] is { } v) writer.WriteNumber(key, v);
            else writer.WriteNull(key);
        }
    }

    public static double? ReadNumber(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => null,
        JsonTokenType.Number => reader.GetDouble(),
        JsonTokenType.String => double.Parse(reader.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture),
        _ => throw new JsonException($"Expected a number, got {reader.TokenType}."),
    };

    /// <summary>Reads one object: <paramref name="fixedProperty"/> handles the fixed properties (returns false for the rest).</summary>
    public static void ReadObject(ref Utf8JsonReader reader, double?[] values, Layout layout, Func<string, double?, bool> fixedProperty)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected an object.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            reader.Read();
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
                continue;
            }
            var value = ReadNumber(ref reader);
            if (fixedProperty(name, value)) continue;
            if (layout.ByKey.TryGetValue(name, out var index)) values[index] = value;
        }
    }
}

internal sealed class TrendSliceJsonConverter : JsonConverter<TrendSlice>
{
    public override TrendSlice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var slice = new TrendSlice(MetricColumnsJson.Trend.Count);
        MetricColumnsJson.ReadObject(ref reader, slice.Values, MetricColumnsJson.Trend, (name, value) =>
        {
            switch (name)
            {
                case "t": slice.T = value ?? 0; return true;
                case "start": slice.Start = value ?? 0; return true;
                case "end": slice.End = value ?? 0; return true;
                default: return false;
            }
        });
        return slice;
    }

    public override void Write(Utf8JsonWriter writer, TrendSlice value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("t", value.T);
        writer.WriteNumber("start", value.Start);
        writer.WriteNumber("end", value.End);
        MetricColumnsJson.WriteColumns(writer, value.Values, MetricColumnsJson.Trend);
        writer.WriteEndObject();
    }
}

internal sealed class PhraseMetricsJsonConverter : JsonConverter<PhraseMetrics>
{
    public override PhraseMetrics Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var phrase = new PhraseMetrics(MetricColumnsJson.Phrase.Count);
        MetricColumnsJson.ReadObject(ref reader, phrase.Values, MetricColumnsJson.Phrase, (name, value) =>
        {
            switch (name)
            {
                case "start": phrase.Start = value ?? 0; return true;
                case "end": phrase.End = value ?? 0; return true;
                default: return false;
            }
        });
        return phrase;
    }

    public override void Write(Utf8JsonWriter writer, PhraseMetrics value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("start", value.Start);
        writer.WriteNumber("end", value.End);
        MetricColumnsJson.WriteColumns(writer, value.Values, MetricColumnsJson.Phrase);
        writer.WriteEndObject();
    }
}
