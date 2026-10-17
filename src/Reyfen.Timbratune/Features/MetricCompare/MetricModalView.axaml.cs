using Avalonia.Controls;
using Avalonia.Input;

namespace Reyfen.Timbratune.Features.MetricCompare;

public partial class MetricModalView : UserControl
{
    public MetricModalView() => InitializeComponent();

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MetricModalViewModel vm) vm.CloseCommand.Execute(null);
    }
}
