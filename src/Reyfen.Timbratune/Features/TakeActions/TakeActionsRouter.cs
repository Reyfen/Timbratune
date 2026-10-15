using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.TakeActions;

/// <summary>
/// Opens a take's dialog over the page; one at a time (opening one closes the one before). When
/// the takes are reloaded the dialog closes, as its take may have changed or gone.
/// </summary>
public sealed class TakeActionsRouter : RouterBase, ITakeActionsLauncher
{
    private readonly TakeActionsManager _manager;

    public TakeActionsRouter(IOverlayHost overlays, TakeActionsManager manager, ITakeCatalog catalog) : base(overlays)
    {
        _manager = manager;
        catalog.Changed += (_, _) => CloseAll<TakeDialogViewModel>();
    }

    public void OpenSaveMenu(int takeId) => Open(takeId, TakeDialogKind.SaveMenu);
    public void OpenRename(int takeId) => Open(takeId, TakeDialogKind.Rename);
    public void OpenDelete(int takeId) => Open(takeId, TakeDialogKind.Delete);

    private void Open(int takeId, TakeDialogKind kind)
    {
        if (_manager.Find(takeId) is not { } take) return;
        CloseAll<TakeDialogViewModel>();
        Open(new TakeDialogViewModel(take, kind, _manager, dialog => Close(dialog)));
    }
}

public static class TakeActionsFeature
{
    public static IServiceCollection AddTakeActionsFeature(this IServiceCollection services)
    {
        services.AddSingleton<TakeActionsManager>();
        services.AddSingleton<TakeActionsRouter>();
        services.AddSingleton<ITakeActionsLauncher>(sp => sp.GetRequiredService<TakeActionsRouter>());
        return services;
    }
}
