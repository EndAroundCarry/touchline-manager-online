using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Comms;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.Squad.Generation;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Squad;

/// <summary>What the rollover's contract-continuity step did (`CON-6`, `CON-8`).</summary>
/// <param name="Retired">How many players retired.</param>
/// <param name="Announced">How many players announced that the coming season is their last.</param>
/// <param name="Renewed">How many contracts were renewed.</param>
/// <param name="Released">How many expired players left for free agency.</param>
/// <param name="Replacements">How many emergency replacement players were created (`SQ-8`).</param>
public sealed record SquadContinuityResult(
    int Retired,
    int Announced,
    int Renewed,
    int Released,
    int Replacements);

/// <summary>
/// Resolves the closing season's contracts at rollover: retirements, expiries, unmanaged-club renewals, and
/// the emergency replacements that keep every club legal (`CON-6`, `CON-8`, `SQ-8`, master plan §7.5).
/// </summary>
/// <remarks>
/// <para>
/// One unit of work, staged into the rollover's <c>Squads</c> phase transaction. It reads a projection of
/// every contracted squad once, then closes, re-signs, retires, and releases the tracked entities, so the
/// whole world's squad continuity commits together with the phase checkpoint and a crash leaves nothing
/// half-applied.
/// </para>
/// <para>
/// The order is deliberate. Retirements come first, so a player who announced last season leaves before
/// expiry can offer them a new deal; then announced players are kept for their final season; then expiring
/// contracts are renewed or released; then any club left below the minimum is repaired. A manager who
/// renewed during the season is unaffected, because their contract no longer expires.
/// </para>
/// </remarks>
public sealed partial class SettleSquadContinuity
{
    /// <summary>The outfield positions replacements cycle through, so a repair keeps a balanced shape.</summary>
    private static readonly PlayerPosition[] OutfieldPositions =
    [
        PlayerPosition.CentreBack,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.Striker,
        PlayerPosition.LeftBack,
        PlayerPosition.RightWinger,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.AttackingMidfielder,
    ];

    private readonly IContractContinuityQueries _continuity;
    private readonly ISquadRepository _squad;
    private readonly IInboxRepository _inbox;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;
    private readonly ILogger<SettleSquadContinuity> _logger;

    /// <summary>Initializes the use case.</summary>
    public SettleSquadContinuity(
        IContractContinuityQueries continuity,
        ISquadRepository squad,
        IInboxRepository inbox,
        IAuditWriter audit,
        IRequestContext requestContext,
        ILogger<SettleSquadContinuity> logger)
    {
        _continuity = continuity;
        _squad = squad;
        _inbox = inbox;
        _audit = audit;
        _requestContext = requestContext;
        _logger = logger;
    }

    /// <summary>Resolves every club's contracts for the season about to be played.</summary>
    /// <param name="world">The world.</param>
    /// <param name="season">The closing season.</param>
    /// <param name="nextSeason">The next season, which replacements register in and renewals start from.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SquadContinuityResult> ExecuteAsync(
        GameWorld world,
        Season season,
        Season nextSeason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(nextSeason);

        var clubs = await _continuity.LoadAsync(world.Id, season.Id, nextSeason.GameYear, cancellationToken);

        if (clubs.Count == 0)
        {
            return new SquadContinuityResult(0, 0, 0, 0, 0);
        }

        var playerIds = clubs.SelectMany(club => club.Players).Select(player => player.PlayerId).Distinct().ToList();

        var players = (await _squad.LoadPlayersAsync(playerIds, cancellationToken)).ToDictionary(player => player.Id);
        var contracts = (await _squad.LoadActiveContractsForClubsAsync(
                [.. clubs.Select(club => club.ClubId)],
                cancellationToken))
            .ToDictionary(contract => contract.Id);
        var registrations = (await _squad.LoadActiveRegistrationsForPlayersAsync(playerIds, cancellationToken))
            .ToDictionary(registration => registration.PlayerId);

        var seed = world.Id.ToString("D");
        var retired = 0;
        var announced = 0;
        var renewed = 0;
        var released = 0;
        var replacements = 0;
        var replacementOrdinal = 0;
        var announcements = new List<(Guid ClubId, Guid PlayerId, string PlayerName)>();
        var repairedClubs = new List<Guid>();

        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            var gone = new HashSet<Guid>();
            var announcedExpiring = new List<ContractContinuityPlayer>();
            var expiring = new List<ContractContinuityPlayer>();
            var stableCount = 0;
            var stableGoalkeepers = 0;

