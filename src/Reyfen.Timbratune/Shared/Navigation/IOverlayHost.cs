using CommunityToolkit.Mvvm.ComponentModel;

namespace Reyfen.Timbratune.Shared.Navigation;

/// <summary>
/// The one stack of overlays over the page (metric comparison, take dialog, later sheets),
/// held by the shell. Overlays are view models; the shell's view shows <see cref="Top"/>.
/// Esc and Android's back key close the top one. Features open and close overlays through
/// their routers (<see cref="RouterBase"/>), never through each other.
/// </summary>
public interface IOverlayHost : System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>The overlay in front, or null when the page is uncovered.</summary>
    object? Top { get; }

    /// <summary>Something is open over the page.</summary>
    bool IsOpen { get; }

    /// <summary>All open overlays, bottom first.</summary>
    IReadOnlyList<object> Overlays { get; }

    /// <summary>Puts <paramref name="overlay"/> in front (moving it there if it is already open).</summary>
    void Push(object overlay);

    /// <summary>Removes <paramref name="overlay"/> wherever it is, and disposes it if it is disposable; false if it wasn't open.</summary>
    bool Close(object overlay);

    /// <summary>Closes the overlay in front (Esc, back); false if none was open.</summary>
    bool CloseTop();

    /// <summary>Raised on the UI thread after every change.</summary>
    event EventHandler? Changed;
}

/// <summary>The stack itself. Not thread-safe: used from the UI thread.</summary>
public sealed partial class OverlayHost : ObservableObject, IOverlayHost
{
    private readonly List<object> _overlays = [];

    public object? Top => _overlays.Count > 0 ? _overlays[^1] : null;
    public bool IsOpen => _overlays.Count > 0;
    public IReadOnlyList<object> Overlays => _overlays;

    public event EventHandler? Changed;

    public void Push(object overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (_overlays.Count > 0 && ReferenceEquals(_overlays[^1], overlay)) return;
        _overlays.Remove(overlay);
        _overlays.Add(overlay);
        Notify();
    }

    public bool Close(object overlay)
    {
        if (!_overlays.Remove(overlay)) return false;
        Notify();
        (overlay as IDisposable)?.Dispose();
        return true;
    }

    public bool CloseTop() => Top is { } top && Close(top);

    private void Notify()
    {
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(IsOpen));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
