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

/// <summary>A take's analysis files were rewritten (e.g. trends or series backfilled): whoever shows it reloads it.</summary>
public sealed record TakeAnalysisUpdated(int TakeId);

/// <summary>Take <paramref name="TakeId"/> was analyzed before the time trends existed: its audio can be analyzed again.</summary>
public sealed record TakeTrendsOutdated(int TakeId);

/// <summary>A recording started (e.g. an import's note about the takes list is old news now).</summary>
public sealed record RecordingStarted;

/// <summary>
/// What an action on take <paramref name="TakeId"/> is doing or how it went, for the take's card:
/// <paramref name="Text"/> null = nothing to say; <paramref name="Busy"/> = an export is running.
/// </summary>
public sealed record TakeActionStatus(int TakeId, string? Text, bool Busy);
