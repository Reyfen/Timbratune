using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Streaming;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Tracks;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>The track layer (REFACTOR-PLAN §2.1): stages, track sets, and the derived tracks' reuse.</summary>
public sealed class TrackTests
{
    private static Sound Fixture(string clip)
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", clip + ".wav"));
        return WavDecoder.Decode(s);
    }

    [Fact]
    public void ATrackBringsWhatItIsComputedFrom()
    {
        Assert.Equal(TrackSet.Pitch | TrackSet.Pulses, TrackSet.Pulses.WithDependencies());
        Assert.Equal(TrackSet.Pitch | TrackSet.Intensity | TrackSet.Formants | TrackSet.VoicedFrames | TrackSet.LoudVowelGate | TrackSet.VowelFormantRows,
            TrackSet.VowelFormantRows.WithDependencies());
        Assert.Equal(TrackSet.Pitch | TrackSet.Formants | TrackSet.VoicedFrames | TrackSet.WeightFrames, TrackSet.WeightFrames.WithDependencies());
        Assert.Equal(TrackSet.All, TrackSet.All.WithDependencies());
        Assert.True(TrackSet.LiveFull.Has(TrackSet.Live));
        Assert.False(TrackSet.LiveFull.Has(TrackSet.Ltas));
    }

    [Fact]
    public void OnlyTheAskedTracksAreMeasured()
    {
        var view = SavedTrackProvider.Measure(Fixture("vctk_f339"), TrackSet.LoudVowelGate);
        Assert.Equal(TrackSet.Pitch | TrackSet.Intensity | TrackSet.VoicedFrames | TrackSet.LoudVowelGate, view.Tracks);
        Assert.NotNull(view.Pitch);
        Assert.Null(view.Harmonicity);
        Assert.Null(view.Formants5500);
        Assert.Empty(view.Pulses);
        Assert.Equal(TrackSet.VoicedFrames | TrackSet.LoudVowelGate, view.Derived.Tracks);
        Assert.NotEmpty(view.Derived.LoudTimes);
        Assert.True(view.Derived.LoudTimes.Count <= view.Derived.Voiced.Count);
    }

    [Fact]
    public void TheDerivedTracksAreWhatTheAssemblerWouldDerive()
    {
        var sound = Fixture("vctk_m311");
        var tracks = AcousticsAnalysisEngine.MeasureTracks(sound);
        Assert.NotNull(tracks.Derived);
        var fromTracks = RawAnalysisAssembler.Assemble(tracks);
        var derivedHere = RawAnalysisAssembler.Assemble(tracks with { Derived = null });
        Assert.Equal(derivedHere.Formants, fromTracks.Formants);
        Assert.Equal(derivedHere.FormantFramesTried, fromTracks.FormantFramesTried);
        Assert.Equal(derivedHere.WeightFrames, fromTracks.WeightFrames);

        // A copy with another gate must not reuse the gate made for the first one.
        var gate0 = RawAnalysisAssembler.Assemble(tracks with { LoudOutlierFraction = 0 });
        var measured0 = RawAnalysisAssembler.Assemble(AcousticsAnalysisEngine.MeasureTracks(sound, 0));
        Assert.Equal(measured0.Formants, gate0.Formants);
        Assert.Equal(measured0.FormantFramesTried, gate0.FormantFramesTried);
    }

    [Fact]
    public void QuickLiveUpdatesLeaveOutTheDerivedTracks()
    {
        var sound = Fixture("vctk_f294");
        var signal = new LiveSignal(sound.SamplingFrequency);
        var provider = new LiveTrackProvider(sound.SamplingFrequency);
        signal.Append(sound.Channel(0)[..(int)(3 * sound.SamplingFrequency)]);

        var quick = provider.Update(signal.View(), TrackSet.Live, final: false)!.Value;
        Assert.Equal(TrackSet.Live, quick.Tracks);
        Assert.False(quick.Derived.Tracks.Has(TrackSet.VowelFormantRows));
        Assert.NotEmpty(quick.Derived.Voiced);
        Assert.Empty(quick.Ltas);

        var full = provider.Update(signal.View(), TrackSet.All, final: false)!.Value;
        Assert.Equal(TrackSet.LiveFull, full.Tracks); // the LTAS waits for the final update
        Assert.True(full.Derived.Tracks.Has(TrackSet.VowelFormantRows | TrackSet.WeightFrames));

        var final = provider.Update(signal.View(), TrackSet.All, final: true)!.Value;
        Assert.Equal(TrackSet.All, final.Tracks);
        Assert.NotEmpty(final.Ltas);
    }
}
