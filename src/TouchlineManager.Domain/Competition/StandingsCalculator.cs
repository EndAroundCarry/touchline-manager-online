using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Competition;

/// <summary>One club's cards in one fixture, which the table's last tie-breakers count (`TBL-8`, `TBL-9`).</summary>
/// <param name="YellowCards">Yellow cards shown to the club.</param>
/// <param name="RedCards">Red cards shown to the club, counting a second yellow.</param>
public sealed record DisciplineCounts(int YellowCards, int RedCards);

/// <summary>
/// One published result, as the table reads it.
/// </summary>
/// <remarks>
/// Deliberately the outcome and not the fixture: ranking is a pure function of what was played, so a
/// table can be rebuilt from the results alone (`TBL-13`) and the rules can be tested without a database
/// or a season.
/// </remarks>
/// <param name="HomeClubId">The host.</param>
/// <param name="AwayClubId">The visitor.</param>
/// <param name="HomeGoals">Goals scored by the host.</param>
/// <param name="AwayGoals">Goals scored by the visitor.</param>
/// <param name="Home">The host's cards.</param>
/// <param name="Away">The visitor's cards.</param>
public sealed record MatchOutcome(
    Guid HomeClubId,
    Guid AwayClubId,
    int HomeGoals,
    int AwayGoals,
    DisciplineCounts Home,
    DisciplineCounts Away);

/// <summary>One club's computed line in a table, before it is stored.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="Played">Fixtures played.</param>
/// <param name="Won">Fixtures won.</param>
/// <param name="Drawn">Fixtures drawn.</param>
/// <param name="Lost">Fixtures lost.</param>
/// <param name="GoalsFor">Goals scored.</param>
/// <param name="GoalsAgainst">Goals conceded.</param>
/// <param name="Points">Points: three a win, one a draw.</param>
/// <param name="YellowCards">Yellow cards accumulated.</param>
/// <param name="RedCards">Red cards accumulated.</param>
/// <param name="Rank">The 1-based position the line earned.</param>
public sealed record StandingLine(
    Guid ClubId,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int Points,
    int YellowCards,
    int RedCards,
    int Rank)
{
    /// <summary>Gets goal difference, the table's second tie-breaker (`TBL-3`).</summary>
    public int GoalDifference => GoalsFor - GoalsAgainst;
}

/// <summary>
/// The league table's ordering rules, in their exact sequence (`TBL-1`…`TBL-13`).
/// </summary>
/// <remarks>
/// <para>
/// One pure function, so the order a division is listed in is a property of the results and the stored
/// draw rather than of how the rows happened to be read. Every criterion before the draw is a fact about
/// what was played; the draw is the last one precisely because it must never decide anything that a
/// result could have (`TBL-11`, `TBL-12`).
/// </para>
/// <para>
/// The six criteria that need other clubs — head-to-head points and goal difference among tied clubs —
/// are applied to the <em>group</em> a club is tied into by the first four, which is what "among the tied
/// clubs" means: a club that is tied on points, goal difference, goals scored, and wins with two others is
/// separated by how those three did against each other, not by a two-way record against one of them.
/// </para>
/// <para>
/// The final comparison is the stored draw key (`TBL-10`), derived from the seed the division recorded
/// before the season started, so it is visible in the competition rules and reproducible from stored data.
/// The club identity is a last resort beyond it, present only so the ordering is total: two clubs cannot
/// share a draw key without a hash collision, and a table whose order depended on that would be
/// unreproducible in exactly the case the rules exist to make reproducible (`TBL-12`, `CONC-4`).
/// </para>
/// </remarks>
public static class StandingsCalculator
{
    /// <summary>Points awarded for a win (`TBL-1`).</summary>
    public const int PointsForWin = 3;

    /// <summary>Points awarded for a draw (`TBL-1`).</summary>
    public const int PointsForDraw = 1;

    /// <summary>Ranks every club in a division from its published results.</summary>
    /// <param name="clubIds">Every club in the division, including those that have not played yet.</param>
    /// <param name="outcomes">The division's published results.</param>
    /// <param name="drawKeyOf">The stored draw key of a club (`TBL-10`, `TBL-11`).</param>
    /// <returns>The table, best first, each line carrying its rank.</returns>
    public static IReadOnlyList<StandingLine> Rank(
        IReadOnlyCollection<Guid> clubIds,
        IEnumerable<MatchOutcome> outcomes,
        Func<Guid, string> drawKeyOf)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(drawKeyOf);

        var tallies = new Dictionary<Guid, Tally>();

        foreach (var clubId in clubIds)
        {
            tallies[clubId] = new Tally(clubId);
        }

        var played = outcomes.ToList();

