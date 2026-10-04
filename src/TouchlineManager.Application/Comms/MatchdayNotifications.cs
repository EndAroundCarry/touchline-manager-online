using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Comms;

/// <summary>One fixture a round played, as the result notification reads it.</summary>
/// <param name="FixtureId">The fixture.</param>
/// <param name="MatchId">The played match the message links to.</param>
/// <param name="HomeClubId">The host.</param>
/// <param name="AwayClubId">The visitor.</param>
/// <param name="HomeGoals">The host's goals.</param>
/// <param name="AwayGoals">The visitor's goals.</param>
public sealed record PlayedFixtureFact(
    Guid FixtureId,
    Guid MatchId,
    Guid HomeClubId,
    Guid AwayClubId,
    int HomeGoals,
    int AwayGoals);

/// <summary>One club's league position after a round, with where it was before (`F-41`).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="Position">Its position after the round.</param>
/// <param name="PreviousPosition">Its position before the round.</param>
public sealed record ClubPositionFact(Guid ClubId, int Position, int PreviousPosition);

/// <summary>What one round did to one player, as the discipline and injury notifications read it.</summary>
/// <param name="ClubId">The club the player played for.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="SuspensionFixtures">How many fixtures a suspension covers, or zero.</param>
/// <param name="FromBookings">Whether an accumulation of bookings earned the suspension.</param>
/// <param name="FromRedCard">Whether a sending-off earned the suspension.</param>
/// <param name="InjuryFixtures">How many fixtures an injury rules the player out for, or zero.</param>
public sealed record PlayerEffectFact(
    Guid ClubId,
    Guid PlayerId,
    int SuspensionFixtures,
    bool FromBookings,
    bool FromRedCard,
    int InjuryFixtures);

/// <summary>One repair a club's frozen side needed, as the message names it (`DIS-7`).</summary>
/// <param name="SlotNumber">The slot the decision was made in.</param>
/// <param name="ReasonCode">Why, as a <c>SnapshotRepairReasons</c> code.</param>
/// <param name="ReplacementPlayerId">The player who took the slot, or null.</param>
public sealed record RepairLineFact(int SlotNumber, string ReasonCode, Guid? ReplacementPlayerId);

/// <summary>One club's repairs for one fixture, as the lock workflow reports them.</summary>
/// <param name="ClubId">The club whose side was changed.</param>
/// <param name="FixtureId">The fixture the side was frozen for.</param>
/// <param name="Repairs">The decisions the builder made, in slot order.</param>
public sealed record RepairedSideFact(Guid ClubId, Guid FixtureId, IReadOnlyList<RepairLineFact> Repairs);

/// <summary>
/// Turns a matchday's facts into the messages its managers read (master plan §16 Stage 8, F-41).
/// </summary>
/// <remarks>
/// <para>
/// One composer rather than a message built at each call site, so a result, a card, an injury, a table move,
/// and a repaired side all address their manager the same way and resolve a name once. It stages messages
/// and never saves, so they commit inside the transaction that publishes or locks the round — the result and
/// the news of it become public together, which is the same rule the table and the absences follow
/// (`MAT-7`).
/// </para>
/// <para>
/// An AI club has no manager, so its targets are skipped. That is the whole of the "who is told" rule: the
/// message goes to whoever held the club when the round published, and a club nobody holds tells nobody.
/// </para>
/// </remarks>
public sealed class MatchdayNotifications
{
    private readonly IInboxRepository _inbox;
    private readonly INewsRepository _news;

    /// <summary>Initializes the composer.</summary>
    public MatchdayNotifications(IInboxRepository inbox, INewsRepository news)
    {
        _inbox = inbox;
        _news = news;
    }

