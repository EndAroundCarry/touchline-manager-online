namespace TouchlineManager.Contracts.Ops;

/// <summary>
/// The operational funnels an operator reads (master plan §16 Stage 13, `F-54`, ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// Every field is a count over rows the server already holds or an instant. There is deliberately no
/// per-manager row, no identifier beyond the world and season, and no hidden value: the response is the
/// shape the data-classification policy asks for — "counts and durations only, never values"
/// (<c>docs/security/data-classification.md</c> §2, `LGL-5`, `MAT-11`).
/// </para>
/// <para>
/// This is an operator surface, not a manager one, and it cannot render a manager's private data because
/// it never carries any. The admin console that displays it arrives with Stage 14 (`F-46`).
/// </para>
/// </remarks>
/// <param name="WorldId">The world the counts are about.</param>
/// <param name="SeasonNumber">The season in progress, so a funnel is read against the same calendar the game is playing.</param>
/// <param name="GeneratedAt">The server's instant when the counts were read.</param>
/// <param name="Onboarding">How far the membership has walked the onboarding funnel.</param>
/// <param name="Retention">How many tenures and accounts are still active.</param>
public sealed record OperationalAnalyticsResponse(
    Guid WorldId,
    int SeasonNumber,
    DateTimeOffset GeneratedAt,
    OnboardingFunnelResponse Onboarding,
    RetentionFunnelResponse Retention);

/// <summary>The onboarding funnel: registered → verified → profile → club (master plan §15, `F-54`).</summary>
/// <remarks>
/// Each step is a count of accounts that have reached it, so the four numbers read as a funnel: a later
/// step can never exceed an earlier one. Anonymized accounts are excluded from the account steps, because
/// they are no longer a manager walking the funnel.
/// </remarks>
/// <param name="Registered">Accounts that have registered.</param>
/// <param name="Verified">Accounts that have confirmed their email address.</param>
/// <param name="ProfilesCreated">Manager profiles created.</param>
/// <param name="ClubsClaimed">Managers currently holding a club, through an open tenure.</param>
public sealed record OnboardingFunnelResponse(
    int Registered,
    int Verified,
    int ProfilesCreated,
    int ClubsClaimed);

/// <summary>The retention funnel: who is still holding a club and who has been seen recently.</summary>
/// <param name="ActiveTenures">Open tenures whose manager is expected to act (`OCC-2`).</param>
/// <param name="InactiveTenures">Open tenures the inactivity ladder has handed to the AI, which the manager may still resume.</param>
/// <param name="ClosedTenures">Tenures that have ended, however they ended.</param>
/// <param name="ActiveAccounts">Accounts in good standing.</param>
/// <param name="AccountsActiveInLastSevenDays">Accounts last seen authenticating within the last seven days.</param>
public sealed record RetentionFunnelResponse(
    int ActiveTenures,
    int InactiveTenures,
    int ClosedTenures,
    int ActiveAccounts,
    int AccountsActiveInLastSevenDays);
