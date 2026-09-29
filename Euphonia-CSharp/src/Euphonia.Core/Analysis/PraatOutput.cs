using System.Globalization;

namespace Euphonia.Core.Analysis;

/// <summary>Raw values printed by analyze.praat, before any statistics.</summary>
public sealed class PraatOutput
{
    public required IReadOnlyDictionary<string, double> Summary { get; init; }
    /// <summary>Vowel-core candidates that passed the F1 gate, in time order per ceiling.</summary>
    public required IReadOnlyList<FormantRow> Formants { get; init; }
    public required IReadOnlyList<WeightRow> WeightFrames { get; init; }
    public required IReadOnlyList<(double X, double Y)> Ltas { get; init; }
    /// <summary>10 ms pitch track; Hz &lt;= 0 means unvoiced.</summary>
    public required IReadOnlyList<(double T, double Hz)> Contour { get; init; }
    public required IReadOnlyList<(double Start, double End)> Sounding { get; init; }

    public double Get(string key) => Summary.TryGetValue(key, out var v) ? v : double.NaN;

    public readonly record struct FormantRow(double Ceiling, double T, double F1, double F2, double F3);

    public readonly record struct WeightRow(
        double T, double F0, double F1, double F2, double F3,
        double B1, double B2, double B3,
        double H1, double A3, double K3);

    /// <summary>
    /// Parses analyze.praat's stdout (tab-separated records, see the header of
    /// the script). Throws <see cref="PraatException"/> if the END marker is
    /// missing — i.e. the script didn't run to completion.
    /// </summary>
    public static PraatOutput Parse(string stdout)
    {
        var summary = new Dictionary<string, double>(StringComparer.Ordinal);
        var formants = new List<FormantRow>();
        var weight = new List<WeightRow>();
        var ltas = new List<(double, double)>();
        var contour = new List<(double, double)>();
        var phrases = new List<(double, double)>();
        var complete = false;

        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            var f = line.TrimEnd('\r').Split('\t');
            switch (f[0])
            {
                case "S" when f.Length >= 3:
                    summary[f[1]] = ParseNumber(f[2]);
                    break;
                case "F" when f.Length >= 6:
                    formants.Add(new FormantRow(N(f, 1), N(f, 2), N(f, 3), N(f, 4), N(f, 5)));
                    break;
                case "W" when f.Length >= 12:
                    weight.Add(new WeightRow(N(f, 1), N(f, 2), N(f, 3), N(f, 4), N(f, 5), N(f, 6), N(f, 7), N(f, 8), N(f, 9), N(f, 10), N(f, 11)));
                    break;
                case "L" when f.Length >= 3:
                    ltas.Add((N(f, 1), N(f, 2)));
                    break;
                case "C" when f.Length >= 3:
                    contour.Add((N(f, 1), N(f, 2)));
                    break;
                case "P" when f.Length >= 3:
                    phrases.Add((N(f, 1), N(f, 2)));
                    break;
                case "END":
                    complete = true;
                    break;
            }
        }
        if (!complete) throw new PraatException("Praat's output ended early (no END marker).");

        return new PraatOutput
        {
            Summary = summary,
            Formants = formants,
            WeightFrames = weight,
            Ltas = ltas,
            Contour = contour,
            Sounding = phrases,
        };
    }

    private static double N(string[] fields, int i) => ParseNumber(fields[i]);

    /// <summary>Praat prints undefined as "--undefined--"; that (and anything unparsable) becomes NaN.</summary>
    internal static double ParseNumber(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
}
