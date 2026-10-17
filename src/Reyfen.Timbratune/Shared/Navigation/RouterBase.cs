namespace Reyfen.Timbratune.Shared.Navigation;

/// <summary>
/// Base of a feature's router: the feature's own ways to open and close what it shows over the
/// page (e.g. <c>OpenMetric(id)</c>, <c>OpenTakeActions(takeId)</c>). Routers hold no business
/// logic; they build the overlay's view model and hand it to the <see cref="IOverlayHost"/>.
/// </summary>
public abstract class RouterBase(IOverlayHost overlays)
{
    protected IOverlayHost Overlays { get; } = overlays;

    /// <summary>Shows <paramref name="overlay"/> in front.</summary>
    protected void Open(object overlay) => Overlays.Push(overlay);

    /// <summary>Closes <paramref name="overlay"/> if it is open (the host disposes it).</summary>
    protected void Close(object overlay) => Overlays.Close(overlay);

    /// <summary>Closes every overlay of type <typeparamref name="T"/> (one dialog of a kind at a time).</summary>
    protected void CloseAll<T>() where T : class
    {
        for (var i = Overlays.Overlays.Count - 1; i >= 0; i--)
            if (i < Overlays.Overlays.Count && Overlays.Overlays[i] is T overlay)
                Close(overlay);
    }
}
