namespace Tactical11Manager.Domain.Competition;

/// <summary>
/// One match of a season's promotion play-off bracket, from the draw to the club that goes through (`PR-4`, `PR-6`).
/// </summary>
/// <remarks>
/// <para>
/// All of a bracket's ties are created when it is drawn, so "the play-offs are over" means "no tie without a winner".
/// The final's away side is empty until the semi-final is decided and <see cref="FillAway"/> names the winner.
/// </para>
/// <para>
/// Every transition is idempotent for the same input and refuses a different one, because the publication that drives it
/// is at-least-once (§7.4, ADR-0003): a retry must find the work done, and a contradicting retry is a defect to surface.
/// </para>
/// </remarks>
public sealed class PlayoffTie
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayoffTie()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the season the bracket belongs to.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets the country.</summary>
    public Guid CountryId { get; private set; }

    /// <summary>Gets the higher league of the pair.</summary>
    public Guid ParentDivisionId { get; private set; }

    /// <summary>Gets the lower league of the pair.</summary>
    public Guid? FeederDivisionId { get; private set; }

    /// <summary>Gets which match of the bracket this is.</summary>
    public PlayoffStage Stage { get; private set; }

    /// <summary>Gets the kickoff slot after matchday 30 the match was planned for (`CAL-6`).</summary>
    public int Slot { get; private set; }

    /// <summary>Gets the home club.</summary>
    public Guid HomeClubId { get; private set; }

    /// <summary>Gets the home club's league this season.</summary>
    public Guid HomeDivisionId { get; private set; }

    /// <summary>Gets the home club's final table place.</summary>
    public int HomePlace { get; private set; }

    /// <summary>Gets the away club, or <see langword="null"/> while it waits for a semi-final winner.</summary>
    public Guid? AwayClubId { get; private set; }

    /// <summary>Gets the away club's league this season.</summary>
    public Guid? AwayDivisionId { get; private set; }

    /// <summary>Gets the away club's final table place.</summary>
    public int? AwayPlace { get; private set; }

    /// <summary>Gets the match, once it is scheduled.</summary>
    public Guid? FixtureId { get; private set; }

    /// <summary>Gets the club that goes through, once decided.</summary>
    public Guid? WinnerClubId { get; private set; }

    /// <summary>Gets the club that is out, once decided.</summary>
    public Guid? LoserClubId { get; private set; }

    /// <summary>Gets how the tie was settled, once decided.</summary>
    public PlayoffDecision? DecidedBy { get; private set; }

    /// <summary>Gets the home side's shoot-out goals, when the tie went to penalties.</summary>
    public int? ShootoutHomeScore { get; private set; }

    /// <summary>Gets the away side's shoot-out goals, when the tie went to penalties.</summary>
    public int? ShootoutAwayScore { get; private set; }

    /// <summary>Gets the shoot-out's kicks as stored by the engine's shoot-out, when the tie went to penalties.</summary>
    public string? ShootoutKicks { get; private set; }

    /// <summary>Gets when the tie was drawn.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets a value indicating whether the tie has a winner.</summary>
    public bool IsDecided => WinnerClubId is not null;

    /// <summary>Gets a value indicating whether both clubs are known.</summary>
    public bool HasBothSides => AwayClubId is not null;

    /// <summary>Stores one drawn pairing as a tie.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="seasonId">The season the bracket belongs to.</param>
    /// <param name="countryId">The country.</param>
    /// <param name="pairing">The pairing the draw produced.</param>
    /// <param name="now">The current instant.</param>
    public static PlayoffTie Draw(Guid id, Guid seasonId, Guid countryId, PlayoffPairing pairing, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pairing);

        if (pairing.AwayClubId == pairing.HomeClubId)
        {
            throw new ArgumentException("A club cannot play itself.", nameof(pairing));
        }

        if (pairing.Stage == PlayoffStage.SemiFinal
            && (pairing.AwayClubId is null || pairing.AwayDivisionId is null || pairing.AwayPlace is null))
        {
            throw new ArgumentException("A semi-final is drawn with both clubs.", nameof(pairing));
        }

        if ((pairing.AwayClubId is null) != (pairing.AwayDivisionId is null)
            || (pairing.AwayClubId is null) != (pairing.AwayPlace is null))
        {
            throw new ArgumentException("The away club, its league and its place are all set or all empty.", nameof(pairing));
        }

        return new PlayoffTie
        {
            Id = id,
            SeasonId = seasonId,
            CountryId = countryId,
            ParentDivisionId = pairing.ParentDivisionId,
            FeederDivisionId = pairing.FeederDivisionId,
            Stage = pairing.Stage,
            Slot = pairing.Slot,
            HomeClubId = pairing.HomeClubId,
            HomeDivisionId = pairing.HomeDivisionId,
            HomePlace = pairing.HomePlace,
            AwayClubId = pairing.AwayClubId,
            AwayDivisionId = pairing.AwayDivisionId,
            AwayPlace = pairing.AwayPlace,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Names the away club of a tie that was drawn without one: the semi-final winner.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="divisionId">The club's league this season.</param>
    /// <param name="place">The club's final table place.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>Idempotent for the same club; a different club once one is named is refused.</remarks>
    public void FillAway(Guid clubId, Guid divisionId, int place, DateTimeOffset now)
    {
        if (AwayClubId is not null)
        {
            if (AwayClubId != clubId)
            {
                throw new InvalidOperationException("The away side of a play-off tie is named once.");
            }

            return;
        }

        if (clubId == HomeClubId)
        {
            throw new ArgumentException("A club cannot play itself.", nameof(clubId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(place, 1);

        AwayClubId = clubId;
        AwayDivisionId = divisionId;
        AwayPlace = place;

        Touch(now);
    }

    /// <summary>Attaches the scheduled match.</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>Idempotent for the same fixture; a tie plays one match.</remarks>
    public void AttachFixture(Guid fixtureId, DateTimeOffset now)
    {
        if (fixtureId == Guid.Empty)
        {
            throw new ArgumentException("A fixture identity is required.", nameof(fixtureId));
        }

        if (FixtureId is not null)
        {
            if (FixtureId != fixtureId)
            {
                throw new InvalidOperationException("A play-off tie is played in one match.");
            }

            return;
        }

        if (!HasBothSides)
        {
            throw new InvalidOperationException("A play-off match needs both clubs before it is scheduled.");
        }

        FixtureId = fixtureId;

        Touch(now);
    }

    /// <summary>Records who goes through.</summary>
    /// <param name="winnerClubId">The home or away club.</param>
    /// <param name="decidedBy">How the tie was settled.</param>
    /// <param name="shootoutHomeScore">The home side's shoot-out goals; required for penalties and refused otherwise.</param>
    /// <param name="shootoutAwayScore">The away side's shoot-out goals; required for penalties and refused otherwise.</param>
    /// <param name="shootoutKicks">The shoot-out's kicks, stored as the shoot-out produced them.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>Idempotent for the same winner; a different winner once decided is refused.</remarks>
    public void Decide(
        Guid winnerClubId,
        PlayoffDecision decidedBy,
        int? shootoutHomeScore,
        int? shootoutAwayScore,
        string? shootoutKicks,
        DateTimeOffset now)
    {
        if (IsDecided)
        {
            if (WinnerClubId != winnerClubId)
            {
                throw new InvalidOperationException("A decided play-off tie keeps its winner.");
            }

            return;
        }

        if (FixtureId is null || AwayClubId is null)
        {
            throw new InvalidOperationException("A play-off tie is decided by its match.");
        }

        if (winnerClubId != HomeClubId && winnerClubId != AwayClubId)
        {
            throw new ArgumentException("The winner is the home or the away club.", nameof(winnerClubId));
        }

        if (decidedBy == PlayoffDecision.Penalties)
        {
            if (shootoutHomeScore is null || shootoutAwayScore is null)
            {
                throw new ArgumentException("A shoot-out has a score for both sides.", nameof(shootoutHomeScore));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(shootoutHomeScore.Value);
            ArgumentOutOfRangeException.ThrowIfNegative(shootoutAwayScore.Value);

            var homeWon = shootoutHomeScore > shootoutAwayScore;

            if (shootoutHomeScore == shootoutAwayScore || homeWon != (winnerClubId == HomeClubId))
            {
                throw new ArgumentException("The shoot-out score must favour the winner.", nameof(winnerClubId));
            }
        }
        else if (shootoutHomeScore is not null || shootoutAwayScore is not null || shootoutKicks is not null)
        {
            throw new ArgumentException("A tie decided on the score has no shoot-out.", nameof(decidedBy));
        }

        WinnerClubId = winnerClubId;
        LoserClubId = winnerClubId == HomeClubId ? AwayClubId : HomeClubId;
        DecidedBy = decidedBy;
        ShootoutHomeScore = shootoutHomeScore;
        ShootoutAwayScore = shootoutAwayScore;
        ShootoutKicks = shootoutKicks;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
