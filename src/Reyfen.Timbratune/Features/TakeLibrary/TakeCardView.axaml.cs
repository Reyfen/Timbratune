using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Reyfen.Timbratune.Features.TakeLibrary;

public partial class TakeCardView : UserControl
{
    public TakeCardView() => InitializeComponent();

    // Clicking the card background (not one of its buttons / the waveform)
    // selects this take, like the wrapping div's onClick in App.tsx.
    private void OnCardClicked(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if (e.Source is Controls.WaveformView) return;
        if (DataContext is TakeCardViewModel item) item.SelectCommand.Execute(null);
    }
}