            foreach (var row in club.Players.OrderBy(player => player.PlayerId))
            {
                if (!players.TryGetValue(row.PlayerId, out var player)
                    || !contracts.TryGetValue(row.ContractId, out var contract))
                {
                    continue;
                }

                var decision = RetirementPolicy.Decide(
                    season.Id,
                    row.PlayerId,
                    new RetirementInput(row.Age, row.IsGoalkeeper, row.Ability, row.ConditionBp, row.RetirementAnnounced));

                if (decision == RetirementDecision.Retire)
                {
                    contract.Close(PlayerContractCloseReasons.Retired, now);
                    EndRegistration(registrations, row.PlayerId, now);
                    player.Retire(now);
                    gone.Add(row.PlayerId);
                    retired++;

                    continue;
                }

                if (decision == RetirementDecision.Announce)
                {
                    player.AnnounceRetirement(nextSeason.SequenceNumber, now);
                    announced++;
                    announcements.Add((club.ClubId, row.PlayerId, player.FullName));
                }

                if (row.EndSeasonNumber <= season.SequenceNumber)
                {
                    if (player.IsRetirementAnnounced)
                    {
                        announcedExpiring.Add(row);
                    }
                    else
                    {
                        expiring.Add(row);
                    }
                }
                else
                {
                    stableCount++;

                    if (row.IsGoalkeeper)
                    {
                        stableGoalkeepers++;
                    }
                }
            }

            // An announced player whose deal is expiring is kept for their final season, on a one-season deal.
            foreach (var row in announcedExpiring.OrderBy(row => row.PlayerId))
            {
                Renew(club, row, 1);
                renewed++;
            }

            var stableForPolicy = stableCount + announcedExpiring.Count;
            var stableGoalkeepersForPolicy = stableGoalkeepers + announcedExpiring.Count(row => row.IsGoalkeeper);

            if (club.AttentiveManager)
            {
                // A manager who is present chose not to renew: the players leave for free agency (`CON-6`).
                foreach (var row in expiring.OrderBy(row => row.PlayerId))
                {
                    Release(row);
                    gone.Add(row.PlayerId);
                    released++;
                }
            }
            else
            {
                var candidates = expiring
                    .OrderBy(row => row.PlayerId)
                    .Select(row => new AiContractPlayer(row.PlayerId, row.Ability, row.Age, row.IsGoalkeeper))
                    .ToList();

                var decisions = AiContractPolicy
                    .Decide(stableForPolicy, stableGoalkeepersForPolicy, candidates)
                    .ToDictionary(decision => decision.PlayerId);

                foreach (var row in expiring.OrderBy(row => row.PlayerId))
                {
                    var decision = decisions[row.PlayerId];

                    if (decision.Renew)
                    {
                        Renew(club, row, decision.Seasons);
                        renewed++;
                    }
                    else
                    {
                        Release(row);
                        gone.Add(row.PlayerId);
                        released++;
                    }
                }
            }

            // Repair a club that expiry or retirement left below the minimum (`SQ-8`, `SQ-9`).
            var remaining = club.Players.Count - gone.Count;
            var remainingGoalkeepers = club.Players.Count(row => row.IsGoalkeeper)
                - club.Players.Count(row => row.IsGoalkeeper && gone.Contains(row.PlayerId));