        foreach (var outcome in played)
        {
            if (!tallies.TryGetValue(outcome.HomeClubId, out var home)
                || !tallies.TryGetValue(outcome.AwayClubId, out var away))
            {
                // A result between clubs that are not in this division is not this table's business.
                // Silently ignoring it would hide a genuine data defect, so the caller is refused.
                throw new ArgumentException(
                    "A result names a club that is not in this division's table.",
                    nameof(outcomes));
            }

            home.RecordOutcome(outcome.HomeGoals, outcome.AwayGoals, outcome.Home);
            away.RecordOutcome(outcome.AwayGoals, outcome.HomeGoals, outcome.Away);
        }

        var ordered = new List<Tally>(tallies.Count);

        // TBL-2 to TBL-5: points, goal difference, goals scored, wins. Clubs equal on all four are tied
        // and are separated by what they did against each other.
        var tied = tallies.Values
            .GroupBy(tally => (tally.Points, tally.GoalDifference, tally.GoalsFor, tally.Won))
            .OrderByDescending(group => group.Key)
            .Select(group => group.ToList());

        foreach (var group in tied)
        {
            if (group.Count == 1)
            {
                ordered.Add(group[0]);

                continue;
            }

            var members = group.Select(tally => tally.ClubId).ToHashSet();
            var headToHead = new Dictionary<Guid, Tally>();

            foreach (var tally in group)
            {
                headToHead[tally.ClubId] = new Tally(tally.ClubId);
            }

            foreach (var outcome in played)
            {
                if (!members.Contains(outcome.HomeClubId) || !members.Contains(outcome.AwayClubId))
                {
                    continue;
                }

                headToHead[outcome.HomeClubId].RecordOutcome(outcome.HomeGoals, outcome.AwayGoals, outcome.Home);
                headToHead[outcome.AwayClubId].RecordOutcome(outcome.AwayGoals, outcome.HomeGoals, outcome.Away);
            }

            ordered.AddRange(group
                .OrderByDescending(tally => headToHead[tally.ClubId].Points)
                .ThenByDescending(tally => headToHead[tally.ClubId].GoalDifference)
                .ThenBy(tally => tally.RedCards)
                .ThenBy(tally => tally.YellowCards)
                .ThenBy(tally => drawKeyOf(tally.ClubId), StringComparer.Ordinal)
                .ThenBy(tally => tally.ClubId));
        }

        var table = new List<StandingLine>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            table.Add(ordered[index].ToLine(index + 1));
        }

        return table;
    }

    /// <summary>
    /// Derives a club's draw key from the seed the division stored before the season (`TBL-10`, `TBL-11`).
    /// </summary>
    /// <remarks>
    /// A digest rather than a stream of draws: the key has to be reproducible from the seed and the club
    /// alone, in any order, at any time — including years later when a finished season's ordering is being
    /// explained — and it has to be the same in every process. Drawing keys by iterating clubs would make
    /// the table depend on the order they were iterated in, which is the thing `TBL-12` forbids.
    /// </remarks>
    /// <param name="tieDrawSeed">The division-season's stored tie-break seed.</param>
    /// <param name="clubId">The club.</param>
    public static string DrawKeyOf(string tieDrawSeed, Guid clubId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tieDrawSeed);

        return DeterministicDigest.Of(tieDrawSeed, "tie-draw", clubId.ToString("D"));
    }

    /// <summary>One club's running totals while a table is being computed.</summary>
    private sealed class Tally
    {
        public Tally(Guid clubId) => ClubId = clubId;

        public Guid ClubId { get; }

        public int Played { get; private set; }

        public int Won { get; private set; }

        public int Drawn { get; private set; }

        public int Lost { get; private set; }

        public int GoalsFor { get; private set; }

        public int GoalsAgainst { get; private set; }

        public int Points => (Won * PointsForWin) + (Drawn * PointsForDraw);

        public int GoalDifference => GoalsFor - GoalsAgainst;

        public int YellowCards { get; private set; }

        public int RedCards { get; private set; }

        public void RecordOutcome(int goalsFor, int goalsAgainst, DisciplineCounts cards)
        {
            ArgumentNullException.ThrowIfNull(cards);

            Played++;
            GoalsFor += goalsFor;
            GoalsAgainst += goalsAgainst;
            YellowCards += cards.YellowCards;
            RedCards += cards.RedCards;

            if (goalsFor > goalsAgainst)
            {
                Won++;
            }
            else if (goalsFor == goalsAgainst)
            {
                Drawn++;
            }
            else
            {
                Lost++;
            }
        }

        public StandingLine ToLine(int rank) => new(
            ClubId,
            Played,
            Won,
            Drawn,
            Lost,
            GoalsFor,
            GoalsAgainst,
            Points,
            YellowCards,
            RedCards,
            rank);
    }
}
