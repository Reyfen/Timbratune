using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reyfen.Timbratune.ViewModels;

namespace Reyfen.Timbratune.Views;

public partial class TakeDialogView : UserControl
{
    private TopLevel? _topLevel;
    private RecordingItemViewModel? _take;

    public TakeDialogView() => InitializeComponent();

    // Renaming starts with the name selected in a focused box (which brings up a phone's keyboard).
    // Watched on the take, not on the box's visibility: with no dialog open the view has no
    // DataContext, and the box's IsVisible binding then falls back to true, so it never changes.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_take is not null) _take.PropertyChanged -= OnTakeChanged;
        _take = DataContext as RecordingItemViewModel;
        if (_take is null) return;
        _take.PropertyChanged += OnTakeChanged;
        if (_take.IsRenaming) FocusName(attempts: 10);
    }

    private void OnTakeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordingItemViewModel.IsRenaming) && _take?.IsRenaming == true) FocusName(attempts: 10);
    }

    // The dialog opens in the same step as renaming starts, before it is laid out, and a
    // control that isn't shown yet can't take focus: retry briefly until it can.
    private void FocusName(int attempts)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (_take?.IsRenaming != true) return;
            if (RenameBox.IsEffectivelyVisible && RenameBox.Focus(NavigationMethod.Pointer))
            {
                RenameBox.SelectAll();
                return;
            }
            if (attempts > 1) FocusName(attempts - 1);
        }, TimeSpan.FromMilliseconds(50));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel?.InputPane is { } pane) pane.StateChanged += OnInputPaneChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel?.InputPane is { } pane) pane.StateChanged -= OnInputPaneChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    // An on-screen keyboard: keep the card in the part of the page it leaves free.
    private void OnInputPaneChanged(object? sender, InputPaneStateEventArgs e)
    {
        var bottom = 0.0;
        if (_topLevel is not null && e.NewState == InputPaneState.Open && e.EndRect.Height > 0
            && this.TranslatePoint(new Point(0, Bounds.Height), _topLevel) is { } viewBottom)
            bottom = Math.Max(0, viewBottom.Y - e.EndRect.Y);
        Scroller.Margin = new Thickness(0, 0, 0, bottom);
        if (RenameBox.IsFocused) Dispatcher.UIThread.Post(() => RenameBox.BringIntoView(), DispatcherPriority.Background);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => Close();

    // The scroller lies over the scrim: a press anywhere on it but the card is a press on the background.
    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && (source == Card || Card.IsVisualAncestorOf(source))) return;
        Close();
        e.Handled = true;
    }

    private void Close()
    {
        if (DataContext is RecordingItemViewModel vm) vm.CloseDialogCommand.Execute(null);
    }
}
