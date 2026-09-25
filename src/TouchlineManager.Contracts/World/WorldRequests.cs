namespace TouchlineManager.Contracts.World;

/// <summary>Request to create the account's manager profile (master plan §10.2).</summary>
/// <remarks>
/// One profile per account. The manager's name is the account's display name, so it is deliberately not
/// repeated here: two names for one person would raise the question of which one a league table shows.
/// </remarks>
public sealed record CreateManagerProfileRequest
{
    /// <summary>Gets the preferred locale for formatting, e.g. <c>en-GB</c>.</summary>
    public required string Locale { get; init; }

    /// <summary>Gets the IANA time zone used to render deadlines in local time (`CAL-4`).</summary>
    public required string TimeZone { get; init; }
}

/// <summary>Request to take over an AI-controlled club (master plan §7.6).</summary>
/// <remarks>
/// The command identifies the club only. Ownership is never taken from the body: the manager is derived
/// from the authenticated account, and the country, tier, and current control of the club are read from
/// the database (master plan §10.9).
/// </remarks>
public sealed record ClaimClubRequest
{
    /// <summary>Gets the club to take over.</summary>
    public required Guid ClubId { get; init; }
}
