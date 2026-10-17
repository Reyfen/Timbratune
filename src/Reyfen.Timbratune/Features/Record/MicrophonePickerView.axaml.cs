using Avalonia.Controls;

namespace Reyfen.Timbratune.Features.Record;

public partial class MicrophonePickerView : UserControl
{
    public MicrophonePickerView() => InitializeComponent();

    // A microphone plugged in since the list was made shows up when the dropdown opens.
    private void OnDropDownOpened(object? sender, EventArgs e) => (DataContext as MicrophonePickerViewModel)?.Refresh();
}
