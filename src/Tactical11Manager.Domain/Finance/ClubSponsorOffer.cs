namespace Tactical11Manager.Domain.Finance;

/// <summary>
/// One of the three offers a sponsor makes a club for a season, kept as it was made (`SPN-1`).
/// </summary>
/// <remarks>
/// The figures are a function of the club's fans and standing on the day the season is created, so they are stored with the fans, the standing
/// and the tier they were made from: the bonus a manager sees for an objective next to an offer is worked out from those, and does not move
/// while he decides. An offer is never edited.
/// </remarks>
public sealed class ClubSponsorOffer
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubSponsorOffer()
    {
    }

    /// <summary>Gets the offer identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club the offer is made to.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the season the offer is for.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets which of the three offers this is.</summary>
    public SponsorOfferKind Kind { get; private set; }

    /// <summary>Gets the sponsor's name: fictional, drawn from the world seed.</summary>
    public string SponsorName { get; private set; } = string.Empty;

    /// <summary>Gets what the offer pays over the season whatever happens, in minor units.</summary>
    public long FixedSeasonMinor { get; private set; }

    /// <summary>Gets how much the offer multiplies a bonus by, in basis points.</summary>
    public int BonusMultiplierBp { get; private set; }

    /// <summary>Gets the tier the club plays the season in.</summary>
    public int Tier { get; private set; }

    /// <summary>Gets the club's fans when the offer was made.</summary>
    public int Fans { get; private set; }

    /// <summary>Gets the club's standing in points when the offer was made.</summary>
    public int StandingPoints { get; private set; }

    /// <summary>Gets when the offer was made.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records an offer.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="offer">The offer <see cref="SponsorOffers.For"/> made.</param>
    /// <param name="tier">The tier the club plays the season in.</param>
    /// <param name="fans">The club's fans when the offer was made.</param>
    /// <param name="standingPoints">The club's standing in points when the offer was made.</param>
    /// <param name="now">The current instant.</param>
    public static ClubSponsorOffer Record(
        Guid id,
        Guid clubId,
        Guid seasonId,
        SponsorOffer offer,
        int tier,
        int fans,
        int standingPoints,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.SponsorName);
        ArgumentOutOfRangeException.ThrowIfNegative(offer.FixedSeasonMinor);
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(fans);

        return new ClubSponsorOffer
        {
            Id = id,
            ClubId = clubId,
            SeasonId = seasonId,
            Kind = offer.Kind,
            SponsorName = offer.SponsorName,
            FixedSeasonMinor = offer.FixedSeasonMinor,
            BonusMultiplierBp = offer.BonusMultiplierBp,
            Tier = tier,
            Fans = fans,
            StandingPoints = standingPoints,
            CreatedAt = now,
        };
    }
}
