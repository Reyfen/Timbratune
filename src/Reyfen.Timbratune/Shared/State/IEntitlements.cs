namespace Reyfen.Timbratune.Shared.State;

/// <summary>
/// What the user's support unlocks (REFACTOR-PLAN §6). Accounts don't exist yet, so the default
/// (<see cref="NoEntitlements"/>) answers no; the account feature will provide the real one.
/// </summary>
public interface IEntitlements
{
    /// <summary>The user has donated at least once (any tier, any time): such installs are always in the telemetry rollout.</summary>
    bool HasEverDonated { get; }
}

public sealed class NoEntitlements : IEntitlements
{
    public static readonly NoEntitlements Instance = new();

    private NoEntitlements()
    {
    }

    public bool HasEverDonated => false;
}
