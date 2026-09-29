using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Euphonia.Services;
using Euphonia.ViewModels;
using Euphonia.Views;

namespace Euphonia;

public sealed class App : Application
{
    /// <summary>
    /// Set by the platform head before start-up: builds the services (store,
    /// Praat engine, audio). Receives the view layer's file dialogs.
    /// </summary>
    public static Func<IFileDialogs, AppServices>? ServicesFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var factory = ServicesFactory ?? throw new InvalidOperationException("App.ServicesFactory must be set by the platform head.");
        var dialogs = new StorageProviderDialogs();
        var vm = new MainViewModel(factory(dialogs));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = vm };
            dialogs.Attach(window);
            desktop.MainWindow = window;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            var view = new MainView { DataContext = vm };
            dialogs.Attach(view);
            single.MainView = view;
        }

        _ = vm.ReloadAsync(selectLatest: true);
        base.OnFrameworkInitializationCompleted();
    }
}
