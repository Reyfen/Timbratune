namespace Reyfen.Timbratune.Shared.Platform;

/// <summary>
/// Lowers the OS priority of the calling thread, so background work (the live analysis) leaves
/// the UI and the renderer their CPU time. Android uses its background priority; elsewhere the
/// default does nothing.
/// </summary>
public interface IThreadPriority
{
    void LowerCurrentThread();
}

/// <summary>Leaves the priority as it is.</summary>
public sealed class DefaultThreadPriority : IThreadPriority
{
    public void LowerCurrentThread()
    {
    }
}
