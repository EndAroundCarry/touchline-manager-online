namespace TouchlineManager.Contracts.Competition;

/// <summary>
/// One player's discipline in a division-season: the cards shown and any suspension still owed
/// (master plan §6.4, `DIS-2`…`DIS-5`).
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="PlayerName">The player's generated name.</param>
/// <param name="ClubId">The club the player plays for.</param>
/// <param name="ClubName">The club's generated name.</param>
/// <param name="ClubShortName">The club's abbreviation, for a narrow screen.</param>
/// <param name="YellowCards">Bookings accumulated (`DIS-2`).</param>
/// <param name="RedCards">Sendings-off accumulated (`DIS-4`).</param>
/// <param name="SuspensionFixturesRemaining">
/// How many fixtures the player still misses through suspension, or zero when they owe none (`DIS-5`).
/// </param>
public sealed record DivisionDisciplineRowResponse(
    Guid PlayerId,
    string PlayerName,
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    int YellowCards,
    int RedCards,
    int SuspensionFixturesRemaining);

/// <summary>
/// A division's discipline for the season in progress: every player carrying a card, and any suspension
/// they still owe (master plan §10.5, `DIS-2`…`DIS-5`).
/// </summary>
/// <remarks>
/// The counterpart of the table's card columns: the table answers "which club has been booked most" as a
/// tie-breaker, and this answers "who, and who is out". The rows arrive in the order the server ranked them
/// — most sendings-off, then most bookings, then name — so the screen sorts nothing.
/// </remarks>
/// <param name="DivisionId">The division.</param>
/// <param name="DivisionName">The division's generated name.</param>
/// <param name="TierNumber">The tier.</param>
/// <param name="CountryId">The country.</param>
/// <param name="CountryCode">The country's code.</param>
/// <param name="CountryName">The country's name.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="SeasonLabel">The season's display label.</param>
/// <param name="Rows">Every player with a card this season, most sendings-off first.</param>
/// <param name="ServerTime">
/// The server's current instant, so a client with a wrong clock shows the right "as of" (`TIME-5`).
/// </param>
public sealed record DivisionDisciplineResponse(
    Guid DivisionId,
    string DivisionName,
    int TierNumber,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    int SeasonNumber,
    string SeasonLabel,
    IReadOnlyList<DivisionDisciplineRowResponse> Rows,
    DateTimeOffset ServerTime);
