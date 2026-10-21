using FluentAssertions;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Comms;

/// <summary>
/// Direct messages between managers: every refusal the use cases give, and that a block works in both
/// directions (`COM-10` to `COM-16`).
/// </summary>
public sealed class DirectMessageUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_conversation_is_started_from_a_club_page_and_addressed_to_its_manager()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");

        var result = await world.Start().ExecuteAsync(a.UserId, bClub, "  Friendly?  ", "Fancy a game\r\nsometime?", default);

        result.Outcome.Should().Be(DirectMessageOutcome.Ok, "COM-11");
        result.Sent.Should().NotBeNull();

        var conversation = world.Messages.Conversations.Should().ContainSingle().Subject;

        conversation.StartedByManagerId.Should().Be(a.Id);
        conversation.RecipientManagerId.Should().Be(b.Id, "the recipient is whoever holds the club's tenure, not named by the request (COM-11)");
        conversation.StartedFromClubId.Should().Be(bClub);
        conversation.Subject.Should().Be("Friendly?", "the subject is trimmed (COM-13)");

        var message = world.Messages.Sent.Should().ContainSingle().Subject;

        message.RecipientManagerId.Should().Be(b.Id);
        message.IsRead.Should().BeFalse("COM-16");
        message.Body.Should().Be("Fancy a game\nsometime?", "a line break is kept as one (COM-13)");
        world.Saves.Should().Be(1, "a start commits as one unit of work");
    }

    [Theory]
    [InlineData("", "Hello")]
    [InlineData("   ", "Hello")]
    [InlineData("Hi", "")]
    [InlineData("Hi", " \n ")]
    [InlineData("Hi", null)]
    public async Task A_missing_subject_or_message_is_refused_before_anything_is_looked_up(string? subject, string? body)
    {
        var world = new World();
        var (_, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");

        var result = await world.Start().ExecuteAsync(a.UserId, bClub, subject, body, default);

        result.Outcome.Should().Be(DirectMessageOutcome.InvalidText, "COM-13");
        world.Messages.Conversations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_subject_or_message_over_the_limit_is_refused()
    {
        var world = new World();
        var (_, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");

        var longSubject = await world.Start().ExecuteAsync(
            a.UserId,
            bClub,
            new string('s', DirectMessageRules.SubjectMaxChars + 1),
            "Hello",
            default);
        var longBody = await world.Start().ExecuteAsync(
            a.UserId,
            bClub,
            "Hi",
            new string('b', DirectMessageRules.BodyMaxChars + 1),
            default);
        var atTheLimit = await world.Start().ExecuteAsync(
            a.UserId,
            bClub,
            new string('s', DirectMessageRules.SubjectMaxChars),
            new string('b', DirectMessageRules.BodyMaxChars),
            default);

        longSubject.Outcome.Should().Be(DirectMessageOutcome.InvalidText, "COM-13");
        longBody.Outcome.Should().Be(DirectMessageOutcome.InvalidText, "COM-13");
        atTheLimit.Outcome.Should().Be(DirectMessageOutcome.Ok, "the limits are inclusive (COM-13)");
    }

    [Fact]
    public async Task An_account_with_no_manager_profile_cannot_start_a_conversation()
    {
        var world = new World();
        var (_, bClub) = world.AddHumanClub("Bea");

        var result = await world.Start().ExecuteAsync(Guid.CreateVersion7(), bClub, "Hi", "Hello", default);

        result.Outcome.Should().Be(DirectMessageOutcome.NoManagerProfile);
    }

    [Fact]
    public async Task A_manager_with_no_club_cannot_start_a_conversation()
    {
        var world = new World();
        var (_, bClub) = world.AddHumanClub("Bea");
        var jobless = world.AddManager("Jo");

        var result = await world.Start().ExecuteAsync(jobless.UserId, bClub, "Hi", "Hello", default);

        result.Outcome.Should().Be(DirectMessageOutcome.SenderHasNoClub, "a conversation starts from a club the sender holds (COM-11)");
    }

    [Fact]
    public async Task A_club_that_does_not_exist_is_refused()
    {
        var world = new World();
        var (a, _) = world.AddHumanClub("Alf");

        var result = await world.Start().ExecuteAsync(a.UserId, Guid.CreateVersion7(), "Hi", "Hello", default);

        result.Outcome.Should().Be(DirectMessageOutcome.ClubNotFound);
    }

    [Fact]
    public async Task A_manager_cannot_start_a_conversation_from_their_own_club()
    {
        var world = new World();
        var (a, aClub) = world.AddHumanClub("Alf");

        var result = await world.Start().ExecuteAsync(a.UserId, aClub, "Hi", "Hello", default);

        result.Outcome.Should().Be(DirectMessageOutcome.OwnClub, "COM-11");
    }

    [Fact]
    public async Task A_club_run_by_the_game_has_nobody_to_write_to()
    {
        var world = new World();
        var aiClub = world.AddAiClub();
        var (a, _) = world.AddHumanClub("Alf");

        var result = await world.Start().ExecuteAsync(a.UserId, aiClub, "Hi", "Hello", default);

        result.Outcome.Should().Be(DirectMessageOutcome.ClubHasNoManager, "an AI-run club cannot be messaged (COM-11)");
    }

    [Fact]
    public async Task A_manager_whose_account_is_closing_cannot_be_written_to()
    {
        var world = new World();
        var (_, bClub) = world.AddHumanClub("Bea", accountClosing: true);
        var (a, _) = world.AddHumanClub("Alf");

        var result = await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default);

        result.Outcome.Should().Be(DirectMessageOutcome.RecipientUnavailable, "COM-11");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_block_in_either_direction_stops_a_conversation_from_starting(bool senderBlockedRecipient)
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");

        world.Messages.Blocks.Add(senderBlockedRecipient
            ? ManagerBlock.Create(a.Id, b.Id, Now)
            : ManagerBlock.Create(b.Id, a.Id, Now));

        var result = await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default);

        result.Outcome.Should().Be(DirectMessageOutcome.Blocked, "a block stops a start whichever side placed it (COM-15)");
        world.Messages.Conversations.Should().BeEmpty();
    }

    [Fact]
    public async Task Either_side_replies_and_the_thread_moves_up_the_list()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        world.Clock.UtcNow = Now.AddMinutes(5);

        var reply = await world.Reply().ExecuteAsync(b.UserId, conversationId, " Sure ", default);

        reply.Outcome.Should().Be(DirectMessageOutcome.Ok, "COM-12");

        var conversation = world.Messages.Conversations.Single();

        conversation.LastMessageAt.Should().Be(Now.AddMinutes(5), "a reply bumps the thread (COM-10)");
        world.Messages.Sent.Should().HaveCount(2);
        world.Messages.Sent[1].RecipientManagerId.Should().Be(a.Id);
        world.Messages.Sent[1].Body.Should().Be("Sure");

        var second = await world.Reply().ExecuteAsync(a.UserId, conversationId, "Great", default);

        second.Outcome.Should().Be(DirectMessageOutcome.Ok, "either participant can reply (COM-12)");
        world.Messages.Sent[2].RecipientManagerId.Should().Be(b.Id);
    }

    [Fact]
    public async Task A_reply_needs_no_club()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        world.Tenures.Clear();

        var reply = await world.Reply().ExecuteAsync(b.UserId, conversationId, "Sorry, I left", default);

        reply.Outcome.Should().Be(DirectMessageOutcome.Ok, "a manager who has since left their club still answers (COM-12)");
    }

    [Fact]
    public async Task A_manager_outside_a_conversation_is_told_it_does_not_exist()
    {
        var world = new World();
        var (_, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var (third, _) = world.AddHumanClub("Cy");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        var reply = await world.Reply().ExecuteAsync(third.UserId, conversationId, "Hi both", default);
        var read = await world.Read().ExecuteAsync(third.UserId, conversationId, default);

        reply.Outcome.Should().Be(DirectMessageOutcome.ConversationNotFound, "a guessed identity confirms nothing (I-5)");
        read.Should().Be(DirectMessageOutcome.ConversationNotFound);
        world.Messages.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task A_reply_with_no_text_is_refused()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        var reply = await world.Reply().ExecuteAsync(b.UserId, conversationId, "   ", default);

        reply.Outcome.Should().Be(DirectMessageOutcome.InvalidText, "COM-13");
    }

    [Fact]
    public async Task A_reply_to_a_manager_whose_account_is_closing_is_refused()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        world.ClosingManagers.Add(a.Id);

        var reply = await world.Reply().ExecuteAsync(b.UserId, conversationId, "Hello back", default);

        reply.Outcome.Should().Be(DirectMessageOutcome.RecipientUnavailable, "COM-12");
    }

    [Fact]
    public async Task A_block_stops_replies_in_both_directions_until_it_is_lifted()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        (await world.Block().ExecuteAsync(b.UserId, a.Id, default)).Should().Be(DirectMessageOutcome.Ok);

        (await world.Reply().ExecuteAsync(a.UserId, conversationId, "Please?", default)).Outcome
            .Should().Be(DirectMessageOutcome.Blocked, "the blocked side cannot reply (COM-15)");
        (await world.Reply().ExecuteAsync(b.UserId, conversationId, "No", default)).Outcome
            .Should().Be(DirectMessageOutcome.Blocked, "the blocker cannot reply either (COM-15)");

        (await world.Unblock().ExecuteAsync(b.UserId, a.Id, default)).Should().Be(DirectMessageOutcome.Ok);

        (await world.Reply().ExecuteAsync(a.UserId, conversationId, "Please?", default)).Outcome
            .Should().Be(DirectMessageOutcome.Ok, "after unblocking, sends work (COM-15)");
        (await world.Reply().ExecuteAsync(b.UserId, conversationId, "Fine", default)).Outcome
            .Should().Be(DirectMessageOutcome.Ok);
    }

    [Fact]
    public async Task Unblocking_lifts_only_the_callers_own_block()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        await world.Block().ExecuteAsync(b.UserId, a.Id, default);

        // The blocked manager "unblocks" the blocker: they have no block of their own, so nothing changes.
        (await world.Unblock().ExecuteAsync(a.UserId, b.Id, default)).Should().Be(DirectMessageOutcome.Ok);

        (await world.Reply().ExecuteAsync(a.UserId, conversationId, "Please?", default)).Outcome
            .Should().Be(DirectMessageOutcome.Blocked, "unblocking never opens a door the other side shut (COM-15)");
    }

    [Fact]
    public async Task Blocking_twice_writes_one_block_and_a_stranger_or_oneself_cannot_be_blocked()
    {
        var world = new World();
        var (b, _) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");

        (await world.Block().ExecuteAsync(a.UserId, b.Id, default)).Should().Be(DirectMessageOutcome.Ok);
        (await world.Block().ExecuteAsync(a.UserId, b.Id, default)).Should().Be(DirectMessageOutcome.Ok);

        world.Messages.Blocks.Should().ContainSingle("blocking is idempotent");

        (await world.Block().ExecuteAsync(a.UserId, a.Id, default)).Should().Be(DirectMessageOutcome.ManagerNotFound);
        (await world.Block().ExecuteAsync(a.UserId, Guid.CreateVersion7(), default)).Should().Be(DirectMessageOutcome.ManagerNotFound);
        (await world.Block().ExecuteAsync(Guid.CreateVersion7(), b.Id, default)).Should().Be(DirectMessageOutcome.NoManagerProfile);

        (await world.Unblock().ExecuteAsync(a.UserId, b.Id, default)).Should().Be(DirectMessageOutcome.Ok);
        (await world.Unblock().ExecuteAsync(a.UserId, b.Id, default)).Should().Be(DirectMessageOutcome.Ok, "unblocking is idempotent");

        world.Messages.Blocks.Should().BeEmpty();
    }

    [Fact]
    public async Task Opening_a_conversation_reads_only_what_is_addressed_to_the_caller()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        await world.Reply().ExecuteAsync(b.UserId, conversationId, "Hello back", default);

        world.Clock.UtcNow = Now.AddMinutes(10);

        (await world.Read().ExecuteAsync(b.UserId, conversationId, default)).Should().Be(DirectMessageOutcome.Ok);

        world.Messages.Sent[0].ReadAt.Should().Be(Now.AddMinutes(10), "the message addressed to Bea is read (COM-16)");
        world.Messages.Sent[1].ReadAt.Should().BeNull("Bea's own message is addressed to Alf and stays unread (COM-16)");

        world.Clock.UtcNow = Now.AddMinutes(20);
        var saves = world.Saves;

        (await world.Read().ExecuteAsync(b.UserId, conversationId, default)).Should().Be(DirectMessageOutcome.Ok);

        world.Messages.Sent[0].ReadAt.Should().Be(Now.AddMinutes(10), "reading twice keeps the first read time (COM-16)");
        world.Saves.Should().Be(saves, "nothing changed, so nothing is saved");
    }

    [Fact]
    public async Task A_thread_says_why_a_reply_would_be_refused()
    {
        var world = new World();
        var (b, bClub) = world.AddHumanClub("Bea");
        var (a, _) = world.AddHumanClub("Alf");
        var conversationId = (await world.Start().ExecuteAsync(a.UserId, bClub, "Hi", "Hello", default)).Sent!.ConversationId;

        var open = (await world.ReadThread().ExecuteAsync(a.UserId, conversationId, null, default)).Conversation!;

        open.CanReply.Should().BeTrue();
        open.ReplyBlockedReason.Should().BeNull();
        open.OtherManager.DisplayName.Should().Be("Bea");

        world.Messages.Blocks.Add(ManagerBlock.Create(b.Id, a.Id, Now));

        var blockedByThem = (await world.ReadThread().ExecuteAsync(a.UserId, conversationId, null, default)).Conversation!;

        blockedByThem.CanReply.Should().BeFalse("COM-15");
        blockedByThem.ReplyBlockedReason.Should().Be("This manager is not accepting your messages.");
        blockedByThem.BlockedByMe.Should().BeFalse();

        var blockedByMe = (await world.ReadThread().ExecuteAsync(b.UserId, conversationId, null, default)).Conversation!;

        blockedByMe.CanReply.Should().BeFalse("COM-15");
        blockedByMe.BlockedByMe.Should().BeTrue();
        blockedByMe.ReplyBlockedReason.Should().Contain("You have blocked");

        world.Messages.Blocks.Clear();
        world.ClosingManagers.Add(b.Id);

        var closing = (await world.ReadThread().ExecuteAsync(a.UserId, conversationId, null, default)).Conversation!;

        closing.CanReply.Should().BeFalse("COM-12");
        closing.ReplyBlockedReason.Should().Contain("closing");
    }

    [Fact]
    public async Task A_thread_or_list_with_a_cursor_this_server_did_not_produce_is_refused()
    {
        var world = new World();
        var (a, _) = world.AddHumanClub("Alf");

        (await world.ReadThread().ExecuteAsync(a.UserId, Guid.CreateVersion7(), "not-a-cursor", default)).Outcome
            .Should().Be(DirectMessageOutcome.InvalidCursor);
        (await world.ReadList().ExecuteAsync(a.UserId, "not-a-cursor", default)).Outcome
            .Should().Be(DirectMessageOutcome.InvalidCursor);
    }

    [Fact]
    public async Task A_thread_that_does_not_exist_is_not_found()
    {
        var world = new World();
        var (a, _) = world.AddHumanClub("Alf");

        (await world.ReadThread().ExecuteAsync(a.UserId, Guid.CreateVersion7(), null, default)).Outcome
            .Should().Be(DirectMessageOutcome.ConversationNotFound);
    }

    /// <summary>A small world held in memory: managers, clubs, tenures, and the direct-message store.</summary>
    private sealed class World
    {
        public World()
        {
            Clock = new TestClock { UtcNow = Now };
            Messages = new InMemoryDirectMessages();
            Managers = [];
            Clubs = [];
            Tenures = [];
            Names = [];
            ClosingManagers = [];
        }

        public TestClock Clock { get; }

        public InMemoryDirectMessages Messages { get; }

        public Dictionary<Guid, Manager> Managers { get; }

        public HashSet<Guid> Clubs { get; }

        public List<ClubTenure> Tenures { get; }

        public Dictionary<Guid, string> Names { get; }

        public HashSet<Guid> ClosingManagers { get; }

        public int Saves { get; private set; }

        public Manager AddManager(string name)
        {
            var manager = Manager.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "en-GB", "Europe/London", Now);

            Managers[manager.Id] = manager;
            Names[manager.Id] = name;

            return manager;
        }

        public (Manager Manager, Guid ClubId) AddHumanClub(string name, bool accountClosing = false)
        {
            var manager = AddManager(name);
            var clubId = Guid.CreateVersion7();

            Clubs.Add(clubId);
            Tenures.Add(ClubTenure.Start(Guid.CreateVersion7(), clubId, manager.Id, Guid.CreateVersion7().ToString(), Now));

            if (accountClosing)
            {
                ClosingManagers.Add(manager.Id);
            }

            return (manager, clubId);
        }

        public Guid AddAiClub()
        {
            var clubId = Guid.CreateVersion7();

            Clubs.Add(clubId);

            return clubId;
        }

        public StartConversation Start() => new(
            Messages,
            new FakeManagers(this),
            new FakeTenures(this),
            new FakeClubs(this),
            new Lookup(this),
            new CountingUnitOfWork(this),
            Clock);

        public ReplyToConversation Reply() => new(
            Messages,
            new FakeManagers(this),
            new Lookup(this),
            new CountingUnitOfWork(this),
            Clock);

        public MarkConversationRead Read() => new(
            Messages,
            new FakeManagers(this),
            new CountingUnitOfWork(this),
            Clock);

        public BlockManager Block() => new(Messages, new FakeManagers(this), new CountingUnitOfWork(this), Clock);

        public UnblockManager Unblock() => new(Messages, new FakeManagers(this), new CountingUnitOfWork(this));

        public GetConversation ReadThread() => new(
            new ThreadQueries(this),
            new FakeManagers(this),
            new Lookup(this),
            Clock);

        public GetConversations ReadList() => new(new ThreadQueries(this), new FakeManagers(this), Clock);

        private sealed class CountingUnitOfWork : IUnitOfWork
        {
            private readonly World _world;

            public CountingUnitOfWork(World world) => _world = world;

            public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
            {
                _world.Saves++;

                return Task.FromResult(0);
            }

            public Task<IDatabaseTransaction> BeginTransactionAsync(
                TransactionIsolation isolation,
                CancellationToken cancellationToken) =>
                throw new NotSupportedException("A direct message commits one unit of work.");
        }

        private sealed class FakeManagers : IManagerRepository
        {
            private readonly World _world;

            public FakeManagers(World world) => _world = world;

            public Task<Manager?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
                Task.FromResult(_world.Managers.Values.FirstOrDefault(manager => manager.UserId == userId));

            public Task<Manager?> FindByIdAsync(Guid managerId, CancellationToken cancellationToken) =>
                Task.FromResult(_world.Managers.GetValueOrDefault(managerId));

            public void Add(Manager manager) => throw new NotSupportedException();
        }

        private sealed class FakeTenures : IClubTenureRepository
        {
            private readonly World _world;

            public FakeTenures(World world) => _world = world;

            public Task<ClubTenure?> FindOpenByManagerAsync(Guid managerId, CancellationToken cancellationToken) =>
                Task.FromResult(_world.Tenures.FirstOrDefault(tenure => tenure.ManagerId == managerId));

            public Task<ClubTenure?> FindOpenByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
                Task.FromResult(_world.Tenures.FirstOrDefault(tenure => tenure.ClubId == clubId));

            public Task<IReadOnlyList<ClubTenure>> ListByManagerAsync(Guid managerId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<ClubTenure?> FindByTakeoverKeyAsync(string takeoverIdempotencyKey, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public void Add(ClubTenure tenure) => throw new NotSupportedException();

            public Task<IReadOnlyList<OpenTenureRow>> ListOpenAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<IReadOnlyCollection<Guid>> ListOpenClubIdsAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }

        private sealed class FakeClubs : IClubRepository
        {
            private readonly World _world;

            public FakeClubs(World world) => _world = world;

            public Task<Club?> FindAsync(Guid clubId, CancellationToken cancellationToken) =>
                Task.FromResult(_world.Clubs.Contains(clubId)
                    ? Club.Generate(
                        clubId,
                        Guid.CreateVersion7(),
                        Guid.CreateVersion7(),
                        new ClubIdentity("Test FC", "TST", "Testville", "Testshire", "seed"),
                        tier: 1,
                        foundingGameYear: 1900,
                        Now)
                    : null);

            public Task<IReadOnlyList<Club>> LoadAsync(IReadOnlyCollection<Guid> clubIds, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public void Add(Club club) => throw new NotSupportedException();
        }

        private sealed class Lookup : IClubManagerLookup
        {
            private readonly World _world;

            public Lookup(World world) => _world = world;

            public Task<ClubManagerRow?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken)
            {
                var tenure = _world.Tenures.FirstOrDefault(candidate => candidate.ClubId == clubId);

                return Task.FromResult(tenure is null
                    ? null
                    : new ClubManagerRow(
                        tenure.ManagerId,
                        _world.Names[tenure.ManagerId],
                        _world.ClosingManagers.Contains(tenure.ManagerId)));
            }

            public Task<bool> IsAccountClosingAsync(Guid managerId, CancellationToken cancellationToken) =>
                Task.FromResult(_world.ClosingManagers.Contains(managerId));
        }

        /// <summary>Serves the thread page and the list from the in-memory store.</summary>
        private sealed class ThreadQueries : IDirectMessageQueries
        {
            private readonly World _world;

            public ThreadQueries(World world) => _world = world;

            public Task<ConversationPage> GetConversationsAsync(
                Guid managerId,
                DirectMessageCursorPosition? before,
                CancellationToken cancellationToken) =>
                Task.FromResult(new ConversationPage([], HasMore: false));

            public Task<ConversationThreadPage?> GetThreadAsync(
                Guid managerId,
                Guid conversationId,
                DirectMessageCursorPosition? before,
                CancellationToken cancellationToken)
            {
                var conversation = _world.Messages.Conversations.FirstOrDefault(
                    candidate => candidate.Id == conversationId && candidate.IsParticipant(managerId));

                if (conversation is null)
                {
                    return Task.FromResult<ConversationThreadPage?>(null);
                }

                var otherId = conversation.OtherParticipant(managerId);

                return Task.FromResult<ConversationThreadPage?>(new ConversationThreadPage(
                    conversation.Id,
                    conversation.Subject,
                    new DirectMessageParticipantRow(otherId, _world.Names[otherId], null, null),
                    [.. _world.Messages.Sent
                        .Where(message => message.ConversationId == conversationId)
                        .OrderByDescending(message => message.SentAt)
                        .Select(message => new DirectMessageRow(
                            message.Id,
                            message.SenderManagerId == managerId,
                            message.Body,
                            message.SentAt,
                            message.ReadAt))],
                    HasMore: false,
                    BlockedByMe: _world.Messages.Blocks.Any(
                        block => block.BlockerManagerId == managerId && block.BlockedManagerId == otherId),
                    BlockedMe: _world.Messages.Blocks.Any(
                        block => block.BlockerManagerId == otherId && block.BlockedManagerId == managerId)));
            }

            public Task<int> CountUnreadAsync(Guid managerId, CancellationToken cancellationToken) =>
                Task.FromResult(_world.Messages.Sent.Count(
                    message => message.RecipientManagerId == managerId && !message.IsRead));
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class InMemoryDirectMessages : IDirectMessageRepository
    {
        public List<Conversation> Conversations { get; } = [];

        public List<DirectMessage> Sent { get; } = [];

        public List<ManagerBlock> Blocks { get; } = [];

        public void Add(Conversation conversation) => Conversations.Add(conversation);

        public void Add(DirectMessage message) => Sent.Add(message);

        public void Add(ManagerBlock block) => Blocks.Add(block);

        public void Remove(ManagerBlock block) => Blocks.Remove(block);

        public Task<Conversation?> FindConversationAsync(
            Guid managerId,
            Guid conversationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Conversations.FirstOrDefault(
                conversation => conversation.Id == conversationId && conversation.IsParticipant(managerId)));

        public Task<IReadOnlyList<DirectMessage>> LoadUnreadAsync(
            Guid managerId,
            Guid conversationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DirectMessage>>(
                [.. Sent.Where(message => message.ConversationId == conversationId
                    && message.RecipientManagerId == managerId
                    && !message.IsRead)]);

        public Task<ManagerBlock?> FindBlockAsync(
            Guid blockerManagerId,
            Guid blockedManagerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Blocks.FirstOrDefault(
                block => block.BlockerManagerId == blockerManagerId && block.BlockedManagerId == blockedManagerId));

        public Task<bool> IsBlockedBetweenAsync(
            Guid firstManagerId,
            Guid secondManagerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Blocks.Any(
                block => (block.BlockerManagerId == firstManagerId && block.BlockedManagerId == secondManagerId)
                    || (block.BlockerManagerId == secondManagerId && block.BlockedManagerId == firstManagerId)));
    }
}
