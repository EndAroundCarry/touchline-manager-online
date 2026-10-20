using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Fans;

/// <summary>
/// The one place a club's fans are opened or changed: it stages the new numbers and the event that explains them together (`FAN-10`).
/// </summary>
/// <remarks>
/// Every caller (a published round, a weekly review, a rollover, a takeover) changes the fans through <see cref="Change"/>, so no change
/// can reach the row without a history entry beside it. Nothing here saves; the caller's transaction does.
/// </remarks>
public sealed class FanBaseKeeper
{
    private readonly IFanRepository _fans;

    /// <summary>Initializes the keeper.</summary>
    public FanBaseKeeper(IFanRepository fans) => _fans = fans;

    /// <summary>Opens a club's fans at its tier's opening numbers and records the opening.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="tier">The tier it plays in, starting at 1.</param>
    /// <param name="seasonId">The season it opens in.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The staged fan base.</returns>
    public ClubFanBase Open(Guid clubId, int tier, Guid seasonId, DateTimeOffset now)
    {
        var state = FanRuleSet.OpeningFor(tier);
        var fanBase = ClubFanBase.Open(clubId, state, now);

        _fans.Add(fanBase);
        _fans.AddEvent(ClubFanEvent.Record(Guid.CreateVersion7(), clubId, seasonId, now, FanEventKind.Opening, state, state));

        return fanBase;
    }

    /// <summary>Loads the fan bases of some clubs, tracked, opening any club that has none yet.</summary>
    /// <remarks>
    /// A club always has a fan base: the migration backfills the clubs that existed and the world generator opens every new one. Opening a
    /// missing one here is a safety net, so a defect elsewhere cannot make a round unpublishable.
    /// </remarks>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="tierOf">The tier each club plays in, for one that has to be opened.</param>
    /// <param name="seasonId">The season, for the opening event of a club that has to be opened.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Dictionary<Guid, ClubFanBase>> LoadAsync(
        IReadOnlyCollection<Guid> clubIds,
        Func<Guid, int> tierOf,
        Guid seasonId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        ArgumentNullException.ThrowIfNull(tierOf);

        var distinct = clubIds.Distinct().ToList();
        var bases = (await _fans.LoadAsync(distinct, cancellationToken)).ToDictionary(fanBase => fanBase.ClubId);

        foreach (var clubId in distinct.Where(clubId => !bases.ContainsKey(clubId)).Order())
        {
            bases[clubId] = Open(clubId, tierOf(clubId), seasonId, now);
        }

        return bases;
    }

    /// <summary>Changes a club's fans and records why.</summary>
    /// <param name="fanBase">The club's fan base, tracked.</param>
    /// <param name="after">The new numbers, from the fan rules.</param>
    /// <param name="kind">What moved the fans.</param>
    /// <param name="seasonId">The season the change belongs to.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="tenureId">The tenure, on a takeover.</param>
    /// <param name="fixtureId">The fixture, on a match.</param>
    /// <param name="weekOf">The Sunday reviewed, on a weekly review.</param>
    /// <param name="expectedPointsCentis">The points expected, on a match.</param>
    /// <param name="pointsWon">The points won, on a match.</param>
    /// <param name="gap">The gap to the target, on a review or a season reset.</param>
    public void Change(
        ClubFanBase fanBase,
        FanBaseState after,
        FanEventKind kind,
        Guid seasonId,
        DateTimeOffset now,
        Guid? tenureId = null,
        Guid? fixtureId = null,
        DateOnly? weekOf = null,
        int? expectedPointsCentis = null,
        int? pointsWon = null,
        int? gap = null)
    {
        ArgumentNullException.ThrowIfNull(fanBase);

        var before = fanBase.State;

        fanBase.Apply(after, now);

        _fans.AddEvent(ClubFanEvent.Record(
            Guid.CreateVersion7(),
            fanBase.ClubId,
            seasonId,
            now,
            kind,
            before,
            after,
            tenureId,
            fixtureId,
            weekOf,
            expectedPointsCentis,
            pointsWon,
            gap));
    }

    /// <summary>Turns the players a snapshot froze into the squad the fans size up.</summary>
    /// <param name="players">The snapshot's players.</param>
    public static IReadOnlyList<AiSquadPlayer> SquadOf(IEnumerable<SnapshotPlayerRow> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        return
        [
            .. players.Select(player => new AiSquadPlayer(
                player.PlayerId,
                player.PrimaryPosition,
                player.SecondaryPositions,
                player.Attributes,
                player.ConditionBp,
                player.IsAvailable)),
        ];
    }

    /// <summary>Ranks clubs by squad strength: the strongest is 1, a tie goes to the smaller club identity.</summary>
    /// <param name="strengths">Each club's squad strength.</param>
    /// <returns>Each club's rank, from 1.</returns>
    public static Dictionary<Guid, int> RankByStrength(IReadOnlyDictionary<Guid, int> strengths)
    {
        ArgumentNullException.ThrowIfNull(strengths);

        var ranks = new Dictionary<Guid, int>(strengths.Count);
        var place = 0;

        foreach (var club in strengths.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
        {
            ranks[club.Key] = ++place;
        }

        return ranks;
    }
}
