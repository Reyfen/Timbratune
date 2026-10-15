namespace Reyfen.Timbratune.Shared.Messaging;

// What features tell each other, through CommunityToolkit's IMessenger (registered once by the
// composition root). A feature sends what happened; whoever cares subscribes. Messages carry ids
// and plain values, never view models, so no feature depends on another's types.

/// <summary>A recording was analyzed and saved as take <paramref name="TakeId"/>.</summary>
public sealed record TakeSaved(int TakeId);

/// <summary>Take <paramref name="TakeId"/> got a new label.</summary>
public sealed record TakeRenamed(int TakeId, string Label);

/// <summary>Take <paramref name="TakeId"/> was deleted.</summary>
public sealed record TakeDeleted(int TakeId);

/// <summary>An import ended: <paramref name="Imported"/> takes added, <paramref name="Failed"/> files not.</summary>
public sealed record ImportFinished(int Imported, int Failed);

/// <summary>A take's analysis files were rewritten (e.g. trends or series backfilled).</summary>
public sealed record TakeAnalysisUpdated(int TakeId);
