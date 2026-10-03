namespace TouchlineManager.Domain.Competition;

/// <summary>One player's contribution to one match, as the season statistics accumulate it.</summary>
/// <remarks>
/// The projection's input, produced by the pure calculator from a stored result — the player's line, the
/// shots and saves its events name, and the minutes it played — so the season's totals are a sum of match
/// facts rather than a second simulation. A player who did not appear produces no line at all.
/// </remarks>
public sealed record PlayerMatchStatLine
{
    /// <summary>Gets the player.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>Gets the club the player played for.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets how many appearances the match added: one when the player took the pitch, else zero.</summary>
    public required int Appearances { get; init; }

    /// <summary>Gets how many starts the match added: one when the player was in the eleven.</summary>
    public required int Starts { get; init; }

    /// <summary>Gets the minutes played.</summary>
    public required int MinutesPlayed { get; init; }

    /// <summary>Gets the goals scored.</summary>
    public required int Goals { get; init; }

    /// <summary>Gets the goals set up.</summary>
    public required int Assists { get; init; }

    /// <summary>Gets the shots taken.</summary>
    public required int Shots { get; init; }

    /// <summary>Gets the shots on target (goals plus the ones a goalkeeper saved).</summary>
    public required int ShotsOnTarget { get; init; }

    /// <summary>Gets the saves made.</summary>
    public required int Saves { get; init; }

    /// <summary>Gets the passes attempted.</summary>
    public required int PassesAttempted { get; init; }

    /// <summary>Gets the passes that found a teammate (a subset of those attempted).</summary>
    public required int PassesCompleted { get; init; }

    /// <summary>Gets the take-ons attempted.</summary>
    public required int DribblesAttempted { get; init; }

    /// <summary>Gets the take-ons won (a subset of those attempted).</summary>
    public required int DribblesCompleted { get; init; }

    /// <summary>Gets the bookings received, counting a second yellow as the booking it was.</summary>
    public required int YellowCards { get; init; }

    /// <summary>Gets the sendings-off, counting a second yellow as the red it became.</summary>
    public required int RedCards { get; init; }

    /// <summary>Gets the match rating in basis points, or zero when the player was not rated.</summary>
    public required int RatingBasisPoints { get; init; }
}

/// <summary>
/// A player's whole season for one club, as a rebuild computes it (`STA-1`, `TBL-13`).
/// </summary>
/// <remarks>
/// The aggregate twin of <see cref="PlayerMatchStatLine"/>: a match line is one fixture's contribution and
/// always carries exactly one appearance, while this is the sum over a division-season's published results.
/// It exists so a projection can be rewritten from what it should hold rather than only advanced by what
/// just happened, which is what makes the statistics exactly rebuildable (`TBL-13`) rather than merely
/// plausible.
/// </remarks>
public sealed record PlayerSeasonStatLine
{
    /// <summary>Gets the player.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>Gets the club the player appeared for.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets how many matches the player appeared in.</summary>
    public required int Appearances { get; init; }

    /// <summary>Gets how many of those the player started.</summary>
    public required int Starts { get; init; }

    /// <summary>Gets the minutes played.</summary>
    public required int MinutesPlayed { get; init; }

    /// <summary>Gets the goals scored.</summary>
    public required int Goals { get; init; }

    /// <summary>Gets the goals set up.</summary>
    public required int Assists { get; init; }

    /// <summary>Gets the shots taken.</summary>
    public required int Shots { get; init; }

    /// <summary>Gets the shots on target.</summary>
    public required int ShotsOnTarget { get; init; }

    /// <summary>Gets the saves made.</summary>
    public required int Saves { get; init; }

    /// <summary>Gets the passes attempted.</summary>
    public required int PassesAttempted { get; init; }

    /// <summary>Gets the passes that found a teammate (a subset of those attempted).</summary>
    public required int PassesCompleted { get; init; }

    /// <summary>Gets the take-ons attempted.</summary>
    public required int DribblesAttempted { get; init; }

    /// <summary>Gets the take-ons won (a subset of those attempted).</summary>
    public required int DribblesCompleted { get; init; }

    /// <summary>Gets the league bookings accumulated.</summary>
    public required int YellowCards { get; init; }

    /// <summary>Gets the sendings-off accumulated.</summary>
    public required int RedCards { get; init; }

    /// <summary>Gets the sum of the player's match ratings, in basis points.</summary>
    public required long RatingBasisPointsTotal { get; init; }

