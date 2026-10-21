using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Domain.Auth;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.Competition;

namespace Tactical11Manager.Infrastructure.Tests.Comms;

/// <summary>
/// Direct messages over a real PostgreSQL: the paged list and thread, the unread count, the block lookup, and
/// the check constraints that back the domain rules (`COM-10` to `COM-16`).
/// </summary>
/// <remarks>
/// The keyset reads are the part a fake cannot prove: the same-instant tie-break and the descending indexes
/// are translated by PostgreSQL, not by the language.
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class DirectMessagePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Initializes the tests.</summary>
    public DirectMessagePersistenceTests(MatchdayFixture fixture) => Fixture = fixture;

    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task Conversations_page_newest_activity_first_and_the_cursor_walks_the_rest()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var me = await AppointManagerAsync(db, "Me");
        var other = await AppointManagerAsync(db, "Other");

        // Thirty conversations at one instant, which a script can produce: the identity is what makes the
        // order total, and the walk must neither skip nor repeat one because of the tie.
        for (var index = 0; index < 30; index++)
        {
            db.Conversations.Add(Conversation.Start(
                Guid.CreateVersion7(),
                $"Subject {index}",
                index % 2 == 0 ? me.Id : other.Id,
                index % 2 == 0 ? other.Id : me.Id,
                clubId,
                Now));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var queries = scope.ServiceProvider.GetRequiredService<IDirectMessageQueries>();

        var first = await queries.GetConversationsAsync(me.Id, null, CancellationToken.None);

        first.Conversations.Should().HaveCount(DirectMessageRules.ConversationPageSize);
        first.HasMore.Should().BeTrue("a further page exists");

        var last = first.Conversations[^1];
        var second = await queries.GetConversationsAsync(
            me.Id,
            new DirectMessageCursorPosition(last.LastMessageAt, last.Id),
            CancellationToken.None);

        second.Conversations.Should().HaveCount(5);
        second.HasMore.Should().BeFalse();

        first.Conversations.Concat(second.Conversations).Select(row => row.Id)
            .Should().OnlyHaveUniqueItems("the tie-break keeps one conversation from appearing twice")
            .And.HaveCount(30, "the walk skips none");
    }

    [Fact]
    public async Task A_reply_moves_its_conversation_to_the_top_and_the_list_shows_the_other_side_and_the_latest_text()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var alice = await AppointManagerAsync(db, "Alice", holdsClub: clubId);
        var ben = await AppointManagerAsync(db, "Ben");

        var older = await StartAsync(db, alice, ben, "Older thread", "first words", clubId, Now);
        var newer = await StartAsync(db, alice, ben, "Newer thread", "other words", clubId, Now.AddMinutes(1));

        newer.Should().NotBe(older);

        // Ben replies in the older thread, so it becomes the most recently active.
        var conversation = await db.Conversations.SingleAsync(candidate => candidate.Id == older);
        db.DirectMessages.Add(DirectMessage.Write(
            Guid.CreateVersion7(), older, ben.Id, alice.Id, "the latest reply\nover two lines", Now.AddMinutes(5)));
        conversation.RecordReply(Now.AddMinutes(5));
        await db.SaveChangesAsync(CancellationToken.None);

        var page = await scope.ServiceProvider.GetRequiredService<IDirectMessageQueries>()
            .GetConversationsAsync(alice.Id, null, CancellationToken.None);

        page.Conversations.Select(row => row.Id).Should().ContainInOrder(older, newer);

        var top = page.Conversations[0];

        top.Subject.Should().Be("Older thread");
        top.Other.ManagerId.Should().Be(ben.Id);
        top.Other.DisplayName.Should().StartWith("Ben");
        top.Other.ClubId.Should().BeNull("Ben holds no club");
        top.LastMessagePreview.Should().Be("the latest reply over two lines", "the preview is the newest message on one line");
        top.UnreadCount.Should().Be(1, "Ben's reply is addressed to Alice and unread (COM-16)");

        var aliceSeenByBen = await scope.ServiceProvider.GetRequiredService<IDirectMessageQueries>()
            .GetConversationsAsync(ben.Id, null, CancellationToken.None);

        aliceSeenByBen.Conversations[0].Other.ClubId.Should().Be(clubId, "the other side's club is where they are now (COM-10)");
        aliceSeenByBen.Conversations[0].Other.ClubName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_threads_cursor_walks_older_messages_without_skipping_or_repeating()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var alice = await AppointManagerAsync(db, "Alice");
        var ben = await AppointManagerAsync(db, "Ben");
        var conversationId = await StartAsync(db, alice, ben, "Long thread", "message 0", clubId, Now);

        // Sixty-four more messages, sixteen of them sharing one instant.
        for (var index = 1; index <= 64; index++)
        {
            var sender = index % 2 == 0 ? alice : ben;
            var recipient = index % 2 == 0 ? ben : alice;

            db.DirectMessages.Add(DirectMessage.Write(
                Guid.CreateVersion7(),
                conversationId,
                sender.Id,
                recipient.Id,
                $"message {index}",
                index > 48 ? Now.AddMinutes(48) : Now.AddMinutes(index)));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var queries = scope.ServiceProvider.GetRequiredService<IDirectMessageQueries>();

        var first = await queries.GetThreadAsync(alice.Id, conversationId, null, CancellationToken.None);

        first.Should().NotBeNull();
        first!.Messages.Should().HaveCount(DirectMessageRules.ThreadPageSize);
        first.HasMore.Should().BeTrue();
        first.Messages[0].SentAt.Should().Be(Now.AddMinutes(48), "the newest page comes first");
        first.Messages.Select(message => message.FromMe).Distinct().Should().HaveCount(2, "the reader sees both sides");

        var last = first.Messages[^1];
        var second = await queries.GetThreadAsync(
            alice.Id,
            conversationId,
            new DirectMessageCursorPosition(last.SentAt, last.Id),
            CancellationToken.None);

        second!.Messages.Should().HaveCount(15);
        second.HasMore.Should().BeFalse();

        first.Messages.Concat(second.Messages).Select(message => message.Id)
            .Should().OnlyHaveUniqueItems().And.HaveCount(65);
    }

    [Fact]
    public async Task A_third_manager_cannot_read_a_thread_and_a_missing_one_is_the_same_answer()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var alice = await AppointManagerAsync(db, "Alice");
        var ben = await AppointManagerAsync(db, "Ben");
        var stranger = await AppointManagerAsync(db, "Stranger");
        var conversationId = await StartAsync(db, alice, ben, "Private", "for two only", clubId, Now);

        var queries = scope.ServiceProvider.GetRequiredService<IDirectMessageQueries>();
        var repository = scope.ServiceProvider.GetRequiredService<IDirectMessageRepository>();

        (await queries.GetThreadAsync(stranger.Id, conversationId, null, CancellationToken.None))
            .Should().BeNull("a conversation of two other managers is not found (COM-10)");
        (await queries.GetThreadAsync(alice.Id, Guid.CreateVersion7(), null, CancellationToken.None))
            .Should().BeNull("a missing conversation looks the same");
        (await repository.FindConversationAsync(stranger.Id, conversationId, CancellationToken.None))
            .Should().BeNull("the staging finder is participant-scoped as well");
        (await queries.GetConversationsAsync(stranger.Id, null, CancellationToken.None)).Conversations
            .Should().BeEmpty();
    }

    [Fact]
    public async Task The_unread_count_is_only_unread_messages_addressed_to_the_manager()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var alice = await AppointManagerAsync(db, "Alice");
        var ben = await AppointManagerAsync(db, "Ben");
        var conversationId = await StartAsync(db, alice, ben, "Counting", "hello Ben", clubId, Now);

        // Two more to Ben, three to Alice, one of Alice's already read.
        for (var index = 0; index < 2; index++)
        {
            db.DirectMessages.Add(DirectMessage.Write(
                Guid.CreateVersion7(), conversationId, alice.Id, ben.Id, $"to Ben {index}", Now.AddMinutes(index + 1)));
        }

        var read = DirectMessage.Write(Guid.CreateVersion7(), conversationId, ben.Id, alice.Id, "already read", Now.AddMinutes(10));
        read.MarkRead(Now.AddMinutes(11));
        db.DirectMessages.Add(read);

        for (var index = 0; index < 2; index++)
        {
            db.DirectMessages.Add(DirectMessage.Write(
                Guid.CreateVersion7(), conversationId, ben.Id, alice.Id, $"to Alice {index}", Now.AddMinutes(12 + index)));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var queries = scope.ServiceProvider.GetRequiredService<IDirectMessageQueries>();

        (await queries.CountUnreadAsync(ben.Id, CancellationToken.None)).Should().Be(3, "three messages wait for Ben (COM-16)");
        (await queries.CountUnreadAsync(alice.Id, CancellationToken.None)).Should().Be(2, "Alice's read message does not count");

        var repository = scope.ServiceProvider.GetRequiredService<IDirectMessageRepository>();

        foreach (var message in await repository.LoadUnreadAsync(ben.Id, conversationId, CancellationToken.None))
        {
            message.MarkRead(Now.AddHours(1));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        (await queries.CountUnreadAsync(ben.Id, CancellationToken.None)).Should().Be(0, "opening the thread marks Ben's messages read");
        (await queries.CountUnreadAsync(alice.Id, CancellationToken.None))
            .Should().Be(2, "opening it for Ben does not read what is addressed to Alice");
    }

    [Fact]
    public async Task A_block_in_either_direction_is_found_and_unblocking_removes_it()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var alice = await AppointManagerAsync(db, "Alice");
        var ben = await AppointManagerAsync(db, "Ben");
        var carol = await AppointManagerAsync(db, "Carol");

        var repository = scope.ServiceProvider.GetRequiredService<IDirectMessageRepository>();

        (await repository.IsBlockedBetweenAsync(alice.Id, ben.Id, CancellationToken.None)).Should().BeFalse();

        repository.Add(ManagerBlock.Create(ben.Id, alice.Id, Now));
        await db.SaveChangesAsync(CancellationToken.None);

        (await repository.IsBlockedBetweenAsync(alice.Id, ben.Id, CancellationToken.None))
            .Should().BeTrue("a block stops both of them (COM-15)");
        (await repository.IsBlockedBetweenAsync(ben.Id, alice.Id, CancellationToken.None)).Should().BeTrue();
        (await repository.IsBlockedBetweenAsync(alice.Id, carol.Id, CancellationToken.None))
            .Should().BeFalse("a block is between one pair only");
        (await repository.FindBlockAsync(alice.Id, ben.Id, CancellationToken.None))
            .Should().BeNull("Alice has not blocked Ben, Ben blocked Alice");

        var block = await repository.FindBlockAsync(ben.Id, alice.Id, CancellationToken.None);

        block.Should().NotBeNull();
        repository.Remove(block!);
        await db.SaveChangesAsync(CancellationToken.None);

        (await repository.IsBlockedBetweenAsync(alice.Id, ben.Id, CancellationToken.None))
            .Should().BeFalse("unblocking restores messaging (COM-15)");
    }

    [Fact]
    public async Task The_thread_and_the_list_say_who_has_blocked_whom_and_existing_messages_stay_readable()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var alice = await AppointManagerAsync(db, "Alice");
        var ben = await AppointManagerAsync(db, "Ben");
        var conversationId = await StartAsync(db, alice, ben, "Before the block", "still readable", clubId, Now);

        db.ManagerBlocks.Add(ManagerBlock.Create(ben.Id, alice.Id, Now.AddMinutes(1)));
        await db.SaveChangesAsync(CancellationToken.None);

        var queries = scope.ServiceProvider.GetRequiredService<IDirectMessageQueries>();

        var asAlice = await queries.GetThreadAsync(alice.Id, conversationId, null, CancellationToken.None);
        var asBen = await queries.GetThreadAsync(ben.Id, conversationId, null, CancellationToken.None);

        asAlice!.BlockedMe.Should().BeTrue();
        asAlice.BlockedByMe.Should().BeFalse();
        asAlice.Messages.Should().ContainSingle("existing messages stay readable (COM-15)");
        asBen!.BlockedByMe.Should().BeTrue();
        asBen.BlockedMe.Should().BeFalse();

        (await queries.GetConversationsAsync(ben.Id, null, CancellationToken.None)).Conversations[0].BlockedByMe
            .Should().BeTrue();
        (await queries.GetConversationsAsync(alice.Id, null, CancellationToken.None)).Conversations[0].BlockedByMe
            .Should().BeFalse();
    }

    [Fact]
    public async Task The_database_rejects_a_conversation_with_oneself()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var alice = await AppointManagerAsync(db, "Alice");
        var ben = await AppointManagerAsync(db, "Ben");

        var conversation = Conversation.Start(Guid.CreateVersion7(), "Fine on paper", alice.Id, ben.Id, clubId, Now);

        // Same-manager rows cannot be built through the domain, so the constraint is exercised in SQL.
        var act = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into comms.conversations
                (id, subject, started_by_manager_id, recipient_manager_id, started_from_club_id,
                 created_at, last_message_at, updated_at, version)
            values ({conversation.Id}, 'Self', {alice.Id}, {alice.Id}, {clubId}, {Now}, {Now}, {Now}, 1)
            """);

        var thrown = await act.Should().ThrowAsync<PostgresException>("a conversation is between two managers (COM-10)");
        thrown.Which.ConstraintName.Should().Be("ck_conversations_participants");
    }

    [Fact]
    public async Task The_database_rejects_a_message_to_oneself_and_a_block_on_oneself()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var clubId = await AnyClubAsync(db);
        var alice = await AppointManagerAsync(db, "Alice");
        var ben = await AppointManagerAsync(db, "Ben");
        var conversationId = await StartAsync(db, alice, ben, "Constraints", "hello", clubId, Now);

        var selfMessage = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into comms.direct_messages (id, conversation_id, sender_manager_id, recipient_manager_id, body, sent_at)
            values ({Guid.CreateVersion7()}, {conversationId}, {alice.Id}, {alice.Id}, 'to me', {Now})
            """);

        (await selfMessage.Should().ThrowAsync<PostgresException>("a message goes between two managers (COM-10)"))
            .Which.ConstraintName.Should().Be("ck_direct_messages_participants");

        var emptyBody = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into comms.direct_messages (id, conversation_id, sender_manager_id, recipient_manager_id, body, sent_at)
            values ({Guid.CreateVersion7()}, {conversationId}, {alice.Id}, {ben.Id}, '', {Now})
            """);

        (await emptyBody.Should().ThrowAsync<PostgresException>("a body has at least one character (COM-13)"))
            .Which.ConstraintName.Should().Be("ck_direct_messages_body");

        var selfBlock = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into comms.manager_blocks (blocker_manager_id, blocked_manager_id, created_at)
            values ({alice.Id}, {alice.Id}, {Now})
            """);

        (await selfBlock.Should().ThrowAsync<PostgresException>("a manager cannot block themselves (COM-15)"))
            .Which.ConstraintName.Should().Be("ck_manager_blocks_distinct");
    }

    /// <summary>Starts a conversation with its first message and saves both.</summary>
    private static async Task<Guid> StartAsync(
        Tactical11ManagerDbContext db,
        Manager starter,
        Manager recipient,
        string subject,
        string body,
        Guid clubId,
        DateTimeOffset at)
    {
        var conversation = Conversation.Start(Guid.CreateVersion7(), subject, starter.Id, recipient.Id, clubId, at);

        db.Conversations.Add(conversation);
        db.DirectMessages.Add(DirectMessage.Write(Guid.CreateVersion7(), conversation.Id, starter.Id, recipient.Id, body, at));
        await db.SaveChangesAsync(CancellationToken.None);

        return conversation.Id;
    }

    private static Task<Guid> AnyClubAsync(Tactical11ManagerDbContext db) =>
        db.Clubs.OrderBy(club => club.Id).Select(club => club.Id).FirstAsync();

    /// <summary>Registers an account and its manager profile, optionally holding a club.</summary>
    private static async Task<Manager> AppointManagerAsync(
        Tactical11ManagerDbContext db,
        string name,
        Guid? holdsClub = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = Guid.CreateVersion7();

        db.Users.Add(User.Register(userId, $"dm-{suffix}@example.com", $"{name} {suffix}"[..24], "hash", "stamp", Now));

        var manager = Manager.Create(Guid.CreateVersion7(), userId, "en-GB", "Europe/London", Now);

        db.Managers.Add(manager);

        if (holdsClub is { } club)
        {
            db.ClubTenures.Add(ClubTenure.Start(Guid.CreateVersion7(), club, manager.Id, $"dm-test-{Guid.NewGuid():N}", Now));
        }

        await db.SaveChangesAsync(CancellationToken.None);

        return manager;
    }
}
