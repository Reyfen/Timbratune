using Reyfen.Timbratune.Core.Domain;

namespace Reyfen.Timbratune.Shared.Navigation;

// What one feature may open in another, without knowing its types. Each is implemented by the
// owning feature's router and registered by its Add<Feature>Feature().

/// <summary>A take's dialog over the page: save or export, rename, delete (the TakeActions feature).</summary>
public interface ITakeActionsLauncher
{
    void OpenSaveMenu(int takeId);
    void OpenRename(int takeId);
    void OpenDelete(int takeId);
}

/// <summary>The comparison of one metric across takes and reference voices (the MetricCompare feature).</summary>
public interface IMetricCompareLauncher
{
    void Open(MetricKey metric);
}
