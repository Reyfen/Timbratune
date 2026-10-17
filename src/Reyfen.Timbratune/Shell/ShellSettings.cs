using Avalonia.Styling;
using Reyfen.Timbratune.Core.Storage.Settings;

namespace Reyfen.Timbratune.Shell;

/// <summary>The page's own settings. Device-only for now (the owner's choice, 2026-10-15): which ones sync is decided with accounts.</summary>
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
