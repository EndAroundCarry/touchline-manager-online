namespace Tactical11Manager.Application.Abstractions.Finance;

/// <summary>
/// Where a club stands in the sponsor's year: the season, its division, the figures the offers are made from, and how far the season has come
/// (`SPN-1`, `SPN-4`, `SPN-6`).
/// </summary>
/// <param name="ClubId">The club.</param>
/// <param name="ClubName">The club's name.</param>
/// <param name="WorldId">The world, which seeds the sponsors' names.</param>
/// <param name="SeasonId">The world's current season.</param>
/// <param name="SeasonNumber">The season's ordinal in the world.</param>
/// <param name="FirstMatchday">The date of the season's first matchday, which fixes the deadline.</param>
/// <param name="DivisionSeasonId">The club's division in the season.</param>
/// <param name="TierNumber">The tier the club plays in.</param>
/// <param name="Fans">The club's fans.</param>
/// <param name="StandingPoints">The club's standing in points, 1 to 100.</param>
/// <param name="PublishedMatchdays">How many of the division's matchdays have been published.</param>
public sealed record SponsorSeasonContext(
    Guid ClubId,
    string ClubName,
    Guid WorldId,
    Guid SeasonId,
    int SeasonNumber,
    DateOnly FirstMatchday,
    Guid DivisionSeasonId,
    int TierNumber,
    int Fans,
    int StandingPoints,
    int PublishedMatchdays);

/// <summary>
/// The finance module's reads for the sponsor: the context of a club's season, who has not signed, and how long a manager has held a club.
/// </summary>
public interface ISponsorQueries
{
    /// <summary>Reads a club's sponsor context in the world's current season.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The context, or null when the club has no entry in the current season.</returns>
    Task<SponsorSeasonContext?> GetClubContextAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Lists the clubs of a division-season that have been made offers and have not signed one.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> ListClubsWithoutDealAsync(Guid divisionSeasonId, CancellationToken cancellationToken);

    /// <summary>Counts the matchdays of a division-season published after an instant.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="since">The instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountMatchdaysPublishedSinceAsync(Guid divisionSeasonId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Reads when the club's present manager took it over.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The instant, or null when no manager holds the club.</returns>
    Task<DateTimeOffset?> GetTenureStartAsync(Guid clubId, CancellationToken cancellationToken);
}
