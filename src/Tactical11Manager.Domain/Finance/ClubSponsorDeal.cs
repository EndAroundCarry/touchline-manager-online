using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Finance;

/// <summary>
/// The sponsor deal a club signed for a season: the offer it took and the objective it chose (`SPN-2`, `OBJ-1`).
/// </summary>
/// <remarks>
/// <para>
/// A club has one deal a season. The fixed part is paid in equal parts on every matchday from the one the deal is signed on, with the matchdays
/// already played paid at signing; the bonus is paid when the season ends, at the figure worked out when the deal was signed.
/// </para>
/// <para>
/// A new manager may choose another objective for a few matchdays after a takeover, so the objective and its bonus can change until the season
/// is settled. Settling writes the outcome once.
/// </para>
/// </remarks>
public sealed class ClubSponsorDeal
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubSponsorDeal()
    {
    }

    /// <summary>Gets the deal identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the season the deal is for.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets the offer the club took.</summary>
    public Guid OfferId { get; private set; }

    /// <summary>Gets which offer it was.</summary>
    public SponsorOfferKind Kind { get; private set; }

    /// <summary>Gets the objective the club chose.</summary>
    public SeasonObjective Objective { get; private set; }

    /// <summary>Gets the place the objective asks for.</summary>
    public int TargetPlace { get; private set; }

    /// <summary>Gets what the sponsor pays on every matchday, in minor units.</summary>
    public long FixedMatchdayMinor { get; private set; }

    /// <summary>Gets the bonus for meeting the objective, in minor units.</summary>
    public long BonusMetMinor { get; private set; }

    /// <summary>Gets the bonus for exceeding the objective, in minor units.</summary>
    public long BonusExceededMinor { get; private set; }

    /// <summary>Gets who signed the deal.</summary>
    public SponsorSigner SignedBy { get; private set; }

    /// <summary>Gets when the deal was signed.</summary>
    public DateTimeOffset SignedAt { get; private set; }

    /// <summary>Gets how the season turned out against the objective, once it has been settled.</summary>
    public ObjectiveOutcome? Outcome { get; private set; }

    /// <summary>Gets the bonus the season paid, once it has been settled, in minor units.</summary>
    public long? BonusPaidMinor { get; private set; }

    /// <summary>Gets the place the club finished in, once the season has been settled.</summary>
    public int? FinalPlace { get; private set; }

    /// <summary>Gets when the season was settled.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>Gets when the deal was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets whether the season has been settled.</summary>
    public bool IsSettled => Outcome is not null;

    /// <summary>Signs a deal.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="offer">The offer taken.</param>
    /// <param name="objective">The objective chosen.</param>
    /// <param name="bonusMetMinor">The bonus for meeting the objective under this offer.</param>
    /// <param name="bonusExceededMinor">The bonus for exceeding it.</param>
    /// <param name="signedBy">Who signed.</param>
    /// <param name="now">The current instant.</param>
    public static ClubSponsorDeal Sign(
        Guid id,
        ClubSponsorOffer offer,
        SeasonObjective objective,
        long bonusMetMinor,
        long bonusExceededMinor,
        SponsorSigner signedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentOutOfRangeException.ThrowIfNegative(bonusMetMinor);
        ArgumentOutOfRangeException.ThrowIfLessThan(bonusExceededMinor, bonusMetMinor);

        return new ClubSponsorDeal
        {
            Id = id,
            ClubId = offer.ClubId,
            SeasonId = offer.SeasonId,
            OfferId = offer.Id,
            Kind = offer.Kind,
            Objective = objective,
            TargetPlace = EconomyRuleSet.TargetPlaceFor(objective),
            FixedMatchdayMinor = EconomyRuleSet.PerMatchday(offer.FixedSeasonMinor),
            BonusMetMinor = bonusMetMinor,
            BonusExceededMinor = bonusExceededMinor,
            SignedBy = signedBy,
            SignedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Chooses another objective under the same offer, before the season is settled.</summary>
    /// <param name="objective">The new objective.</param>
    /// <param name="bonusMetMinor">The bonus for meeting it.</param>
    /// <param name="bonusExceededMinor">The bonus for exceeding it.</param>
    /// <param name="signedBy">Who chose it.</param>
    /// <param name="now">The current instant.</param>
    public void ChangeObjective(SeasonObjective objective, long bonusMetMinor, long bonusExceededMinor, SponsorSigner signedBy, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bonusMetMinor);
        ArgumentOutOfRangeException.ThrowIfLessThan(bonusExceededMinor, bonusMetMinor);

        if (IsSettled)
        {
            throw new InvalidOperationException("A settled season's objective cannot change (OBJ-1).");
        }

        Objective = objective;
        TargetPlace = EconomyRuleSet.TargetPlaceFor(objective);
        BonusMetMinor = bonusMetMinor;
        BonusExceededMinor = bonusExceededMinor;
        SignedBy = signedBy;
        Touch(now);
    }

    /// <summary>Takes another offer and objective, before the season is settled: a new manager's second thoughts.</summary>
    /// <param name="offer">The offer now taken, one of the club's own for the season.</param>
    /// <param name="objective">The objective now chosen.</param>
    /// <param name="bonusMetMinor">The bonus for meeting it under the new offer.</param>
    /// <param name="bonusExceededMinor">The bonus for exceeding it.</param>
    /// <param name="signedBy">Who chose it.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// The matchdays already paid stay paid at the figure they were paid at; the new fixed part runs from the next matchday. The deal is signed
    /// again at that instant, which is what tells a deal the present manager made from one he inherited.
    /// </remarks>
    public void Replace(
        ClubSponsorOffer offer,
        SeasonObjective objective,
        long bonusMetMinor,
        long bonusExceededMinor,
        SponsorSigner signedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentOutOfRangeException.ThrowIfNegative(bonusMetMinor);
        ArgumentOutOfRangeException.ThrowIfLessThan(bonusExceededMinor, bonusMetMinor);

        if (IsSettled)
        {
            throw new InvalidOperationException("A settled season's deal cannot change (OBJ-1).");
        }

        if (offer.ClubId != ClubId || offer.SeasonId != SeasonId)
        {
            throw new InvalidOperationException("A deal can only take an offer made to the same club for the same season (SPN-1).");
        }

        OfferId = offer.Id;
        Kind = offer.Kind;
        FixedMatchdayMinor = EconomyRuleSet.PerMatchday(offer.FixedSeasonMinor);
        Objective = objective;
        TargetPlace = EconomyRuleSet.TargetPlaceFor(objective);
        BonusMetMinor = bonusMetMinor;
        BonusExceededMinor = bonusExceededMinor;
        SignedBy = signedBy;
        SignedAt = now;
        Touch(now);
    }

    /// <summary>Settles the season against the objective, once.</summary>
    /// <param name="finalPlace">The place the club finished in.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The bonus the season pays, 0 when the objective was missed.</returns>
    public long Settle(int finalPlace, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(finalPlace, 1);

        if (IsSettled)
        {
            throw new InvalidOperationException("A season's objective is settled once (OBJ-4).");
        }

        var outcome = EconomyRuleSet.OutcomeOf(Objective, finalPlace);
        var bonus = outcome switch
        {
            ObjectiveOutcome.Exceeded => BonusExceededMinor,
            ObjectiveOutcome.Met => BonusMetMinor,
            _ => 0,
        };

        Outcome = outcome;
        BonusPaidMinor = bonus;
        FinalPlace = finalPlace;
        SettledAt = now;
        Touch(now);

        return bonus;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
