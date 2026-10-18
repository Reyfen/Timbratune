using System.Collections.Concurrent;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Xunit.Abstractions;
using static Reyfen.Timbratune.Core.Tests.HighPitchTests;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>
/// The rainbow set (<c>Fixtures/rainbow</c>, <c>Fixtures/OWNER-TAKES.md</c>): the Rainbow passage read eight times (the
/// owner in six stated intents, a cis woman and a cis man), each performance picked up by four microphones at once
/// (a USB audio device, a phone relayed over the network, a Bluetooth hands-free headset at 16 kHz, a USB camera
/// microphone). Recorded 2026-10-18 on the owner's decision that their own recordings, not lab corpora, are the
/// tests. Two kinds of fact in one set:
/// <list type="bullet">
/// <item>the four files of one performance are <b>one voice at one moment</b>: a reading that differs between them is the
/// microphone's, not the voice's (the rule: an app that tells a user what their voice does must not change its answer
/// with the microphone);</item>
/// <item>the owner's intents are pairs that differ in one thing (pitch, breathiness, weight), the owner's rule being
/// that pitch, size and weight are separate measurements (HIGH-PITCH-PLAN §1). The intents can be "a bit off" (the
/// owner), so only what Praat confirms is asserted: the "large" takes are not acoustically larger (OWNER-TAKES.md).</item>
/// </list>
/// Every reading is the app's own (<see cref="AcousticsAnalysisEngine.MeasureTracks"/> with the default scoring);
/// the pitch references are Praat's. Acceptance tests are red until met, as HANDOFF §8y's.
/// </summary>
public sealed class RainbowTests(ITestOutputHelper output)
{
    public static readonly string[] Devices = ["usb", "relay", "headset", "cam"];

    /// <summary>The reference microphone: the cleanest signal (flat to 8 kHz, noise 30 dB under the voice) and the one the relay agrees with.</summary>
    public const string Reference = "usb";

    public static readonly TheoryData<string> Performances =
    [
        "owner_extrahigh_small_light", "owner_high_small_light", "owner_high_small_light_breathy", "owner_high_large_light",
        "owner_high_large_heavy", "owner_low_small_heavy", "cisfem", "cismale",
    ];

    /// <summary>
    /// Praat's median pitch (ac, 60–600 Hz) of each performance on the reference microphone, 2026-10-19; the app's
    /// own medians agreed to the Hz on all 32 files, and the other microphones within 1–5 Hz (the camera on the cis
    /// woman 199 against 189: the noisy signal voices other frames; Praat says the same).
    /// </summary>
    public static readonly Dictionary<string, double> PraatPitchHz = new()
    {
        ["owner_extrahigh_small_light"] = 314, ["owner_high_small_light"] = 224, ["owner_high_small_light_breathy"] = 237,
        ["owner_high_large_light"] = 201, ["owner_high_large_heavy"] = 177, ["owner_low_small_heavy"] = 99,
        ["cisfem"] = 189, ["cismale"] = 95,
    };

    internal sealed record Reading(double F1, double F2, double F3, double Size, double Ceiling, double Weight, double Hnr, double Pitch, List<GraphPoint> Lines);

    private static readonly ConcurrentDictionary<string, Lazy<Reading>> Cache = new();

