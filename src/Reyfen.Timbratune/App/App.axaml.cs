using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shell;

namespace Reyfen.Timbratune.App;

/// <summary>
/// The Avalonia application. A head builds the services (<see cref="CompositionRoot.AddTimbratune"/>
/// plus its own platform adapters) and passes the container in; the app only resolves from it.
/// </summary>
public sealed class TimbratuneApp : Application
{
    private readonly IServiceProvider? _services;

    /// <summary>For the XAML previewer only: no services, no view model.</summary>
    public TimbratuneApp()
    {
    }

    public TimbratuneApp(IServiceProvider services) => _services = services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (_services?.GetRequiredService<IPlatformInfo>().IsMobile == true) UseMobileLook();
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
        var services = _services ?? throw new InvalidOperationException("The platform head must create TimbratuneApp with its services.");
        var locator = services.GetRequiredService<ViewLocator>();
        DataTemplates.Add(locator);
        RequestedThemeVariant = ShellSettings.VariantOf(services.GetRequiredService<ISettings>().Get(ShellSettings.Theme));
        // Resolved only now: some services (PlaybackService's DispatcherTimer) need the UI thread set up.
        var dialogs = services.GetRequiredService<StorageProviderDialogs>();
        var vm = services.GetRequiredService<ShellViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = vm };
            dialogs.Attach(window);
            desktop.MainWindow = window;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            // Android: the app is built before any Activity, and each (re-)created Activity asks
            // for its view. They all get the same one, so nothing is lost when one is re-created.
            // The previous Activity's window can still hold it when the next one asks (Avalonia
            // 12 detaches it only later), so it is taken out of that window first.
            var view = locator.Build(vm);
            dialogs.Attach(view);
            activity.MainViewFactory = () =>
            {
                if (view.Parent is ContentControl previous) previous.Content = null;
                if (view.GetVisualParent() is Avalonia.Controls.Presenters.ContentPresenter presenter) presenter.Content = null;
                return view;
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            var view = locator.Build(vm);
            dialogs.Attach(view);
            single.MainView = view;
        }

        _ = vm.StartAsync();
        base.OnFrameworkInitializationCompleted();
    }
}