    /// <summary>Gets how many appearances carried a rating, which the average divides by.</summary>
    public required int RatedAppearances { get; init; }
}

/// <summary>
/// A player's season statistics for one club in one division-season (master plan §6.4).
/// </summary>
/// <remarks>
/// <para>
/// A projection rather than a record: every column is a sum of published match facts, advanced by the
/// matchday publication in the same transaction that publishes the results. It is rebuilt by the same
/// arithmetic the live path uses, which is what `TBL-13` asks of the table and this mirrors for a player.
/// </para>
/// <para>
/// The key is <c>(division-season, player, club)</c> because a player may move mid-season: the goals a
/// player scored for their old club are not the goals they scored for the new one, and a single row keyed on
/// the player would have to decide which club they belonged to.
/// </para>
/// <para>
/// The average rating is computed rather than stored, for the same reason a standing's goal difference is:
/// two columns that must agree are two columns that can disagree. It is null until the player has been
/// rated at least once, because a division's opening table is eighteen clubs who have not played.
/// </para>
/// </remarks>
public sealed class PlayerSeasonStat
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerSeasonStat()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the division-season the statistics belong to.</summary>
    public Guid DivisionSeasonId { get; private set; }

    /// <summary>Gets the player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club the player appeared for.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets how many matches the player appeared in.</summary>
    public int Appearances { get; private set; }

    /// <summary>Gets how many of those the player started.</summary>
    public int Starts { get; private set; }

    /// <summary>Gets the minutes played.</summary>
    public int MinutesPlayed { get; private set; }

    /// <summary>Gets the goals scored.</summary>
    public int Goals { get; private set; }

    /// <summary>Gets the goals set up.</summary>
    public int Assists { get; private set; }

    /// <summary>Gets the shots taken.</summary>
    public int Shots { get; private set; }

    /// <summary>Gets the shots on target.</summary>
    public int ShotsOnTarget { get; private set; }

    /// <summary>Gets the saves made.</summary>
    public int Saves { get; private set; }

    /// <summary>Gets the passes attempted.</summary>
    public int PassesAttempted { get; private set; }

    /// <summary>Gets the passes that found a teammate.</summary>
    public int PassesCompleted { get; private set; }

    /// <summary>Gets the take-ons attempted.</summary>
    public int DribblesAttempted { get; private set; }

    /// <summary>Gets the take-ons won.</summary>
    public int DribblesCompleted { get; private set; }

    /// <summary>Gets the league bookings accumulated.</summary>
    public int YellowCards { get; private set; }

    /// <summary>Gets the sendings-off accumulated.</summary>
    public int RedCards { get; private set; }

    /// <summary>Gets the sum of the player's match ratings, in basis points.</summary>
    public long RatingBasisPointsTotal { get; private set; }

    /// <summary>Gets how many appearances carried a rating, which the average divides by.</summary>
    public int RatedAppearances { get; private set; }

    /// <summary>Gets the player's average match rating in basis points, or null when they have none.</summary>
    public int? AverageRatingBasisPoints =>
        RatedAppearances == 0 ? null : (int)(RatingBasisPointsTotal / RatedAppearances);

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last advanced.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Opens a player's season statistics, with nothing accumulated yet.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="divisionSeasonId">The division-season the statistics belong to.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="clubId">The club the player appeared for.</param>
    /// <param name="now">The current instant.</param>
    public static PlayerSeasonStat Open(
        Guid id,
        Guid divisionSeasonId,
        Guid playerId,
        Guid clubId,
        DateTimeOffset now)
    {
        if (divisionSeasonId == Guid.Empty)
        {
            throw new ArgumentException("Season statistics belong to a division-season.", nameof(divisionSeasonId));
        }

        if (playerId == Guid.Empty)
        {
            throw new ArgumentException("Season statistics belong to a player.", nameof(playerId));
        }

        if (clubId == Guid.Empty)
        {
            throw new ArgumentException("Season statistics belong to a club.", nameof(clubId));
        }

        return new PlayerSeasonStat
        {
            Id = id,
            DivisionSeasonId = divisionSeasonId,
            PlayerId = playerId,
            ClubId = clubId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Creates a player's season statistics already holding a whole season's totals (`TBL-13`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="divisionSeasonId">The division-season the statistics belong to.</param>
    /// <param name="line">The computed season line.</param>
    /// <param name="now">The current instant.</param>
    public static PlayerSeasonStat Create(
        Guid id,
        Guid divisionSeasonId,
        PlayerSeasonStatLine line,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (divisionSeasonId == Guid.Empty)
        {
            throw new ArgumentException("Season statistics belong to a division-season.", nameof(divisionSeasonId));
        }

        if (line.PlayerId == Guid.Empty)
        {
            throw new ArgumentException("Season statistics belong to a player.", nameof(line));
        }

        if (line.ClubId == Guid.Empty)
        {
            throw new ArgumentException("Season statistics belong to a club.", nameof(line));
        }

        var stat = new PlayerSeasonStat
        {
            Id = id,
            DivisionSeasonId = divisionSeasonId,
            PlayerId = line.PlayerId,
            ClubId = line.ClubId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        stat.Assign(line, now);

        return stat;
    }

    /// <summary>
    /// Rewrites the totals from a computed season line (`TBL-13`).
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="Accumulate"/> for a rebuild: it replaces rather than adds, so a
    /// projection that has drifted is set back to the value the published results compute. It takes a whole
    /// season's line, so there is no way to correct one column and leave the rest behind.
    /// </remarks>
    /// <param name="line">The computed season line.</param>
    /// <param name="now">The current instant.</param>
    /// <exception cref="ArgumentException">When the line is not this player's, or its counts cannot be true.</exception>
    public void Rebuild(PlayerSeasonStatLine line, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.PlayerId != PlayerId || line.ClubId != ClubId)
        {
            throw new ArgumentException(
                "A season statistic is rebuilt only from its own player's line for its own club.",
                nameof(line));
        }

        Assign(line, now);
    }

    /// <summary>Advances the season totals by one match.</summary>
    /// <param name="line">The player's contribution, from the pure calculator.</param>
    /// <param name="now">The current instant.</param>
    /// <exception cref="ArgumentException">When the line is not this player's, or its counts cannot be true.</exception>
    public void Accumulate(PlayerMatchStatLine line, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.PlayerId != PlayerId || line.ClubId != ClubId)
        {
            throw new ArgumentException(
                "A season statistic is advanced only by its own player's line for its own club.",
                nameof(line));
        }

        EnsureNonNegative(line);
        EnsureCountsAgree(line);

        Appearances += line.Appearances;
        Starts += line.Starts;
        MinutesPlayed += line.MinutesPlayed;
        Goals += line.Goals;
        Assists += line.Assists;
        Shots += line.Shots;
        ShotsOnTarget += line.ShotsOnTarget;
        Saves += line.Saves;
        PassesAttempted += line.PassesAttempted;
        PassesCompleted += line.PassesCompleted;
        DribblesAttempted += line.DribblesAttempted;
        DribblesCompleted += line.DribblesCompleted;
        YellowCards += line.YellowCards;
        RedCards += line.RedCards;

        if (line.RatingBasisPoints > 0)
        {
            RatingBasisPointsTotal += line.RatingBasisPoints;
            RatedAppearances++;
        }

        UpdatedAt = now;
        Version++;
    }

    private void Assign(PlayerSeasonStatLine line, DateTimeOffset now)
    {
        EnsureAggregateNonNegative(line);
        EnsureAggregateCountsAgree(line);

        Appearances = line.Appearances;
        Starts = line.Starts;
        MinutesPlayed = line.MinutesPlayed;
        Goals = line.Goals;
        Assists = line.Assists;
        Shots = line.Shots;
        ShotsOnTarget = line.ShotsOnTarget;
        Saves = line.Saves;
        PassesAttempted = line.PassesAttempted;
        PassesCompleted = line.PassesCompleted;
        DribblesAttempted = line.DribblesAttempted;
        DribblesCompleted = line.DribblesCompleted;
        YellowCards = line.YellowCards;
        RedCards = line.RedCards;
        RatingBasisPointsTotal = line.RatingBasisPointsTotal;
        RatedAppearances = line.RatedAppearances;

        UpdatedAt = now;
        Version++;
    }

    private static void EnsureAggregateNonNegative(PlayerSeasonStatLine line)
    {
        var counts = new (string Name, long Value)[]
        {
            (nameof(line.Appearances), line.Appearances),
            (nameof(line.Starts), line.Starts),
            (nameof(line.MinutesPlayed), line.MinutesPlayed),
            (nameof(line.Goals), line.Goals),
            (nameof(line.Assists), line.Assists),
            (nameof(line.Shots), line.Shots),
            (nameof(line.ShotsOnTarget), line.ShotsOnTarget),
            (nameof(line.Saves), line.Saves),
            (nameof(line.PassesAttempted), line.PassesAttempted),
            (nameof(line.PassesCompleted), line.PassesCompleted),
            (nameof(line.DribblesAttempted), line.DribblesAttempted),
            (nameof(line.DribblesCompleted), line.DribblesCompleted),
            (nameof(line.YellowCards), line.YellowCards),
            (nameof(line.RedCards), line.RedCards),
            (nameof(line.RatingBasisPointsTotal), line.RatingBasisPointsTotal),
            (nameof(line.RatedAppearances), line.RatedAppearances),
        };

        foreach (var (name, value) in counts)
        {
            if (value < 0)
            {
                throw new ArgumentException($"A season statistic is never negative: {name} was {value}.", nameof(line));
            }
        }
    }

    private static void EnsureAggregateCountsAgree(PlayerSeasonStatLine line)
    {
        // A season line exists only for a player who appeared, so it always covers at least one match.
        if (line.Appearances < 1)
        {
            throw new ArgumentException(
                $"A season line covers at least one appearance: appearances was {line.Appearances}.",
                nameof(line));
        }

        if (line.Starts > line.Appearances)
        {
            throw new ArgumentException("A player cannot start a match they did not appear in.", nameof(line));
        }

        if (line.ShotsOnTarget > line.Shots)
        {
            throw new ArgumentException("Shots on target are a subset of shots.", nameof(line));
        }

        if (line.PassesCompleted > line.PassesAttempted)
        {
            throw new ArgumentException("Completed passes are a subset of attempted passes.", nameof(line));
        }

        if (line.DribblesCompleted > line.DribblesAttempted)
        {
            throw new ArgumentException("Successful dribbles are a subset of attempted dribbles.", nameof(line));
        }

        if (line.RatedAppearances > line.Appearances)
        {
            throw new ArgumentException("A rating belongs to an appearance.", nameof(line));
        }

        if (line.RatingBasisPointsTotal > (long)line.RatedAppearances * 10_000)
        {
            throw new ArgumentException(
                "A season's rating total is at most 10,000 basis points per rated appearance.",
                nameof(line));
        }
    }

    private static void EnsureNonNegative(PlayerMatchStatLine line)
    {
        var counts = new (string Name, int Value)[]
        {
            (nameof(line.Appearances), line.Appearances),
            (nameof(line.Starts), line.Starts),
            (nameof(line.MinutesPlayed), line.MinutesPlayed),
            (nameof(line.Goals), line.Goals),
            (nameof(line.Assists), line.Assists),
            (nameof(line.Shots), line.Shots),
            (nameof(line.ShotsOnTarget), line.ShotsOnTarget),
            (nameof(line.Saves), line.Saves),
            (nameof(line.PassesAttempted), line.PassesAttempted),
            (nameof(line.PassesCompleted), line.PassesCompleted),
            (nameof(line.DribblesAttempted), line.DribblesAttempted),
            (nameof(line.DribblesCompleted), line.DribblesCompleted),
            (nameof(line.YellowCards), line.YellowCards),
            (nameof(line.RedCards), line.RedCards),
            (nameof(line.RatingBasisPoints), line.RatingBasisPoints),
        };

        foreach (var (name, value) in counts)
        {
            if (value < 0)
            {
                throw new ArgumentException($"A season statistic is never negative: {name} was {value}.", nameof(line));
            }
        }
    }

    private static void EnsureCountsAgree(PlayerMatchStatLine line)
    {
        // A line is only produced for a player who took the pitch, and a matchday is one round with one
        // fixture per club (CAL-9), so an appearance is a flag that is always set rather than a count.
        if (line.Appearances != 1)
        {
            throw new ArgumentException(
                $"A line is only produced for a player who appeared: appearances was {line.Appearances}.",
                nameof(line));
        }

        if (line.Starts > line.Appearances)
        {
            throw new ArgumentException("A player cannot start a match they did not appear in.", nameof(line));
        }

        if (line.ShotsOnTarget > line.Shots)
        {
            throw new ArgumentException("Shots on target are a subset of shots.", nameof(line));
        }

        if (line.PassesCompleted > line.PassesAttempted)
        {
            throw new ArgumentException("Completed passes are a subset of attempted passes.", nameof(line));
        }

        if (line.DribblesCompleted > line.DribblesAttempted)
        {
            throw new ArgumentException("Successful dribbles are a subset of attempted dribbles.", nameof(line));
        }

        if (line.RatingBasisPoints > 10_000)
        {
            throw new ArgumentException(
                $"A match rating is at most 10,000 basis points, was {line.RatingBasisPoints}.",
                nameof(line));
        }
    }
}
