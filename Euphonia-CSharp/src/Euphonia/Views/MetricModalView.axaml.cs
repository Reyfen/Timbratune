using Avalonia.Controls;
using Avalonia.Input;
using Euphonia.ViewModels;

namespace Euphonia.Views;

public partial class MetricModalView : UserControl
{
    public MetricModalView() => InitializeComponent();

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MetricModalViewModel vm) vm.CloseCommand.Execute(null);
    }
}
