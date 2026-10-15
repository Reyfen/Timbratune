using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis;

namespace Reyfen.Timbratune.Architecture.Tests;

/// <summary>
/// The dependency rules of REFACTOR-PLAN.md: §1.1 (feature modules), §5 (analytics stays out of
/// live analysis) and §7 (nothing a browser head would need references native-only code).
/// Several rules have nothing to check until the phases that add feature folders land; they
/// hold vacuously until then and start biting as soon as the first type appears.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string FeaturesNs = "Reyfen.Timbratune.Features";

    private static readonly Assembly Acoustics = typeof(Sound).Assembly;
    private static readonly Assembly Core = typeof(LiveAnalyzer).Assembly;
    private static readonly Assembly Ui = typeof(Reyfen.Timbratune.App.TimbratuneApp).Assembly;

    /// <summary>Assemblies only a native head may use: a browser head couldn't load them.</summary>
    private static readonly string[] NativeOnly =
    [
        "SoundFlow", "Mono.Android", "Java.Interop", "Avalonia.Desktop", "Avalonia.Android", "Avalonia.iOS", "Avalonia.Native",
        "Avalonia.Win32", "Avalonia.X11", "Avalonia.FreeDesktop", "Avalonia.LinuxFramebuffer",
        "Reyfen.Timbratune.Audio.", "Reyfen.Timbratune.Desktop", "Reyfen.Timbratune.Android",
    ];

    // ---- Assembly-level rules (§1.1, §7) ----

    [Fact]
    public void AcousticsReferencesNothingButTheRuntime() =>
        Assert.Empty(Acoustics.GetReferencedAssemblies().Where(a => !IsRuntime(a)).Select(a => a.Name));

    [Fact]
    public void CoreReferencesOnlyAcousticsAndTheRuntime() =>
        Assert.Empty(Core.GetReferencedAssemblies().Where(a => !IsRuntime(a) && a.Name != Acoustics.GetName().Name).Select(a => a.Name));

    [Fact]
    public void WhatABrowserHeadNeedsReferencesNoNativeOnlyCode()
    {
        foreach (var assembly in new[] { Acoustics, Core, Ui })
            Assert.Empty(assembly.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => NativeOnly.Any(p => p.EndsWith('.') ? n.StartsWith(p, StringComparison.Ordinal) : n == p || n.StartsWith(p + ".", StringComparison.Ordinal)))
                .Select(n => $"{assembly.GetName().Name} → {n}"));
    }

    // ---- Feature modules (§1.1) ----

    [Fact]
    public void FeaturesNeverReferenceOtherFeatures()
    {
        var violations = new List<string>();
        foreach (var type in UiTypes())
        {
            if (FeatureOf(type.Namespace) is not { } own) continue;
            foreach (var used in TypeReferences.Of(type))
                if (FeatureOf(Namespace(used)) is { } other && other != own)
                    violations.Add($"{type.FullName} → {used}");
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void OnlyTheCompositionRootAndTheShellSeeFeatures()
    {
        var violations = new List<string>();
        foreach (var type in UiTypes())
        {
            var ns = type.Namespace ?? "";
            if (FeatureOf(ns) is not null || InNamespace(ns, "Reyfen.Timbratune.App") || InNamespace(ns, "Reyfen.Timbratune.Shell")) continue;
            // Avalonia's compiled-XAML loaders (generated, they reach every view) and namespace-less compiler types.
            if (ns.Length == 0 || InNamespace(ns, "CompiledAvaloniaXaml")) continue;
            violations.AddRange(TypeReferences.Of(type).Where(u => FeatureOf(Namespace(u)) is not null).Select(u => $"{type.FullName} → {u}"));
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void ManagersNeverReferenceAvalonia()
    {
        var violations = new List<string>();
        foreach (var type in UiTypes().Where(t => FeatureOf(t.Namespace) is not null && Outermost(t).Name.EndsWith("Manager", StringComparison.Ordinal)))
            violations.AddRange(TypeReferences.Of(type).Where(u => u.StartsWith("Avalonia", StringComparison.Ordinal)).Select(u => $"{type.FullName} → {u}"));
        Assert.Empty(violations);
    }

    [Fact]
    public void FeatureViewModelsDoNoFileIoAndDontUseTheStore()
    {
        string[] forbidden =
        [
            "Reyfen.Timbratune.Core.Storage.RecordingStore", "System.IO.File", "System.IO.Directory", "System.IO.FileInfo",
            "System.IO.DirectoryInfo", "System.IO.FileStream",
        ];
        var violations = new List<string>();
        foreach (var type in UiTypes().Where(t => FeatureOf(t.Namespace) is not null && Outermost(t).Name.EndsWith("ViewModel", StringComparison.Ordinal)))
            violations.AddRange(TypeReferences.Of(type).Where(forbidden.Contains).Select(u => $"{type.FullName} → {u}"));
        Assert.Empty(violations);
    }

    [Fact]
    public void FeaturesUseSettingsOnlyThroughISettings()
    {
        // §5a: features read and write settings through the service, never its files.
        string[] forbidden =
        [
            "Reyfen.Timbratune.Core.Storage.Settings.SettingsService", "Reyfen.Timbratune.Core.Storage.Settings.ISettingsStore",
            "Reyfen.Timbratune.Core.Storage.Settings.DataStoreSettingsStore", "Reyfen.Timbratune.Core.Storage.IDataStore",
            "Reyfen.Timbratune.Core.Storage.FileSystemDataStore",
        ];
        var violations = new List<string>();
        foreach (var type in UiTypes().Where(t => FeatureOf(t.Namespace) is not null))
            violations.AddRange(TypeReferences.Of(type).Where(forbidden.Contains).Select(u => $"{type.FullName} → {u}"));
        Assert.Empty(violations);
    }

    [Fact]
    public void FeaturesDecodeOnlyThroughTheRegistry()
    {
        // §4.1: which decoders exist is the heads' business; features see DecoderRegistry and IDecoderInstaller.
        var decoding = typeof(Reyfen.Timbratune.Core.Audio.Decoding.DecoderRegistry);
        var forbidden = Core.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.Name is "IAudioDecoderProvider" or "IAudioDecoder") || t.Name is "IAudioDecoderProvider" or "IAudioDecoder" or "AnalysisWavSink")
            .Select(t => t.FullName!).ToHashSet();
        Assert.Contains(decoding.Namespace + ".ManagedDecoderProvider", forbidden); // the rule sees the providers
        var violations = new List<string>();
        foreach (var type in UiTypes().Where(t => FeatureOf(t.Namespace) is not null))
            violations.AddRange(TypeReferences.Of(type).Where(forbidden.Contains).Select(u => $"{type.FullName} → {u}"));
        Assert.Empty(violations);
    }

    [Fact]
    public void FeatureViewsDontBindToAParentsViewModel()
    {
        // `$parent[v:MainView].((vm:MainViewModel)DataContext)` couples a view to another one's
        // view model; feature views bind only to their own (§1.1).
        var features = Path.Combine(RepoRoot(), "src", "Reyfen.Timbratune", "Features");
        if (!Directory.Exists(features)) return;
        var parentBinding = new Regex(@"\$parent\[[^\]]*\][^""}]*DataContext", RegexOptions.CultureInvariant);
        var violations = Directory.EnumerateFiles(features, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(x => parentBinding.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(RepoRoot(), x.f)}:{x.i + 1}");
        Assert.Empty(violations);
    }

    // ---- Analytics (§5) ----

    [Fact]
    public void LiveAnalysisNeverReferencesAnalytics()
    {
        var live = TypeReferences.TypesOf(Acoustics).Concat(TypeReferences.TypesOf(Core))
            .Concat(UiTypes().Where(t => InNamespace(t.Namespace ?? "", FeaturesNs + ".LiveMonitor")
                                         || InNamespace(t.Namespace ?? "", "Reyfen.Timbratune.Analysis")
                                         || Outermost(t).Name.StartsWith("Live", StringComparison.Ordinal)));
        var violations = new List<string>();
        foreach (var type in live)
            violations.AddRange(TypeReferences.Of(type).Where(IsAnalytics).Select(u => $"{type.FullName} → {u}"));
        Assert.Empty(violations);
    }

    // ---- The scanner itself (positive controls, so the rules above can't pass by seeing nothing) ----

    [Fact]
    public void ScannerSeesSignaturesAndMethodBodies()
    {
        // LiveAnalyzer keeps its trackers in fields and calls VoiceReport only inside Update.
        var used = TypeReferences.Of(typeof(LiveAnalyzer));
        Assert.Contains("Reyfen.Timbratune.Acoustics.Streaming.LivePitchTracker", used);
        Assert.Contains("Reyfen.Timbratune.Acoustics.Voice.VoiceReport", used);
        // Lambdas and async methods live in nested compiler-generated types.
        // TakeActionsManager uses TakeArchive only in a lambda inside an async method (closure and state machine types).
        var manager = UiTypes().Where(t => Outermost(t).FullName == "Reyfen.Timbratune.Features.TakeActions.TakeActionsManager")
            .SelectMany(TypeReferences.Of).ToHashSet();
        Assert.Contains("Reyfen.Timbratune.Core.Storage.TakeArchive", manager);
        // And the feature rules see feature types at all: the Shell uses them.
        var shell = UiTypes().Where(t => Outermost(t).FullName == "Reyfen.Timbratune.Shell.ShellViewModel").SelectMany(TypeReferences.Of).ToHashSet();
        Assert.Contains(shell, u => u.StartsWith("Reyfen.Timbratune.Features.", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeAssembliesAreToldApartFromPackagesAndProjects()
    {
        Assert.True(IsRuntime(typeof(Regex).Assembly.GetName()));
        Assert.False(IsRuntime(Acoustics.GetName()));
        Assert.False(IsRuntime(typeof(Avalonia.Application).Assembly.GetName()));
    }

    [Theory]
    [InlineData("Reyfen.Timbratune.Features.Recording", "Recording")]
    [InlineData("Reyfen.Timbratune.Features.Recording.Views", "Recording")]
    [InlineData("Reyfen.Timbratune.Features", null)]
    [InlineData("Reyfen.Timbratune.FeaturesX.Recording", null)]
    [InlineData("Reyfen.Timbratune.Shared.State", null)]
    [InlineData(null, null)]
    public void FeatureNamesComeFromTheNamespace(string? ns, string? feature) => Assert.Equal(feature, FeatureOf(ns));

    // ---- Helpers ----

    private static IEnumerable<Type> UiTypes() => TypeReferences.TypesOf(Ui);

    /// <summary>The feature a namespace belongs to (<c>Reyfen.Timbratune.Features.&lt;Name&gt;…</c>), or null.</summary>
    private static string? FeatureOf(string? ns)
    {
        if (ns is null || !ns.StartsWith(FeaturesNs + ".", StringComparison.Ordinal)) return null;
        var rest = ns[(FeaturesNs.Length + 1)..];
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }

    private static bool InNamespace(string ns, string root) => ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

    /// <summary>The namespace part of a full type name (nested types: up to the outermost type).</summary>
    private static string Namespace(string fullName)
    {
        var plus = fullName.IndexOf('+');
        var outer = plus < 0 ? fullName : fullName[..plus];
        var dot = outer.LastIndexOf('.');
        return dot < 0 ? "" : outer[..dot];
    }

    private static Type Outermost(Type t)
    {
        while (t.DeclaringType is { } outer) t = outer;
        return t;
    }

    private static bool IsAnalytics(string fullName)
    {
        var plus = fullName.IndexOf('+');
        var outer = plus < 0 ? fullName : fullName[..plus];
        var segments = outer.Split('.');
        return segments[..^1].Contains("Analytics")
               || segments[^1].Contains("Analytics", StringComparison.Ordinal)
               || segments[^1].Contains("Mixpanel", StringComparison.Ordinal);
    }

    private static readonly string RuntimeDir = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());

    /// <summary>True for an assembly of the shared .NET runtime (not a package or a project).</summary>
    private static bool IsRuntime(AssemblyName name)
    {
        try
        {
            var location = Assembly.Load(name).Location;
            return location.Length > 0 && string.Equals(Path.GetDirectoryName(location), RuntimeDir, StringComparison.OrdinalIgnoreCase);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static string RepoRoot([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
