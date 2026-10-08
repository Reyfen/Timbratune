using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Markup.Xaml;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.ViewModels;
using Reyfen.Timbratune.Views;

namespace Reyfen.Timbratune;

public sealed class App : Application
{
    /// <summary>
    /// Set by the platform head before start-up: builds the services (store,
    /// analysis engine, audio). Receives the view layer's file dialogs.
    /// </summary>
    public static Func<IFileDialogs, AppServices>? ServicesFactory { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()) UseMobileLook();
    }

    /// <summary>
    /// Phones repaint the visible page on every scrolled or animated frame, and a blurred
    /// shadow under every card is the costliest thing in it: measured on a Pixel 4a it held
    /// scrolling to ~33 fps. There the card shadow is a crisp edge (no blur, next to free;
    /// ~51 fps together with the simpler page gradient chosen in Palette.axaml).
    /// </summary>
    private void UseMobileLook() =>
        Styles.Add(new Style(x => x.OfType<Border>().Class("card"))
        {
            Setters = { new Setter(Border.BoxShadowProperty, BoxShadows.Parse("0 4 0 0 #22BA8EC4")) },
        });

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

        _ = vm.StartAsync();
        base.OnFrameworkInitializationCompleted();
    }
}
