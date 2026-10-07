using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Reyfen.Timbratune.Diagnostics;

/// <summary>
/// Timing probes for profiling builds. Off (and nearly free) unless a head sets
/// <see cref="Sink"/>; then each measured step and every UI-thread stall is written
/// to it as a line such as <c>PERF live.update 12.3</c> (milliseconds), so a PC can
/// collect them (on Android: <c>adb logcat -s Timbratune</c>).
/// </summary>
public static class Perf
{
    /// <summary>Where measurements go (e.g. Android's log); null = probes off.</summary>
    public static Action<string>? Sink
    {
        get => s_sink;
        set
        {
            s_sink = value;
            // The analysis library reports its own steps through the same sink.
            Core.Diagnostics.Timing.Sink = value is null ? null : (name, ms) => Report(name, ms);
        }
    }

    private static Action<string>? s_sink;

    public static bool Enabled => Sink is not null;

    /// <summary>Times the block until the returned scope is disposed: <c>using (Perf.Measure("x")) …</c>.</summary>
    public static Scope Measure(string name) =>
        Enabled ? new(name, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

    public static void Report(string name, double milliseconds) =>
        Sink?.Invoke($"PERF {name} {milliseconds.ToString("0.0", CultureInfo.InvariantCulture)}");

    public static void Note(string text) => Sink?.Invoke("PERF-NOTE " + text);

    /// <summary>Experiment switches for A/B runs (profiling builds), e.g. "noshadow".</summary>
    public static IReadOnlySet<string> Flags { get; set; } = new HashSet<string>();

    /// <summary>
    /// Applies the visual experiments named in <see cref="Flags"/>: "noshadow" (cards without
    /// their blurred shadow), "flatbg" (plain page background instead of the gradient),
    /// "overlay" (Avalonia's frame-time overlay, on <paramref name="top"/>).
    /// </summary>
    public static void ApplyExperiments(Avalonia.Application app, Avalonia.Controls.TopLevel? top)
    {
        if (Flags.Count == 0) return;
        Note("flags: " + string.Join(",", Flags));
        // Cap the background threads (analysis) to see how much they starve the UI thread.
        foreach (var cap in new[] { 2, 4 })
            if (Flags.Contains("pool" + cap))
            {
                ThreadPool.SetMinThreads(1, 1);
                ThreadPool.SetMaxThreads(cap, cap);
            }
        if (Flags.Contains("noshadow"))
            app.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Border>().Class("card"))
            {
                Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Border.BoxShadowProperty, new Avalonia.Media.BoxShadows()) },
            });
        if (Flags.Contains("flatbg") && app.TryFindResource("BgColor", app.ActualThemeVariant, out var bg) && bg is Avalonia.Media.Color c)
            app.Resources["PageBackgroundBrush"] = new Avalonia.Media.SolidColorBrush(c);
        // Cheaper look-alikes for the card shadow and the page gradient.
        foreach (var (flag, shadow) in new[] { ("shadow0", "0 4 0 0 #22BA8EC4"), ("shadow8", "0 4 8 0 #22BA8EC4") })
            if (Flags.Contains(flag))
                app.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Border>().Class("card"))
                {
                    Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Border.BoxShadowProperty, Avalonia.Media.BoxShadows.Parse(shadow)) },
                });
        if (Flags.Contains("grad2") && app.TryFindResource("BgColor", app.ActualThemeVariant, out var b2) && b2 is Avalonia.Media.Color bc
            && app.TryFindResource("BgGlow1Color", app.ActualThemeVariant, out var g2) && g2 is Avalonia.Media.Color gc)
            app.Resources["PageBackgroundBrush"] = new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
                GradientStops = { new Avalonia.Media.GradientStop(gc, 0), new Avalonia.Media.GradientStop(bc, 1) },
            };
        if (Flags.Contains("bmpbg") && app.TryFindResource("PageBackgroundBrush", app.ActualThemeVariant, out var pb) && pb is Avalonia.Media.IBrush brush)
        {
            // The gradient drawn once into a small bitmap, stretched over the page.
            var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(54, 117));
            using (var dc = bitmap.CreateDrawingContext())
                dc.FillRectangle(brush, new Avalonia.Rect(0, 0, 54, 117));
            app.Resources["PageBackgroundBrush"] = new Avalonia.Media.ImageBrush(bitmap) { Stretch = Avalonia.Media.Stretch.Fill };
        }
        if (Flags.Contains("overlay") && top is not null)
            top.RendererDiagnostics.DebugOverlays = Avalonia.Rendering.RendererDebugOverlays.Fps | Avalonia.Rendering.RendererDebugOverlays.RenderTimeGraph;
    }

    /// <summary>Reports the time and, as "alloc." + name in KB, the memory allocated on this thread.</summary>
    public readonly struct Scope(string name, long start, long allocated) : IDisposable
    {
        public void Dispose()
        {
            if (start == 0) return;
            Report(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Report("alloc." + name, (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0);
        }
    }

    /// <summary>
    /// Watches how long the UI thread takes to run a tiny job posted every 16 ms (a
    /// frame). Anything over that is time the UI couldn't react or draw: each stall over
    /// 32 ms is reported as <c>ui.stall</c> with its length.
    /// </summary>
    public static void StartStallWatch()
    {
        if (!Enabled) return;
        var thread = new Thread(() =>
        {
            using var done = new AutoResetEvent(false);
            while (true)
            {
                var posted = Stopwatch.GetTimestamp();
                Dispatcher.UIThread.Post(() => done.Set(), DispatcherPriority.Input);
                done.WaitOne();
                var waited = Stopwatch.GetElapsedTime(posted).TotalMilliseconds;
                if (waited > 32) Report("ui.stall", waited);
                Thread.Sleep(16);
            }
        }) { IsBackground = true, Name = "Perf stall watch", Priority = ThreadPriority.AboveNormal };
        thread.Start();

        // Garbage collection: collections per generation, pause time and allocation rate every 5 s.
        var gc = new Thread(() =>
        {
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            var pause = GC.GetTotalPauseDuration();
            var allocated = GC.GetTotalAllocatedBytes();
            while (true)
            {
                Thread.Sleep(5000);
                int n0 = GC.CollectionCount(0), n1 = GC.CollectionCount(1), n2 = GC.CollectionCount(2);
                var p = GC.GetTotalPauseDuration();
                var a = GC.GetTotalAllocatedBytes();
                Note($"gc 5s: gen0 {n0 - g0}, gen1 {n1 - g1}, gen2 {n2 - g2}, pause {(p - pause).TotalMilliseconds:0} ms, allocated {(a - allocated) / 1048576.0:0} MB");
                Report("gc.pause5s", (p - pause).TotalMilliseconds);
                (g0, g1, g2, pause, allocated) = (n0, n1, n2, p, a);
            }
        }) { IsBackground = true, Name = "Perf gc watch" };
        gc.Start();
    }
}
