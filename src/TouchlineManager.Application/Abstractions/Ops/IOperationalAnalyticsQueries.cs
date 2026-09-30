namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>A read of the two operational funnels at one instant (master plan §16 Stage 13, `F-54`).</summary>
/// <remarks>
/// Counts, not rows. The snapshot carries the world and season it was read against so a funnel is never
/// shown without its context, and a server instant so a stale read is visible (`VOI-4`).
/// </remarks>
/// <param name="WorldId">The world the counts are about.</param>
/// <param name="SeasonNumber">The season in progress when the counts were read.</param>
/// <param name="GeneratedAt">The instant the counts were read.</param>
/// <param name="Onboarding">The onboarding funnel's step counts.</param>
/// <param name="Retention">The retention funnel's counts.</param>
public sealed record OperationalAnalyticsSnapshot(
    Guid WorldId,
    int SeasonNumber,
    DateTimeOffset GeneratedAt,
    OnboardingFunnelCounts Onboarding,
    RetentionFunnelCounts Retention);

/// <summary>How far the membership has walked the onboarding funnel.</summary>
/// <param name="Registered">Accounts that have registered, excluding anonymized ones.</param>
/// <param name="Verified">Accounts that have confirmed an email address.</param>
/// <param name="ProfilesCreated">Manager profiles created.</param>
/// <param name="ClubsClaimed">Managers currently holding a club.</param>
public sealed record OnboardingFunnelCounts(
    int Registered,
    int Verified,
    int ProfilesCreated,
    int ClubsClaimed);

/// <summary>Who is still holding a club and who has been seen recently.</summary>
/// <param name="ActiveTenures">Open tenures in the active control state.</param>
/// <param name="InactiveTenures">Open tenures handed to the AI, which may still resume.</param>
/// <param name="ClosedTenures">Tenures that have ended.</param>
/// <param name="ActiveAccounts">Accounts in good standing.</param>
/// <param name="AccountsActiveInLastSevenDays">Accounts last seen authenticating in the last seven days.</param>
public sealed record RetentionFunnelCounts(
    int ActiveTenures,
    int InactiveTenures,
    int ClosedTenures,
    int ActiveAccounts,
    int AccountsActiveInLastSevenDays);

/// <summary>
/// The read side of the operational funnels (master plan §16 Stage 13, `LGL-5`, ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// A projection, not an aggregate: it is one query shaped for an operator, it adds no personal data, and
/// it is never used to make a decision (`MOD-3`). The counts are derived from rows the game already writes
/// — accounts, manager profiles, and tenures — so the surface collects nothing new and has no table of its
/// own.
/// </para>
/// <para>
/// It reads the auth module's accounts, which is a cross-module read and therefore permitted; the same
/// arrangement onboarding already uses to count occupancy (`MOD-3`).
/// </para>
/// </remarks>
public interface IOperationalAnalyticsQueries
{
    /// <summary>Reads the two funnels, or returns null when no world has been seeded.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationalAnalyticsSnapshot?> GetAsync(CancellationToken cancellationToken);
}
