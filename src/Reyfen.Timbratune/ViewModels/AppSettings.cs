using Avalonia.Styling;
using Reyfen.Timbratune.Core.Storage.Settings;

namespace Reyfen.Timbratune.ViewModels;

// The settings that persist today. Each feature owns its keys; until the feature folders exist
// (REFACTOR-PLAN P3) they live here, next to the view models that use them. Every key is
// device-only for now (the owner's choice, 2026-10-15): which ones sync is decided with accounts.

/// <summary>The page's own settings (the Shell's, from P3).</summary>
public static class ShellSettings
{
    /// <summary>"system" (follow the OS), "light" or "dark".</summary>
    public static readonly SettingKey<string> Theme = new("shell.theme", "system", SettingScope.Device, SettingsJson.Default.String);

    public static ThemeVariant VariantOf(string theme) => theme switch
    {
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public static string NameOf(ThemeVariant variant) =>
        variant == ThemeVariant.Light ? "light" : variant == ThemeVariant.Dark ? "dark" : "system";
}

/// <summary>The record panel's settings (the Recording feature's, from P3).</summary>
public static class RecordingSettings
{
    /// <summary>How much of the take the live graphs show, in seconds; 0 = the whole take.</summary>
    public static readonly SettingKey<double> LiveWindowSeconds =
        new("recording.liveWindowSeconds", 0, SettingScope.Device, SettingsJson.Default.Double);

    /// <summary>The choice for a stored value; an unknown one (e.g. from a newer version) shows the whole take.</summary>
    public static LiveWindowOption OptionFor(double seconds) =>
        LiveWindowOption.All.FirstOrDefault(o => (o.Seconds ?? 0) == seconds) ?? LiveWindowOption.All[0];
}