    /// <summary>Writes the messages one published round produces.</summary>
    /// <param name="roundNumber">The round, 1–34.</param>
    /// <param name="worldId">The world, which scopes the round's news.</param>
    /// <param name="divisionId">The division, which scopes the round's news.</param>
    /// <param name="played">The fixtures the round played.</param>
    /// <param name="positions">Every club's position after the round.</param>
    /// <param name="effects">What the round did to each booked, sent-off, or injured player.</param>
    /// <param name="now">The current instant, taken from the caller's clock (`TIME-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task NotifyPublishedAsync(
        int roundNumber,
        Guid worldId,
        Guid divisionId,
        IReadOnlyList<PlayedFixtureFact> played,
        IReadOnlyList<ClubPositionFact> positions,
        IReadOnlyList<PlayerEffectFact> effects,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(played);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(effects);

        if (played.Count == 0 && positions.Count == 0 && effects.Count == 0)
        {
            return;
        }

        // Every club a message could be addressed to: both ends of every fixture, every club whose position
        // is reported, and every club a card or injury names.
        var clubIds = played
            .SelectMany(fixture => new[] { fixture.HomeClubId, fixture.AwayClubId })
            .Concat(positions.Select(position => position.ClubId))
            .Concat(effects.Select(effect => effect.ClubId))
            .Distinct()
            .ToList();

        var targets = (await _inbox.FindClubTargetsAsync(clubIds, cancellationToken))
            .ToDictionary(target => target.ClubId);

        var names = await _inbox.FindPlayerNamesAsync(
            [.. effects.Select(effect => effect.PlayerId).Distinct()],
            cancellationToken);

        var positionOf = positions.ToDictionary(position => position.ClubId);

        foreach (var fixture in played.OrderBy(fixture => fixture.FixtureId))
        {
            NotifyResult(fixture, fixture.HomeClubId, fixture.AwayClubId, fixture.HomeGoals, fixture.AwayGoals);
            NotifyResult(fixture, fixture.AwayClubId, fixture.HomeClubId, fixture.AwayGoals, fixture.HomeGoals);

            // The news feed is public, so it carries the result for everyone, managers or not (COM-1).
            PostResult(fixture, worldId, divisionId, roundNumber, targets, now);
        }

        foreach (var position in positions.OrderBy(position => position.ClubId))
        {
            if (position.Position == position.PreviousPosition)
            {
                continue;
            }

            if (!TryManager(targets, position.ClubId, out var managerId))
            {
                continue;
            }

            _inbox.Add(NewMessage(
                managerId,
                InboxTemplates.Table(roundNumber, position.Position, position.PreviousPosition),
                now));
        }

        foreach (var effect in effects.OrderBy(effect => effect.ClubId).ThenBy(effect => effect.PlayerId))
        {
            if (!TryManager(targets, effect.ClubId, out var managerId))
            {
                continue;
            }

            var playerName = names.TryGetValue(effect.PlayerId, out var name) ? name : "A player";

            if (effect.SuspensionFixtures > 0)
            {
                var reasons = new List<string>(2);

                if (effect.FromBookings)
                {
                    reasons.Add(InboxTemplates.BookingsReason);
                }

                if (effect.FromRedCard)
                {
                    reasons.Add(InboxTemplates.RedCardReason);
                }

                _inbox.Add(NewMessage(
                    managerId,
                    InboxTemplates.Suspension(playerName, reasons, effect.SuspensionFixtures, effect.PlayerId),
                    now));
            }

            if (effect.InjuryFixtures > 0)
            {
                _inbox.Add(NewMessage(
                    managerId,
                    InboxTemplates.Injury(
                        playerName,
                        effect.InjuryFixtures,
                        WorldRuleSet.InjurySeverityFor(effect.InjuryFixtures),
                        effect.PlayerId),
                    now));
            }
        }

