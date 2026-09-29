using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Tests.Squad;

/// <summary>
/// The rollover's contract continuity: retirement, expiry into free agency, unmanaged-club renewal, and the
/// emergency replacements that keep every club legal (`CON-6`, `CON-8`, `SQ-8`).
/// </summary>
public sealed class SettleSquadContinuityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.CreateVersion7();
    private static readonly Guid ClosingSeasonId = Guid.CreateVersion7();
    private static readonly LaunchCountry Country = LaunchCountries.All[0];

    [Fact]
    public async Task An_announced_player_retires_and_the_emptied_club_is_repaired()
    {
        var seasonId = Guid.CreateVersion7();
        var clubId = Guid.CreateVersion7();
        var player = Roster(clubId, seasonId, endSeason: 2, alreadyAnnounced: true);

        var result = await Settle(Continuity(Club(clubId, attentive: false, Row(player, endSeason: 2, announced: true))), Squad(player));

        result.Retired.Should().Be(1);

        player.Contract.ClosedReason.Should().Be(PlayerContractCloseReasons.Retired);
        player.Player.Status.Should().Be(PlayerStatus.Retired);
        player.Registration.Status.Should().Be(RegistrationStatus.Ended);

        result.Replacements.Should().Be(WorldRuleSet.SquadMinimumRegistered, "an emptied club is repaired (SQ-8)");
    }

    [Fact]
    public async Task A_human_clubs_expiring_player_leaves_but_an_ai_clubs_is_renewed()
    {
        var seasonId = Guid.CreateVersion7();
        var humanClub = Guid.CreateVersion7();
        var aiClub = Guid.CreateVersion7();

        var human = Roster(humanClub, seasonId, endSeason: 1, alreadyAnnounced: false);
        var ai = Roster(aiClub, seasonId, endSeason: 1, alreadyAnnounced: false);

        var squad = Squad(human, ai);

        var result = await Settle(
            Continuity(
                Club(humanClub, attentive: true, Row(human, endSeason: 1, announced: false)),
                Club(aiClub, attentive: false, Row(ai, endSeason: 1, announced: false))),
            squad);

        result.Released.Should().Be(1);
        result.Renewed.Should().Be(1);

        human.Contract.ClosedReason.Should().Be(PlayerContractCloseReasons.Expired);
        human.Player.Status.Should().Be(PlayerStatus.FreeAgent);
        human.Registration.Status.Should().Be(RegistrationStatus.Ended);

        ai.Contract.ClosedReason.Should().Be(PlayerContractCloseReasons.Renewed);
        ai.Player.Status.Should().Be(PlayerStatus.Active);

        var renewed = squad.AddedContracts.Should().ContainSingle(contract => contract.PlayerId == ai.Player.Id).Which;
        renewed.StartSeasonNumber.Should().Be(2);
    }

    [Fact]
    public async Task A_club_short_of_the_minimum_is_repaired_with_goalkeepers_first()
    {
        var seasonId = Guid.CreateVersion7();
        var clubId = Guid.CreateVersion7();
        var player = Roster(clubId, seasonId, endSeason: 2, alreadyAnnounced: false);

        var squad = Squad(player);

        var result = await Settle(
            Continuity(Club(clubId, attentive: true, Row(player, endSeason: 2, announced: false))),
            squad);

        result.Replacements.Should().Be(17, "one player plus seventeen reaches the minimum of eighteen");

        squad.AddedPlayers.Should().HaveCount(17);
        squad.AddedPlayers.Should().Contain(
            replacement => replacement.PrimaryPosition == PlayerPosition.Goalkeeper,
            "a club with no goalkeeper is repaired with one (SQ-2)");
    }

    [Fact]
    public async Task A_player_who_announces_is_kept_for_a_final_season()
    {
        var seasonId = Guid.CreateVersion7();
        var clubId = Guid.CreateVersion7();

        // The announcement is a deterministic draw, so a specific player either announces or does not; find
        // one who does, so the "kept for one more season" path is exercised.
        var announcing = FindAnnouncingPlayerId(ClosingSeasonId);
        var row = Roster(clubId, seasonId, endSeason: 1, alreadyAnnounced: false, playerId: announcing);

        var squad = Squad(row);

        var result = await Settle(
            Continuity(Club(clubId, attentive: true, Row(row, endSeason: 1, announced: false, age: 33, ability: 7, conditionBp: 2_000))),
            squad);

        result.Announced.Should().Be(1);
        result.Released.Should().Be(0);
        result.Renewed.Should().Be(1, "an announced player is kept for their final season");

        row.Player.RetirementAnnouncedSeasonNumber.Should().Be(2);
        row.Contract.ClosedReason.Should().Be(PlayerContractCloseReasons.Renewed);
    }

    private static Guid FindAnnouncingPlayerId(Guid seasonId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var id = Guid.CreateVersion7();

            if (RetirementPolicy.Decide(seasonId, id, new RetirementInput(33, false, 7, 2_000, false))
                == RetirementDecision.Announce)
            {
                return id;
            }
        }

        throw new InvalidOperationException("No announcing player was found in fifty attempts.");
    }

    private static Task<SquadContinuityResult> Settle(
        StubContinuityQueries continuity,
        RecordingSquadRepository squad) =>
        new SettleSquadContinuity(
                continuity,
                squad,
                new NullInboxRepository(),
                new NullAuditWriter(),
                new NullRequestContext(),
                NullLogger<SettleSquadContinuity>.Instance)
            .ExecuteAsync(
                GameWorld.Create(WorldId, "Test World", Now),
                SeasonShell(ClosingSeasonId, 1, 2026, new DateOnly(2026, 9, 1)),
                SeasonShell(Guid.CreateVersion7(), 2, 2027, new DateOnly(2027, 9, 7)),
                Now,
                CancellationToken.None);

    private static Season SeasonShell(Guid id, int sequenceNumber, int gameYear, DateOnly firstMatchday) =>
        Season.Create(id, WorldId, sequenceNumber, gameYear, WorldRuleSet.Version, firstMatchday, Now);

    private static ContractContinuityClub Club(
        Guid clubId,
        bool attentive,
        params ContractContinuityPlayer[] players) =>
        new(clubId, "Test Club", Country.Code, Country.NamePoolKey, 1, attentive, players);

    private static ContractContinuityPlayer Row(
        RosterRow row,
        int endSeason,
        bool announced,
        int age = 25,
        int ability = 14,
        int conditionBp = 7_000) =>
        new(
            row.Player.Id,
            row.Contract.Id,
            endSeason,
            row.Contract.WeeklyWageMinor,
            row.Contract.SquadStatus,
            PlayerPositions.FamilyOf(row.Player.PrimaryPosition),
            row.Player.PrimaryPosition == PlayerPosition.Goalkeeper,
            age,
            ability,
            Potential: 14,
            MoraleBp: 5_000,
            conditionBp,
            Appearances: 10,
            announced);

    private static StubContinuityQueries Continuity(params ContractContinuityClub[] clubs) => new(clubs);

    private static RecordingSquadRepository Squad(params RosterRow[] rows) => new(rows);

    private static RosterRow Roster(
        Guid clubId,
        Guid seasonId,
        int endSeason,
        bool alreadyAnnounced,
        Guid? playerId = null,
        PlayerPosition position = PlayerPosition.Striker)
    {
        var id = playerId ?? Guid.CreateVersion7();
        var player = Player.Generate(id, WorldId, Identity(position), Now);

        if (alreadyAnnounced)
        {
            player.AnnounceRetirement(1, Now);
        }

        var contract = PlayerContract.Sign(
            Guid.CreateVersion7(),
            id,
            clubId,
            1,
            endSeason,
            100_000,
            SquadStatus.Rotation,
            Now);

        var registration = PlayerRegistration.Register(
            Guid.CreateVersion7(),
            id,
            clubId,
            seasonId,
            effectiveFixtureBoundaryRound: 0,
            Now);

        return new RosterRow(player, contract, registration);
    }

    private static PlayerIdentity Identity(PlayerPosition position) => new(
        "Corin Alderwick",
        "C. Alderwick",
        "ENG",
        "name-seed",
        BirthGameYear: 2001,
        BirthDayOfYear: 100,
        PreferredFoot.Right,
        HeightCm: 180,
        WeightKg: 75,
        position,
        [],
        Potential: 14,
        Reputation: 12);

    private sealed record RosterRow(Player Player, PlayerContract Contract, PlayerRegistration Registration);

    private sealed class StubContinuityQueries(ContractContinuityClub[] clubs) : IContractContinuityQueries
    {
        public Task<IReadOnlyList<ContractContinuityClub>> LoadAsync(
            Guid worldId,
            Guid seasonId,
            int gameYear,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContractContinuityClub>>(clubs);
    }

    private sealed class RecordingSquadRepository : ISquadRepository
    {
        private readonly List<RosterRow> _rows;

        public RecordingSquadRepository(IEnumerable<RosterRow> rows) => _rows = [.. rows];

        public List<Player> AddedPlayers { get; } = [];

        public List<PlayerContract> AddedContracts { get; } = [];

        public List<PlayerRegistration> AddedRegistrations { get; } = [];

        public void AddPlayer(Player player) => AddedPlayers.Add(player);

        public void AddPlayerAttributes(PlayerAttributes attributes)
        {
        }

        public void AddPlayerState(PlayerState state)
        {
        }

        public void AddPlayerContract(PlayerContract contract) => AddedContracts.Add(contract);

        public void AddPlayerRegistration(PlayerRegistration registration) => AddedRegistrations.Add(registration);

        public Task<PlayerContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlayerContract?> FindActiveContractAsync(Guid playerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlayerRegistration?> FindActiveRegistrationAsync(
            Guid playerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Player>> LoadPlayersAsync(
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Player>>(
                [.. _rows.Select(row => row.Player).Where(player => playerIds.Contains(player.Id))]);

        public Task<IReadOnlyList<PlayerContract>> LoadActiveContractsForClubsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlayerContract>>(
                [.. _rows.Select(row => row.Contract).Where(contract => clubIds.Contains(contract.ClubId))]);

        public Task<IReadOnlyList<PlayerRegistration>> LoadActiveRegistrationsForPlayersAsync(
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlayerRegistration>>(
                [.. _rows.Select(row => row.Registration).Where(registration => playerIds.Contains(registration.PlayerId))]);
    }

    private sealed class NullInboxRepository : IInboxRepository
    {
        public void Add(InboxMessage message)
        {
        }

        public Task<InboxMessage?> FindAsync(Guid managerId, Guid messageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<InboxMessage>> LoadUnreadAsync(
            Guid managerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClubInboxTarget>> FindClubTargetsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubInboxTarget>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> FindPlayerNamesAsync(
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, string>> FindManagerEmailsAsync(
            IReadOnlyCollection<Guid> managerIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public void Record(AuditEntry entry)
        {
        }
    }

    private sealed class NullRequestContext : IRequestContext
    {
        public Guid? ActorUserId => null;

        public string CorrelationId => "test";

        public string? IpAddress => null;

        public string? UserAgent => null;
    }
}
