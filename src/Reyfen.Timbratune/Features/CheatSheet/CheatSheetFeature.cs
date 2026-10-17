using Microsoft.Extensions.DependencyInjection;

namespace Reyfen.Timbratune.Features.CheatSheet;

/// <summary>
/// "💡 What do these mean?": static text, so only a view (<see cref="CheatSheetView"/>), placed by
/// the Shell. No view model, manager or router.
/// </summary>
public static class CheatSheetFeature
{
    public static IServiceCollection AddCheatSheetFeature(this IServiceCollection services) => services;
}
