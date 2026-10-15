using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Shared.Navigation;

namespace Reyfen.Timbratune.Features.Import;

/// <summary>Opens the FFmpeg offer over the page (one at a time).</summary>
public sealed class ImportRouter(IOverlayHost overlays, ImportManager manager) : RouterBase(overlays)
{
    /// <summary>Shows the offer; completes when it is closed, whatever was chosen.</summary>
    public Task OfferFfmpegAsync()
    {
        CloseAll<FfmpegOfferViewModel>();
        var offer = new FfmpegOfferViewModel(manager, o => Close(o));
        Open(offer);
        return offer.Closed;
    }
}

/// <summary>The Import feature's settings.</summary>
public static class ImportSettings
{
    /// <summary>"Not now" was chosen on the FFmpeg offer: it isn't shown unasked again. Device-only (it is about this machine).</summary>
    public static readonly SettingKey<bool> FfmpegOfferDeclined =
        new("import.ffmpegOfferDeclined", false, SettingScope.Device, SettingsJson.Default.Boolean);
}
