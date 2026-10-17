using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Shared.State;

/// <summary>
/// The take in focus: the one picked in the switcher or a card, otherwise the newest. A pick
/// that disappears (deleted, or gone from the folder) falls back to the newest.
/// </summary>
public interface IActiveTakeService
{
    /// <summary>The take shown, or null when there are none.</summary>
    Recording? Active { get; }

    /// <summary>The take shown is the newest one.</summary>
    bool IsLatest { get; }

    /// <summary>Shows take <paramref name="id"/>.</summary>
    void Select(int id);

    /// <summary>Shows the newest take, now and after the next reload (e.g. once a new take is saved).</summary>
    void SelectLatest();

    /// <summary>
    /// Raised (on the UI thread) when the take shown changes, and after every reload of the
    /// catalog, since the take's files may have changed.
    /// </summary>
    event EventHandler? Changed;
}

public sealed class ActiveTakeService : IActiveTakeService
{
    private readonly ITakeCatalog _catalog;
    private int? _selectedId;

    public ActiveTakeService(ITakeCatalog catalog)
    {
        _catalog = catalog;
        _catalog.Changed += (_, _) =>
        {
            if (_selectedId is { } id && _catalog.Takes.All(r => r.Id != id)) _selectedId = null;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public Recording? Active => (_selectedId is { } id ? _catalog.Takes.FirstOrDefault(r => r.Id == id) : null) ?? _catalog.Latest;
    public bool IsLatest => Active is { } a && a.Id == _catalog.Latest?.Id;

    public event EventHandler? Changed;

    public void Select(int id) => SetSelected(id);

    public void SelectLatest() => SetSelected(null);

    private void SetSelected(int? id)
    {
        var before = Active?.Id;
        _selectedId = id;
        if (Active?.Id != before) Changed?.Invoke(this, EventArgs.Empty);
    }
}
