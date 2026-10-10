using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Reyfen.Timbratune.ViewModels;

namespace Reyfen.Timbratune.Views;

public partial class RecordingCardView : UserControl
{
    public RecordingCardView()
    {
        InitializeComponent();
        // Renaming starts with the name selected in a focused box.
        RenameRow.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty || !RenameRow.IsVisible) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RenameBox.Focus();
                RenameBox.SelectAll();
            });
        };
    }

    // Clicking the card background (not one of its buttons / the waveform)
    // selects this take, like the wrapping div's onClick in App.tsx.
    private void OnCardClicked(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if (e.Source is Controls.WaveformView) return;
        if (DataContext is RecordingItemViewModel item &&
            this.FindAncestorOfType<MainView>()?.DataContext is MainViewModel main)
            main.SelectCommand.Execute(item);
    }
}
