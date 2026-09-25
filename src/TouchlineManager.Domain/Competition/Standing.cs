namespace TouchlineManager.Domain.Competition;

/// <summary>
/// One club's line in a division's table (`TBL-1`…`TBL-13`, master plan §6.4).
/// </summary>
/// <remarks>
/// <para>
/// A projection rather than a record: the row is derived from the division's published results, and the
/// same derivation must be able to rebuild it exactly at any time (`TBL-13`). That is why nothing here
/// accumulates — <see cref="Rebuild"/> takes a whole line rather than adding a result, so a table can only
/// ever be as right as the fixtures it was computed from.
/// </para>
/// <para>
/// Goal difference is not stored: it is a function of two columns beside it, and storing it would let a
/// row disagree with itself.
/// </para>
/// </remarks>
public sealed class Standing
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Standing()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the division-season the table belongs to.</summary>
    public Guid DivisionSeasonId { get; private set; }

    /// <summary>Gets the club.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets how many fixtures the club has played.</summary>
    public int Played { get; private set; }

    /// <summary>Gets how many it won.</summary>
    public int Won { get; private set; }

    /// <summary>Gets how many it drew.</summary>
    public int Drawn { get; private set; }

    /// <summary>Gets how many it lost.</summary>
    public int Lost { get; private set; }

    /// <summary>Gets goals scored.</summary>
    public int GoalsFor { get; private set; }

    /// <summary>Gets goals conceded.</summary>
    public int GoalsAgainst { get; private set; }

    /// <summary>Gets points: three for a win, one for a draw (`TBL-1`).</summary>
    public int Points { get; private set; }

    /// <summary>Gets yellow cards accumulated, which break a tie once every result has (`TBL-9`).</summary>
    public int YellowCards { get; private set; }

    /// <summary>Gets red cards accumulated, which break a tie before yellows do (`TBL-8`).</summary>
    public int RedCards { get; private set; }

    /// <summary>Gets the current rank, 1-based and unique (`TBL-10`).</summary>
    public int Rank { get; private set; }

    /// <summary>Gets goal difference, which is a tie-breaker rather than a stored fact (`TBL-3`).</summary>
    public int GoalDifference => GoalsFor - GoalsAgainst;

    /// <summary>Gets when the row was last rebuilt.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the version, bumped on every rebuild.</summary>
    public long Version { get; private set; }

    /// <summary>Creates a club's line in a table.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="divisionSeasonId">The division-season the table belongs to.</param>
    /// <param name="line">The computed line.</param>
    /// <param name="now">The current instant.</param>
    public static Standing Create(Guid id, Guid divisionSeasonId, StandingLine line, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);

        var standing = new Standing
        {
            Id = id,
            DivisionSeasonId = divisionSeasonId,
            ClubId = line.ClubId,
        };

        standing.Apply(line, now);

        return standing;
    }

    /// <summary>
    /// Rewrites the row from a computed line (`TBL-13`).
    /// </summary>
    /// <remarks>
    /// The only way a stored table moves. A projection that could be edited one column at a time would be
    /// able to drift from the results it claims to summarise, and the whole point of rebuilding from
    /// published fixtures is that it cannot.
    /// </remarks>
    /// <param name="line">The computed line.</param>
    /// <param name="now">The current instant.</param>
    public void Rebuild(StandingLine line, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.ClubId != ClubId)
        {
            throw new ArgumentException("A standing is rebuilt from its own club's line.", nameof(line));
        }

        Apply(line, now);
    }

    private void Apply(StandingLine line, DateTimeOffset now)
    {
        if (line.Played != line.Won + line.Drawn + line.Lost)
        {
            throw new ArgumentException(
                "A line's played count is its wins, draws, and losses (TBL-1).",
                nameof(line));
        }

        if (line.Points != (line.Won * 3) + line.Drawn)
        {
            throw new ArgumentException(
                "A line's points are three a win and one a draw (TBL-1).",
                nameof(line));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(line.Rank, 1);

        Played = line.Played;
        Won = line.Won;
        Drawn = line.Drawn;
        Lost = line.Lost;
        GoalsFor = line.GoalsFor;
        GoalsAgainst = line.GoalsAgainst;
        Points = line.Points;
        YellowCards = line.YellowCards;
        RedCards = line.RedCards;
        Rank = line.Rank;
        UpdatedAt = now;
        Version++;
    }
}