            if (remaining < WorldRuleSet.SquadMinimumRegistered
                || remainingGoalkeepers < WorldRuleSet.MinimumGoalkeepers)
            {
                var outfieldIndex = 0;
                var added = 0;

                // Bounded: at most the minimum plus the goalkeeper minimum are ever needed.
                var limit = WorldRuleSet.SquadMinimumRegistered + WorldRuleSet.MinimumGoalkeepers;

                while ((remaining < WorldRuleSet.SquadMinimumRegistered
                        || remainingGoalkeepers < WorldRuleSet.MinimumGoalkeepers)
                    && added < limit)
                {
                    var position = remainingGoalkeepers < WorldRuleSet.MinimumGoalkeepers
                        ? PlayerPosition.Goalkeeper
                        : OutfieldPositions[outfieldIndex++ % OutfieldPositions.Length];

                    var member = PlayerGenerator.GenerateEmergencyReplacement(
                        seed,
                        club.NamePoolKey,
                        club.CountryCode,
                        world.Id,
                        club.ClubId,
                        club.TierNumber,
                        nextSeason.Id,
                        nextSeason.SequenceNumber,
                        nextSeason.GameYear,
                        position,
                        replacementOrdinal++,
                        now);

                    _squad.AddPlayer(member.Player);
                    _squad.AddPlayerAttributes(member.Attributes);
                    _squad.AddPlayerState(member.State);
                    _squad.AddPlayerContract(member.Contract);
                    _squad.AddPlayerRegistration(member.Registration);

                    remaining++;
                    added++;
                    replacements++;

                    if (position == PlayerPosition.Goalkeeper)
                    {
                        remainingGoalkeepers++;
                    }
                }

                repairedClubs.Add(club.ClubId);

                _audit.Record(new AuditEntry(
                    SquadAuditActions.EmergencyReplacement,
                    AuditActorTypes.Service,
                    _requestContext.ActorUserId,
                    AuditTargetTypes.Club,
                    club.ClubId,
                    _requestContext.CorrelationId,
                    IpHash: null,
                    Reason: $"{added} replacement(s): {club.ClubName}"));

                LogEmergencyReplacement(club.ClubId, added);
            }
        }

        await NotifyAnnouncementsAsync(announcements, now, cancellationToken);

        _audit.Record(new AuditEntry(
            WorldAuditActions.SquadsSettled,
            AuditActorTypes.Service,
            _requestContext.ActorUserId,
            AuditTargetTypes.SeasonRollover,
            season.Id,
            _requestContext.CorrelationId,
            IpHash: null,
            Reason: $"retired {retired}, announced {announced}, renewed {renewed}, released {released}, "
                + $"replacements {replacements}"));

        LogSettled(season.Id, retired, announced, renewed, released, replacements);

        return new SquadContinuityResult(retired, announced, renewed, released, replacements);

        void Renew(ContractContinuityClub club, ContractContinuityPlayer row, int seasons)
        {
            var contract = contracts[row.ContractId];

            var terms = ContractRenewalQuote.Calculate(
                new ContractRenewalInput(
                    row.Ability,
                    row.Potential,
                    row.Age,
                    row.Appearances,
                    row.MoraleBp,
                    club.TierNumber,
                    RemainingSeasons: 0),
                seasons);

            contract.Close(PlayerContractCloseReasons.Renewed, now);

            _squad.AddPlayerContract(PlayerContract.Sign(
                Guid.CreateVersion7(),
                row.PlayerId,
                club.ClubId,
                nextSeason.SequenceNumber,
                nextSeason.SequenceNumber + terms.Seasons - 1,
                terms.WeeklyWageMinor,
                contract.SquadStatus,
                now));
        }

        void Release(ContractContinuityPlayer row)
        {
            contracts[row.ContractId].Close(PlayerContractCloseReasons.Expired, now);
            EndRegistration(registrations, row.PlayerId, now);
            players[row.PlayerId].ReleaseToFreeAgency(now);
        }
    }

    private static void EndRegistration(
        Dictionary<Guid, PlayerRegistration> registrations,
        Guid playerId,
        DateTimeOffset now)
    {
        if (registrations.TryGetValue(playerId, out var registration))
        {
            registration.End(now);
        }
    }

    private async Task NotifyAnnouncementsAsync(
        List<(Guid ClubId, Guid PlayerId, string PlayerName)> announcements,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (announcements.Count == 0)
        {
            return;
        }

        var targets = (await _inbox.FindClubTargetsAsync(
                [.. announcements.Select(announcement => announcement.ClubId).Distinct()],
                cancellationToken))
            .ToDictionary(target => target.ClubId);

        foreach (var announcement in announcements.OrderBy(item => item.ClubId).ThenBy(item => item.PlayerId))
        {
            if (!targets.TryGetValue(announcement.ClubId, out var target)
                || target.ManagerId is not { } managerId
                || managerId == Guid.Empty)
            {
                // An AI club has nobody to tell (COM-1).
                continue;
            }

            var draft = InboxTemplates.Retirement(announcement.PlayerName, announcement.PlayerId);

            _inbox.Add(InboxMessage.Record(
                Guid.CreateVersion7(),
                managerId,
                draft.Category,
                draft.TemplateKey,
                draft.ParametersJson,
                draft.RelatedEntityId,
                now));
        }
    }

    [LoggerMessage(
        EventId = 5300,
        Level = LogLevel.Information,
        Message = "Settled contracts for season {SeasonId}: {Retired} retired, {Announced} announced, "
            + "{Renewed} renewed, {Released} released, {Replacements} replacements (CON-6).")]
    private partial void LogSettled(
        Guid seasonId,
        int retired,
        int announced,
        int renewed,
        int released,
        int replacements);

    [LoggerMessage(
        EventId = 5301,
        Level = LogLevel.Warning,
        Message = "Club {ClubId} needed {Count} emergency replacement(s) below the minimum (SQ-8).")]
    private partial void LogEmergencyReplacement(Guid clubId, int count);
}
