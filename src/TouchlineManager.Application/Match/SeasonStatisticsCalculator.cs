using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Match;

/// <summary>
/// Turns a matchday's published results into one season-statistics line per player who appeared.
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the stored result — the player lines it carries and the shot and save events its
/// events name — so a delayed publication re-derives the same statistic from the facts the result was made
/// from, and the season's totals cannot drift from the matches they summarise. It reads no clock, database,
/// or random source.
/// </para>
/// <para>
/// The lines and the events are two reads of one match: the line says who played and for how long, and what
/// they scored, set up, and were booked for; the events are where the shots and the saves are, because the
/// engine's line carries the attacking and disciplinary summary while its event stream is the durable
/// narrative (`MAT-5`). Summing a player's shots from the same events the score is derived from is what
/// keeps "the shots a player took" and "the shots the side took" one answer.
/// </para>
/// <para>
/// A player who did not take the pitch produces no line: an appearance is a match a player was part of, and
/// a substitute who stayed on the bench was not. The match rating is the engine's own (`engine-v2`), read
/// from the line rather than recomputed, so the season's average rating has one definition.
/// </para>
/// </remarks>
public static class SeasonStatisticsCalculator
{
    /// <summary>A stable version for the rule, so a change to what is counted is a named change.</summary>
    /// <remarks>Version 2 adds the passes and take-ons the engine's player line carries (`engine-v6`).</remarks>
    public const string Version = "season-stats-v2";

    /// <summary>Aggregates a matchday's results per player.</summary>
    /// <param name="matches">The published fixtures of the round, with their stored result documents.</param>
    /// <param name="events">The shot and save events of the round.</param>
    /// <returns>One line per player who appeared, in a stable order.</returns>
    public static IReadOnlyList<PlayerMatchStatLine> Calculate(
        IEnumerable<FixtureMatchLoadRow> matches,
        IEnumerable<MatchStatEvent> events)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(events);

        var byFixture = events
            .GroupBy(matchEvent => matchEvent.FixtureId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var lines = new List<PlayerMatchStatLine>();

        foreach (var match in matches)
        {
            var shots = new Dictionary<Guid, ShotCounts>();
            var saves = new Dictionary<Guid, int>();

            if (byFixture.TryGetValue(match.FixtureId, out var fixtureEvents))
            {
                CountShots(fixtureEvents, shots, saves);
            }

            foreach (var line in MatchStatisticsDocument.Read(match.StatisticsJson).PlayerLines)
            {
                if (line.MinutesPlayed <= 0)
                {
                    continue;
                }

                var shot = shots.TryGetValue(line.ParticipantId, out var counts) ? counts : default;

                lines.Add(new PlayerMatchStatLine
                {
                    PlayerId = line.ParticipantId,
                    ClubId = line.ClubId,
                    Appearances = 1,
                    Starts = line.Started ? 1 : 0,
                    MinutesPlayed = line.MinutesPlayed,
                    Goals = line.Goals,
                    Assists = line.Assists,
                    Shots = shot.Shots,
                    ShotsOnTarget = shot.OnTarget,
                    Saves = saves.TryGetValue(line.ParticipantId, out var made) ? made : 0,
                    PassesAttempted = line.PassesAttempted,
                    PassesCompleted = line.PassesCompleted,
                    DribblesAttempted = line.DribblesAttempted,
                    DribblesCompleted = line.DribblesCompleted,
                    YellowCards = line.YellowCards,
                    RedCards = line.SentOff ? 1 : 0,
                    RatingBasisPoints = line.RatingBasisPoints,
                });
            }
        }

        return
        [
            .. lines
                .OrderBy(line => line.ClubId)
                .ThenBy(line => line.PlayerId),
        ];
    }

    /// <summary>
    /// Folds a season's worth of match lines into one line per player per club (`STA-2`, `TBL-13`).
    /// </summary>
    /// <remarks>
    /// The same arithmetic the live publication performs match by match, applied in one pass over every
    /// published fixture instead. It is what makes the season statistics exactly rebuildable: a projection
    /// that has drifted is set back to whatever this returns, and because it is a sum of stored match facts
    /// rather than a second simulation, it cannot disagree with the results it summarises. The average
    /// rating is left as a total and a count, so one definition of it stays on the aggregate.
    /// </remarks>
    /// <param name="lines">The match lines of a division-season's published results.</param>
    /// <returns>One line per player and club, in a stable order.</returns>
    public static IReadOnlyList<PlayerSeasonStatLine> Aggregate(IEnumerable<PlayerMatchStatLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return
        [
            .. lines
                .GroupBy(line => (line.PlayerId, line.ClubId))
                .Select(group => new PlayerSeasonStatLine
                {
                    PlayerId = group.Key.PlayerId,
                    ClubId = group.Key.ClubId,
                    Appearances = group.Sum(line => line.Appearances),
                    Starts = group.Sum(line => line.Starts),
                    MinutesPlayed = group.Sum(line => line.MinutesPlayed),
                    Goals = group.Sum(line => line.Goals),
                    Assists = group.Sum(line => line.Assists),
                    Shots = group.Sum(line => line.Shots),
                    ShotsOnTarget = group.Sum(line => line.ShotsOnTarget),
                    Saves = group.Sum(line => line.Saves),
                    PassesAttempted = group.Sum(line => line.PassesAttempted),
                    PassesCompleted = group.Sum(line => line.PassesCompleted),
                    DribblesAttempted = group.Sum(line => line.DribblesAttempted),
                    DribblesCompleted = group.Sum(line => line.DribblesCompleted),
                    YellowCards = group.Sum(line => line.YellowCards),
                    RedCards = group.Sum(line => line.RedCards),
                    RatingBasisPointsTotal = group.Sum(line => (long)line.RatingBasisPoints),
                    RatedAppearances = group.Count(line => line.RatingBasisPoints > 0),
                })
                .OrderBy(line => line.ClubId)
                .ThenBy(line => line.PlayerId),
        ];
    }

    /// <summary>Counts each player's shots, shots on target, and saves from one fixture's events.</summary>
    private static void CountShots(
        IEnumerable<MatchStatEvent> events,
        Dictionary<Guid, ShotCounts> shots,
        Dictionary<Guid, int> saves)
    {
        foreach (var matchEvent in events)
        {
            if (matchEvent.Type == MatchEventType.ShotSaved && matchEvent.SecondaryParticipantId is Guid keeper)
            {
                saves.TryGetValue(keeper, out var made);
                saves[keeper] = made + 1;
            }

            if (matchEvent.ParticipantId is not Guid shooter || !IsShot(matchEvent.Type))
            {
                continue;
            }

            shots.TryGetValue(shooter, out var counts);

            shots[shooter] = new ShotCounts(
                counts.Shots + 1,
                counts.OnTarget + (IsOnTarget(matchEvent.Type) ? 1 : 0));
        }
    }

    private static bool IsShot(MatchEventType type) => type
        is MatchEventType.Goal
        or MatchEventType.PenaltyGoal
        or MatchEventType.PenaltyMissed
        or MatchEventType.ShotSaved
        or MatchEventType.ShotBlocked
        or MatchEventType.ShotOffTarget
        or MatchEventType.Woodwork;

    private static bool IsOnTarget(MatchEventType type) => type
        is MatchEventType.Goal
        or MatchEventType.PenaltyGoal
        or MatchEventType.ShotSaved;

    /// <summary>One player's shots in a match.</summary>
    private readonly record struct ShotCounts(int Shots, int OnTarget);
}