        void NotifyResult(
            PlayedFixtureFact fixture,
            Guid clubId,
            Guid opponentClubId,
            int goalsFor,
            int goalsAgainst)
        {
            if (!TryManager(targets, clubId, out var managerId))
            {
                return;
            }

            var isHome = clubId == fixture.HomeClubId;
            var position = positionOf.TryGetValue(clubId, out var clubPosition)
                ? clubPosition.Position
                : 0;

            _inbox.Add(NewMessage(
                managerId,
                InboxTemplates.Result(
                    roundNumber,
                    targets[opponentClubId].Name,
                    isHome,
                    goalsFor,
                    goalsAgainst,
                    Outcome(goalsFor, goalsAgainst),
                    position,
                    fixture.MatchId),
                now));
        }
    }

    /// <summary>Writes the messages a round's frozen sides produce, one per repaired club (`DIS-7`).</summary>
    /// <param name="roundNumber">The round the sides were frozen for.</param>
    /// <param name="repaired">The clubs whose sides the builder changed.</param>
    /// <param name="now">The current instant, taken from the caller's clock (`TIME-2`).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task NotifyRepairedSidesAsync(
        int roundNumber,
        IReadOnlyList<RepairedSideFact> repaired,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repaired);

        if (repaired.Count == 0)
        {
            return;
        }

        var targets = (await _inbox.FindClubTargetsAsync(
                [.. repaired.Select(side => side.ClubId)],
                cancellationToken))
            .ToDictionary(target => target.ClubId);

        var replacementIds = repaired
            .SelectMany(side => side.Repairs)
            .Where(line => line.ReplacementPlayerId is not null)
            .Select(line => line.ReplacementPlayerId!.Value)
            .Distinct()
            .ToList();

        var names = await _inbox.FindPlayerNamesAsync(replacementIds, cancellationToken);

        foreach (var side in repaired.OrderBy(side => side.ClubId))
        {
            if (!TryManager(targets, side.ClubId, out var managerId))
            {
                continue;
            }

            var lines = side.Repairs
                .OrderBy(line => line.SlotNumber)
                .Select(line => new InboxTemplates.RepairParameter(
                    line.SlotNumber,
                    line.ReasonCode,
                    line.ReplacementPlayerId is { } replacement && names.TryGetValue(replacement, out var name)
                        ? name
                        : null))
                .ToList();

            _inbox.Add(NewMessage(
                managerId,
                InboxTemplates.Repair(roundNumber, lines, side.FixtureId),
                now));
        }
    }

    private static bool TryManager(
        Dictionary<Guid, ClubInboxTarget> targets,
        Guid clubId,
        out Guid managerId)
    {
        if (targets.TryGetValue(clubId, out var target) && target.ManagerId is { } manager && manager != Guid.Empty)
        {
            managerId = manager;

            return true;
        }

        managerId = Guid.Empty;

        return false;
    }

    private static string Outcome(int goalsFor, int goalsAgainst) => goalsFor > goalsAgainst
        ? InboxTemplates.WinOutcome
        : goalsFor < goalsAgainst ? InboxTemplates.LossOutcome : InboxTemplates.DrawOutcome;

    private static InboxMessage NewMessage(Guid managerId, InboxDraft draft, DateTimeOffset now) =>
        InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now);

    private void PostResult(
        PlayedFixtureFact fixture,
        Guid worldId,
        Guid divisionId,
        int roundNumber,
        Dictionary<Guid, ClubInboxTarget> targets,
        DateTimeOffset now)
    {
        var homeName = targets.TryGetValue(fixture.HomeClubId, out var home) ? home.Name : "Home";
        var awayName = targets.TryGetValue(fixture.AwayClubId, out var away) ? away.Name : "Away";

        var draft = NewsTemplates.Round(
            roundNumber,
            homeName,
            fixture.HomeGoals,
            awayName,
            fixture.AwayGoals,
            divisionId,
            fixture.MatchId);

        _news.Add(NewsItem.Publish(
            Guid.CreateVersion7(),
            worldId,
            draft.CountryId,
            draft.DivisionId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            publishedAt: now,
            expiresAt: null,
            now));
    }
}
