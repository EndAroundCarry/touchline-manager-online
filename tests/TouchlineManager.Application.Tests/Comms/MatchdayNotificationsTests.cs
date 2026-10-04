using FluentAssertions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Tests.Comms;

/// <summary>
/// The messages a round composes, and whom they go to (`F-41`, `DIS-7`).
/// </summary>
/// <remarks>
/// A fake write port rather than a database: the composition is a pure decision about which facts become
/// which messages and which clubs are told, and asserting it against the rendered English keeps the test at
/// the level a manager reads.
/// </remarks>
public sealed class MatchdayNotificationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.CreateVersion7();
    private static readonly Guid DivisionId = Guid.CreateVersion7();

    private static MatchdayNotifications Compose(FakeInbox inbox) => new(inbox, new FakeNews());

    [Fact]
    public async Task Both_sides_hear_their_result_and_nobody_hears_an_unmoved_table()
    {
        var host = Guid.CreateVersion7();
        var away = Guid.CreateVersion7();
        var fixtureId = Guid.CreateVersion7();
        var matchId = Guid.CreateVersion7();

        var inbox = new FakeInbox()
            .WithClub(host, Guid.CreateVersion7(), "Host FC")
            .WithClub(away, Guid.CreateVersion7(), "Away FC");

        await Compose(inbox).NotifyPublishedAsync(
            roundNumber: 4,
            WorldId,
            DivisionId,
            [
                new PlayedFixtureFact(fixtureId, matchId, host, away, HomeGoals: 2, AwayGoals: 1),
            ],
            [
                new ClubPositionFact(host, Position: 1, PreviousPosition: 1),
                new ClubPositionFact(away, Position: 2, PreviousPosition: 2),
            ],
            effects: [],
            Now,
            CancellationToken.None);

        inbox.Added.Should().HaveCount(2, "the table did not move, so there is nothing else to say");

        var hostText = inbox.TextsFor(host);
        hostText.Should().ContainSingle().Which.Title.Should().Be("Round 4: At home to Away FC");
        hostText[0].Spoiler.Should().StartWith("Won 2\u20131");

        var awayText = inbox.TextsFor(away);
        awayText.Should().ContainSingle().Which.Title.Should().Be("Round 4: Away to Host FC");
        awayText[0].Spoiler.Should().StartWith("Lost 1\u20132");
    }

    [Fact]
    public async Task A_moved_club_is_told_where_it_now_stands()
    {
        var club = Guid.CreateVersion7();
        var status = Guid.CreateVersion7();
        var opponent = Guid.CreateVersion7();
        var inbox = new FakeInbox()
            .WithClub(club, status, "Only FC")
            .WithClub(opponent, Guid.CreateVersion7(), "Opponent FC");

        await Compose(inbox).NotifyPublishedAsync(
            roundNumber: 9,
            WorldId,
            DivisionId,
            [
                new PlayedFixtureFact(Guid.CreateVersion7(), Guid.CreateVersion7(), club, opponent, 0, 0),
            ],
            [new ClubPositionFact(club, Position: 5, PreviousPosition: 8)],
            effects: [],
            Now,
            CancellationToken.None);

        var table = inbox.Added
            .Where(message => message.Category == InboxCategory.Table)
            .Select(message => InboxMessageText.Render(message.TemplateKey, message.ParametersJson))
            .ToList();

        table.Should().ContainSingle();
        table[0].Title.Should().Be("Round 9: your league position has changed");
        table[0].Spoiler.Should().StartWith("You are 5th");
    }

    [Fact]
    public async Task A_suspension_and_an_injury_reach_the_club_that_suffered_them()
    {
        var club = Guid.CreateVersion7();
        var status = Guid.CreateVersion7();
        var booked = Guid.CreateVersion7();
        var hurt = Guid.CreateVersion7();

        var inbox = new FakeInbox()
            .WithClub(club, status, "Only FC")
            .WithPlayer(booked, "Ion Popescu")
            .WithPlayer(hurt, "Ana Ionescu");

        await Compose(inbox).NotifyPublishedAsync(
            roundNumber: 3,
            WorldId,
            DivisionId,
            played: [],
            positions: [],
            [
                new PlayerEffectFact(
                    club,
                    booked,
                    SuspensionFixtures: 2,
                    FromBookings: true,
                    FromRedCard: true,
                    InjuryFixtures: 0),
                new PlayerEffectFact(
                    club,
                    hurt,
                    SuspensionFixtures: 0,
                    FromBookings: false,
                    FromRedCard: false,
                    InjuryFixtures: 1),
            ],
            Now,
            CancellationToken.None);

        var texts = inbox.TextsFor(club);

        texts.Should().HaveCount(2);
        texts.Should().Contain(text => text.Title == "Ion Popescu is suspended");
        texts.Should().Contain(text => text.Title == "Ana Ionescu is injured");

        texts.Single(text => text.Title == "Ion Popescu is suspended").Body
            .Should().Be("Ion Popescu misses 2 fixtures after accumulating bookings and a sending-off.");
    }

    [Fact]
    public async Task An_ai_club_tells_nobody_while_its_opponent_is_still_told()
    {
        var managed = Guid.CreateVersion7();
        var ai = Guid.CreateVersion7();

        var inbox = new FakeInbox()
            .WithClub(managed, Guid.CreateVersion7(), "Managed FC")
            .WithClub(ai, managerId: null, "AI FC");

        await Compose(inbox).NotifyPublishedAsync(
            roundNumber: 1,
            WorldId,
            DivisionId,
            [new PlayedFixtureFact(Guid.CreateVersion7(), Guid.CreateVersion7(), managed, ai, 1, 1)],
            positions: [],
            effects: [],
            Now,
            CancellationToken.None);

        inbox.Added.Should().ContainSingle()
            .Which.RecipientManagerId.Should().NotBe(Guid.Empty);

        inbox.TextsFor(managed).Single().Title.Should().Contain("At home to AI FC");
    }

    [Fact]
    public async Task A_repaired_side_names_the_player_who_came_in()
    {
        var club = Guid.CreateVersion7();
        var manager = Guid.CreateVersion7();
        var replacement = Guid.CreateVersion7();
        var inbox = new FakeInbox()
            .WithClub(club, manager, "Only FC")
            .WithPlayer(replacement, "Dan Marin");

        await Compose(inbox).NotifyRepairedSidesAsync(
            roundNumber: 7,
            [
                new RepairedSideFact(
                    club,
                    Guid.CreateVersion7(),
                    [
                        new RepairLineFact(5, "player_unavailable", replacement),
                        new RepairLineFact(12, "slot_empty", null),
                    ]),
            ],
            Now,
            CancellationToken.None);

        var text = inbox.TextsFor(club).Single();

        text.Title.Should().Be("Your side was changed");
        text.Body.Should().Be(
            "Before round 7, these places in your side were decided for you: "
            + "slot 5: the player was unavailable, Dan Marin came in; slot 12: it was empty.");
    }

    /// <summary>A write port that records what was staged and answers the addressing reads from a table.</summary>
    private sealed class FakeInbox : IInboxRepository
    {
        private readonly Dictionary<Guid, ClubInboxTarget> _clubs = [];
        private readonly Dictionary<Guid, string> _players = [];

        public List<InboxMessage> Added { get; } = [];

        public FakeInbox WithClub(Guid clubId, Guid? managerId, string name)
        {
            _clubs[clubId] = new ClubInboxTarget(clubId, managerId, name);

            return this;
        }

        public FakeInbox WithPlayer(Guid playerId, string name)
        {
            _players[playerId] = name;

            return this;
        }

        public void Add(InboxMessage message) => Added.Add(message);

        public Task<InboxMessage?> FindAsync(Guid managerId, Guid messageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<InboxMessage>> LoadUnreadAsync(Guid managerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClubInboxTarget>> FindClubTargetsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubInboxTarget>>(
                [.. clubIds.Where(_clubs.ContainsKey).Select(clubId => _clubs[clubId])]);

        public Task<IReadOnlyDictionary<Guid, string>> FindPlayerNamesAsync(
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                playerIds.Where(_players.ContainsKey).ToDictionary(playerId => playerId, playerId => _players[playerId]));

        public Task<IReadOnlyDictionary<Guid, string>> FindManagerEmailsAsync(
            IReadOnlyCollection<Guid> managerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                managerIds.ToDictionary(managerId => managerId, _ => "manager@example.test"));

        /// <summary>Renders every message staged for one club's manager, as the English a manager reads.</summary>
        /// <param name="clubId">The club whose manager is read.</param>
        public List<InboxText> TextsFor(Guid clubId)
        {
            var managerId = _clubs[clubId].ManagerId;

            return
            [
                .. Added
                    .Where(message => message.RecipientManagerId == managerId)
                    .OrderBy(message => message.Id)
                    .Select(message => InboxMessageText.Render(message.TemplateKey, message.ParametersJson)),
            ];
        }
    }

    /// <summary>A news write port that records what was staged.</summary>
    private sealed class FakeNews : INewsRepository
    {
        public List<NewsItem> Added { get; } = [];

        public void Add(NewsItem item) => Added.Add(item);

        public Task<IReadOnlyList<NewsItem>> LoadAsync(
            NewsPageQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
