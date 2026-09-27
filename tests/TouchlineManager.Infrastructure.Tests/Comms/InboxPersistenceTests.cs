using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.Competition;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.Competition;

namespace TouchlineManager.Infrastructure.Tests.Comms;

/// <summary>
/// The inbox over a real seeded world: the messages a played round writes, and the paged read a manager
/// walks (`F-41`, master plan §6.9, §10.7).
/// </summary>
/// <remarks>
/// The round is played by the real workflow, so the test proves the wiring a fake cannot: publication
/// writing messages in its own transaction, and the keyset read translating its same-instant tie-break
/// against PostgreSQL rather than only in memory.
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class InboxPersistenceTests
{
    /// <summary>Initializes the tests.</summary>
    public InboxPersistenceTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests write into.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task A_published_round_writes_a_result_for_the_manager_who_held_the_club()
    {
        var now = Fixture.Clock.UtcNow;

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var round = await ClaimRoundAsync(db);

        var fixture = await db.Fixtures
            .Where(candidate => candidate.MatchdayId == round)
            .OrderBy(candidate => candidate.Id)
            .FirstAsync();

        var manager = await AppointManagerAsync(db, fixture.HomeClubId, now);
        var opponent = await db.Clubs
            .Where(club => club.Id == fixture.AwayClubId)
            .Select(club => club.Name)
            .SingleAsync();

        await LockResolveAndPublishAsync(scope, round);

        var inbox = await scope.ServiceProvider.GetRequiredService<IInboxQueries>()
            .GetInboxAsync(manager.Id, new InboxPageQuery(null, UnreadOnly: false), CancellationToken.None);

        inbox.UnreadCount.Should().BeGreaterThan(0, "the round told the manager what happened");

        var texts = inbox.Messages
            .Select(message => InboxMessageText.Render(message.TemplateKey, message.ParametersJson))
            .ToList();

        texts.Should().Contain(
            text => text.Body.Contains(opponent, StringComparison.Ordinal),
            "the result names the opponent");
    }

    [Fact]
    public async Task The_inbox_pages_newest_first_and_the_cursor_walks_the_rest()
    {
        var now = new DateTimeOffset(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var manager = await AppointManagerAsync(db, clubId: null, now);

        // Every message carries the same instant, which a round does: the identity is what makes the order
        // total, and the walk must neither skip nor repeat one because of the tie.
        for (var index = 0; index < 30; index++)
        {
            db.InboxMessages.Add(InboxMessage.Record(
                Guid.CreateVersion7(),
                manager.Id,
                InboxCategory.Result,
                InboxTemplates.ResultRecorded,
                """{"roundNumber":1,"opponentName":"Vale","isHome":true,"goalsFor":1,"goalsAgainst":0,"outcome":"win","position":1}""",
                relatedEntityId: null,
                now));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var queries = scope.ServiceProvider.GetRequiredService<IInboxQueries>();

        var first = await queries.GetInboxAsync(manager.Id, new InboxPageQuery(null, false), CancellationToken.None);

        first.Messages.Should().HaveCount(queries.PageSize);
        first.HasMore.Should().BeTrue();
        first.UnreadCount.Should().Be(30);

        var last = first.Messages[^1];
        var second = await queries.GetInboxAsync(
            manager.Id,
            new InboxPageQuery(new InboxCursorPosition(last.CreatedAt, last.Id), false),
            CancellationToken.None);

        second.Messages.Should().HaveCount(5);
        second.HasMore.Should().BeFalse();

        var ids = first.Messages.Concat(second.Messages).Select(message => message.Id).ToList();

        ids.Should().HaveCount(30);
        ids.Should().OnlyHaveUniqueItems("the tie-break keeps one message from appearing twice");
    }

    [Fact]
    public async Task Marking_messages_read_persists_and_drops_the_unread_count()
    {
        var now = new DateTimeOffset(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var manager = await AppointManagerAsync(db, clubId: null, now);

        for (var index = 0; index < 3; index++)
        {
            db.InboxMessages.Add(InboxMessage.Record(
                Guid.CreateVersion7(),
                manager.Id,
                InboxCategory.Injury,
                InboxTemplates.InjuryReported,
                """{"playerName":"Ana","fixtures":1,"severity":"minor"}""",
                relatedEntityId: null,
                now));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        var unread = await repository.LoadUnreadAsync(manager.Id, CancellationToken.None);

        unread.Should().HaveCount(3);

        foreach (var message in unread)
        {
            message.MarkRead(now);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        await using var readScope = Fixture.CreateScope();
        var queries = readScope.ServiceProvider.GetRequiredService<IInboxQueries>();

        (await queries.CountUnreadAsync(manager.Id, CancellationToken.None)).Should().Be(0);

        var page = await queries.GetInboxAsync(manager.Id, new InboxPageQuery(null, true), CancellationToken.None);

        page.Messages.Should().BeEmpty("the unread filter excludes what was just read");
    }

    /// <summary>Appoints a manager to a club, or to no club when the club is omitted.</summary>
    private static async Task<Manager> AppointManagerAsync(TouchlineManagerDbContext db, Guid? clubId, DateTimeOffset now)
    {
        // A manager profile hangs off an account (FK_managers_users_user_id), so the account comes first.
        var suffix = Guid.NewGuid().ToString("N");
        var userId = Guid.CreateVersion7();

        db.Users.Add(User.Register(
            userId,
            $"inbox-{suffix}@example.com",
            $"Inbox {suffix}"[..24],
            "hash",
            "stamp",
            now));

        var manager = Manager.Create(Guid.CreateVersion7(), userId, "en-GB", "Europe/London", now);

        db.Managers.Add(manager);

        if (clubId is { } club)
        {
            db.ClubTenures.Add(ClubTenure.Start(
                Guid.CreateVersion7(),
                club,
                manager.Id,
                $"inbox-test-{Guid.NewGuid():N}",
                now));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        return manager;
    }

    /// <summary>Plays one round through the real workflow, so its publication writes the messages.</summary>
    private static async Task LockResolveAndPublishAsync(AsyncServiceScope scope, Guid matchdayId)
    {
        await scope.ServiceProvider.GetRequiredService<LockMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);
    }

    /// <summary>Finds one untouched round of a division.</summary>
    private static async Task<Guid> ClaimRoundAsync(TouchlineManagerDbContext db)
    {
        var divisionSeasonId = await db.DivisionSeasons
            .OrderBy(divisionSeason => divisionSeason.CreatedAt)
            .Select(divisionSeason => divisionSeason.Id)
            .FirstAsync();

        return await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId
                && matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }
}
