using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Reyfen.Timbratune.Shell;

public partial class ShellView : UserControl
{
    private TopLevel? _topLevel;

    public ShellView() => InitializeComponent();

    // Android's back button / gesture closes what is open over the page, like Esc.
    // (The view can move to a new TopLevel when the Activity is re-created.)
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.BackRequested += OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null) _topLevel.BackRequested -= OnBackRequested;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm || !vm.Overlays.IsOpen) return;
        vm.CloseOverlayCommand.Execute(null);
        e.Handled = true;
    }
}