    /// <summary>The app's reading of one file, measured once per test run.</summary>
    internal static Reading Read(string performance, string device) => Cache.GetOrAdd($"{performance}_{device}", name => new Lazy<Reading>(() =>
    {
        Sound sound;
        using (var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rainbow", name + ".wav"))) sound = WavDecoder.Decode(s);
        var tracks = AcousticsAnalysisEngine.MeasureTracks(sound, RawAnalysisAssembler.LoudOutlierFraction, null, FormantCeilingMethod.Path, CeilingScoring.Default);
        var metrics = AnalysisPostProcessor.Process(RawAnalysisAssembler.Assemble(tracks)).Metrics;
        var f = metrics.Formants;
        return new Reading(f.F1Hz ?? double.NaN, f.F2Hz ?? double.NaN, f.F3Hz ?? double.NaN, f.VtlCm ?? double.NaN, f.CeilingHz ?? double.NaN,
            metrics.Weight?.H1a3cDb ?? double.NaN, metrics.VoiceQuality.HnrDb ?? double.NaN, metrics.Pitch.MedianHz ?? double.NaN,
            GraphLines(FrameSeriesBuilder.ForTake(tracks)));
    })).Value;

    private void Log(string name, Reading r) =>
        output.WriteLine($"{name,-42} pitch {r.Pitch,4:F0} Hz  HNR {r.Hnr,5:F1} dB  weight {r.Weight,5:F1} dB  F1/F2/F3 {r.F1,4:F0}/{r.F2,4:F0}/{r.F3,4:F0}  ceiling {r.Ceiling,4:F0}  size {r.Size,5:F1} cm");

    // ------------------------------------------------------------------
    // One performance, four microphones
    // ------------------------------------------------------------------

    /// <summary>Pitch may differ between the microphones by this share. Measured 2026-10-19: 0.3–2.7 %, the camera on the cis woman 5.3 %.</summary>
    private const double DevicePitchTolerance = 0.05;

    /// <summary>
    /// The size may differ between the microphones by this much, and F3 by <see cref="DeviceF3ToleranceHz"/>: what two
    /// readings of one voice may differ by before a user sees two voices. Measured 2026-10-19 against the reference:
    /// the relay 0.1–0.7 cm on every performance; the headset 0.4–3.0 cm and the camera 0.5–1.9 cm, both beyond this
    /// on most performances (the headset's F2 reads 300–700 Hz lower, the camera's about 1100 Hz on every file).
    /// v0.3.0 (the fixed 5500 / 5000 Hz choice) differed by up to 2.9 cm between the reference and the relay. An
    /// acceptance test: red until met.
    /// </summary>
    private const double DeviceSizeToleranceCm = 1.0, DeviceF3ToleranceHz = 250;

    /// <summary>
    /// The weight (corrected H1*–A3*, dB) may differ between the microphones by this much. Measured 2026-10-19: up to
    /// 12 dB (the owner's heavy voice 4.4 dB on the camera, 16.3 on the reference): a spectral tilt read without any
    /// microphone correction follows the microphone's own tilt. An acceptance test: red until met.
    /// </summary>
    private const double DeviceWeightToleranceDb = 3;

    /// <summary>
    /// Per 3 s piece, the F2 and F3 lines of one performance may differ between the microphones by this much (the
    /// spread of the piece medians over the microphones that have a full piece there). Measured 2026-10-19 with the
    /// default scoring: the worst piece of each performance differs by 440–1340 Hz in F2 and 460–970 Hz in F3; on the
    /// reference and the relay alone, by up to 650 / 500 Hz. An acceptance test: red until met.
    /// </summary>
    private const double DevicePieceToleranceHz = 300;

    [Theory]
    [MemberData(nameof(Performances))]
    public void TheSamePerformanceReadsTheSamePitchOnEveryMicrophone(string performance)
    {
        var reference = PraatPitchHz[performance];
        var failures = new List<string>();
        foreach (var device in Devices)
        {
            var r = Read(performance, device);
            Log($"{performance} {device}", r);
            if (Math.Abs(r.Pitch - reference) > DevicePitchTolerance * reference) failures.Add($"{device}: {r.Pitch:F0} Hz against Praat's {reference:F0} on the {Reference}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(Performances))]
    public void TheSamePerformanceReadsTheSameSizeOnEveryMicrophone(string performance)
    {
        var reference = Read(performance, Reference);
        var failures = new List<string>();
        foreach (var device in Devices)
        {
            var r = Read(performance, device);
            Log($"{performance} {device}", r);
            if (Math.Abs(r.Size - reference.Size) > DeviceSizeToleranceCm || Math.Abs(r.F3 - reference.F3) > DeviceF3ToleranceHz)
                failures.Add($"{device}: size {r.Size:F1} cm, F3 {r.F3:F0} Hz against {reference.Size:F1} cm, {reference.F3:F0} Hz on the {Reference}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(Performances))]
    public void TheSamePerformanceReadsTheSameWeightOnEveryMicrophone(string performance)
    {
        var reference = Read(performance, Reference);
        var failures = new List<string>();
        foreach (var device in Devices)
        {
            var r = Read(performance, device);
            Log($"{performance} {device}", r);
            if (Math.Abs(r.Weight - reference.Weight) > DeviceWeightToleranceDb) failures.Add($"{device}: weight {r.Weight:F1} dB against {reference.Weight:F1} on the {Reference}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    /// The graph lines, piece by piece: each 3 s piece's median F2 and F3 across the microphones that have a full piece
    /// there (at least <see cref="MinPiecePoints"/> points) within <see cref="DevicePieceToleranceHz"/> of each other.
    /// </summary>
    [Theory]
    [MemberData(nameof(Performances))]
    public void TheSamePerformanceDrawsTheSameLinesOnEveryMicrophone(string performance)
    {
        var pieces = new Dictionary<int, List<(string Device, double F2, double F3)>>();
        foreach (var device in Devices)
            foreach (var (piece, points) in Pieces(Read(performance, device).Lines))
            {
                if (!pieces.TryGetValue(piece, out var list)) pieces[piece] = list = [];
                list.Add((device, Median(points.Select(p => p.F2)), Median(points.Select(p => p.F3))));
            }
        var failures = new List<string>();
        foreach (var (piece, readings) in pieces.OrderBy(p => p.Key))
        {
            if (readings.Count < 2) continue;
            var f2 = readings.Max(r => r.F2) - readings.Min(r => r.F2);
            var f3 = readings.Max(r => r.F3) - readings.Min(r => r.F3);
            var line = $"{performance} {piece * PieceSeconds:F0}-{(piece + 1) * PieceSeconds:F0} s: " +
                       string.Join("  ", readings.Select(r => $"{r.Device} {r.F2:F0}/{r.F3:F0}")) + $"  spread F2 {f2:F0} F3 {f3:F0} Hz";
            output.WriteLine(line);
            if (f2 > DevicePieceToleranceHz || f3 > DevicePieceToleranceHz) failures.Add(line);
        }
        Assert.True(pieces.Count(p => p.Value.Count >= 2) >= 3, "fewer than 3 pieces with two microphones");
        Assert.True(failures.Count == 0, "pieces that differ between microphones:\n" + string.Join("\n", failures));
    }

    // ------------------------------------------------------------------
    // The owner's intents (on the two microphones that agree: the reference and the relay)
    // ------------------------------------------------------------------

    public static readonly TheoryData<string> AgreeingDevices = ["usb", "relay"];

    /// <summary>
    /// The owner's rule: pitch does not change the size. The three "small" performances (extra-high, high, low:
    /// 314, 224 and 99 Hz) must read within this of each other. Measured 2026-10-19: reference 16.4 / 17.0 / 16.9 cm,
    /// relay 16.2 / 17.5 / 16.3.
    /// </summary>
    private const double PitchSizeToleranceCm = 1.5;

    [Theory]
    [MemberData(nameof(AgreeingDevices))]
    public void TheOwnersSizeDoesNotDependOnPitch(string device)
    {
        var sizes = new List<double>();
        foreach (var p in new[] { "owner_extrahigh_small_light", "owner_high_small_light", "owner_low_small_heavy" })
        {
            var r = Read(p, device);
            Log($"{p} {device}", r);
            sizes.Add(r.Size);
        }
        Assert.InRange(sizes.Max() - sizes.Min(), 0, PitchSizeToleranceCm);
        Assert.True(Read("owner_extrahigh_small_light", device).Pitch > 1.3 * Read("owner_high_small_light", device).Pitch, "the extra-high take must read a clearly higher pitch");
        Assert.True(Read("owner_low_small_heavy", device).Pitch < 0.6 * Read("owner_high_small_light", device).Pitch, "the low take must read a clearly lower pitch");
    }

    /// <summary>
    /// Breathiness is clarity, not size: the breathy take (the same voice, pitch and intent otherwise) reads a lower
    /// HNR by at least <see cref="BreathyHnrDropDb"/> and a size within <see cref="BreathySizeToleranceCm"/>. Measured
    /// 2026-10-19: HNR 13.6 → 10.2 dB (reference), 13.2 → 12.3 (relay); size 17.0 → 17.1, 17.5 → 16.8 cm.
    /// </summary>
    private const double BreathyHnrDropDb = 0.5, BreathySizeToleranceCm = 1.0;

    [Theory]
    [MemberData(nameof(AgreeingDevices))]
    public void BreathinessChangesClarityNotSize(string device)
    {
        var clear = Read("owner_high_small_light", device);
        var breathy = Read("owner_high_small_light_breathy", device);
        Log($"clear {device}", clear);
        Log($"breathy {device}", breathy);
        Assert.True(breathy.Hnr <= clear.Hnr - BreathyHnrDropDb, $"HNR {breathy.Hnr:F1} against {clear.Hnr:F1} dB");
        Assert.InRange(Math.Abs(breathy.Size - clear.Size), 0, BreathySizeToleranceCm);
    }

    /// <summary>
    /// The owner's heavy intents read heavier: the app's weight is the corrected H1*–A3* with higher = heavier (the
    /// zones: light under 10 dB, heavy over 12.5). The heavy take of each pair by at least this. Measured 2026-10-19:
    /// large light → large heavy 7.3 → 16.3 dB (reference), 9.7 → 14.8 (relay); small light high → small heavy low
    /// 4.8 → 15.0, 5.4 → 11.9 (that pair changes pitch too).
    /// </summary>
    private const double HeavierByDb = 2;

    [Theory]
    [MemberData(nameof(AgreeingDevices))]
    public void TheOwnersHeavyIntentReadsHeavier(string device)
    {
        foreach (var (light, heavy) in new[] { ("owner_high_large_light", "owner_high_large_heavy"), ("owner_high_small_light", "owner_low_small_heavy") })
        {
            var a = Read(light, device);
            var b = Read(heavy, device);
            Log($"{light} {device}", a);
            Log($"{heavy} {device}", b);
            Assert.True(b.Weight >= a.Weight + HeavierByDb, $"{heavy}: weight {b.Weight:F1} dB against {light}'s {a.Weight:F1}");
        }
    }

    /// <summary>
    /// The two other speakers: a cis woman and a cis man reading the same passage. He reads at least this much larger
    /// on every microphone (measured 2026-10-19: 2.1–4.4 cm) and both pitches sit where Praat puts them.
    /// </summary>
    private const double SpeakersApartCm = 2;

    [Fact]
    public void TheCisManReadsLargerThanTheCisWoman()
    {
        foreach (var device in Devices)
        {
            var woman = Read("cisfem", device);
            var man = Read("cismale", device);
            Log($"cis woman {device}", woman);
            Log($"cis man {device}", man);
            Assert.True(man.Size >= woman.Size + SpeakersApartCm, $"{device}: {man.Size:F1} against {woman.Size:F1} cm");
            Assert.InRange(woman.Pitch, 160, 215);
            Assert.InRange(man.Pitch, 80, 110);
        }
    }
}
