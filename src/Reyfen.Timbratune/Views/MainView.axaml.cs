using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Reyfen.Timbratune.ViewModels;

namespace Reyfen.Timbratune.Views;

public partial class MainView : UserControl
{
    private TopLevel? _topLevel;

    public MainView() => InitializeComponent();

    // Android's back button / gesture closes an open dialog or the metric modal, like Esc.
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
        if (DataContext is not MainViewModel vm || !(vm.IsTakeDialogOpen || vm.IsModalOpen)) return;
        vm.CloseModalCommand.Execute(null);
        e.Handled = true;
    }
}
