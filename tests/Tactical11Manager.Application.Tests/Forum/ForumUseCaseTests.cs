using FluentAssertions;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Forum;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Forum;
using Tactical11Manager.Contracts.Forum;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Forum;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Forum;

public sealed class ForumUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _countryId = Guid.NewGuid();

    private readonly FakeForumRepository _forum = new();
    private readonly StubWorldRepository _world = new();
    private readonly RecordingUnitOfWork _unitOfWork = new();
    private readonly FakeClock _clock = new(Now);

    public ForumUseCaseTests()
    {
        _forum.ManagerContext = ("Alex Ferguson", "Manchester Red", _countryId);
    }

    [Fact]
    public async Task Regular_user_can_create_thread_in_international_general()
    {
        var command = new CreateForumThread(_forum, _unitOfWork, _clock);
        var request = new CreateForumThreadRequest("Best 4-3-3 Tactics", "What wingers work best for inverted runs?");

        var result = await command.ExecuteAsync(
            ForumConstants.SubforumInternationalGeneral,
            _managerId,
            isOperator: false,
            request,
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.Success);
        result.ThreadId.Should().NotBeNull();
        _forum.Threads.Should().ContainSingle(t =>
            t.SubforumId == ForumConstants.SubforumInternationalGeneral
            && t.Title == "Best 4-3-3 Tactics"
            && t.AuthorManagerName == "Alex Ferguson"
            && !t.IsAnnouncement);
        _forum.Posts.Should().ContainSingle(p =>
            p.PostNumber == 1
            && p.Body == "What wingers work best for inverted runs?");
        _unitOfWork.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Regular_user_cannot_create_thread_in_announcements()
    {
        var command = new CreateForumThread(_forum, _unitOfWork, _clock);
        var request = new CreateForumThreadRequest("My Announcement", "This should be forbidden");

        var result = await command.ExecuteAsync(
            ForumConstants.SubforumInternationalAnnouncements,
            _managerId,
            isOperator: false,
            request,
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.AnnouncementsRestricted);
        result.ErrorCode.Should().Be(ForumErrorCodes.AnnouncementsRestricted);
        _forum.Threads.Should().BeEmpty();
    }

    [Fact]
    public async Task Operator_can_create_thread_in_announcements()
    {
        var command = new CreateForumThread(_forum, _unitOfWork, _clock);
        var request = new CreateForumThreadRequest("Official Season Kick-off", "Season 1 is now underway with all fixtures active.");

        var result = await command.ExecuteAsync(
            ForumConstants.SubforumInternationalAnnouncements,
            _managerId,
            isOperator: true,
            request,
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.Success);
        result.ThreadId.Should().NotBeNull();
        _forum.Threads.Should().ContainSingle(t =>
            t.SubforumId == ForumConstants.SubforumInternationalAnnouncements
            && t.IsAnnouncement
            && t.IsPinned);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")] // Too short (< 3)
    public async Task Thread_creation_rejects_invalid_title(string title)
    {
        var command = new CreateForumThread(_forum, _unitOfWork, _clock);
        var request = new CreateForumThreadRequest(title, "Valid body content here.");

        var result = await command.ExecuteAsync(
            ForumConstants.SubforumInternationalGeneral,
            _managerId,
            isOperator: false,
            request,
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.InvalidTitle);
        result.ErrorCode.Should().Be(ForumErrorCodes.TitleLength);
    }

    [Fact]
    public async Task Thread_creation_rejects_invalid_body()
    {
        var command = new CreateForumThread(_forum, _unitOfWork, _clock);
        var request = new CreateForumThreadRequest("Valid Title Here", "ab"); // Too short

        var result = await command.ExecuteAsync(
            ForumConstants.SubforumInternationalGeneral,
            _managerId,
            isOperator: false,
            request,
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.InvalidBody);
        result.ErrorCode.Should().Be(ForumErrorCodes.BodyLength);
    }

    [Fact]
    public async Task User_can_reply_to_existing_thread()
    {
        var threadId = Guid.NewGuid();
        var thread = ForumThread.Create(
            threadId,
            ForumConstants.SubforumInternationalGeneral,
            countryId: null,
            "Tactics Discussion",
            _managerId,
            "Alex Ferguson",
            "Manchester Red",
            Now.AddHours(-2));

        _forum.Threads.Add(thread);

        var command = new CreateForumPost(_forum, _unitOfWork, _clock);
        var request = new CreateForumPostRequest("I agree! Playing with an inside forward works wonders.");

        var result = await command.ExecuteAsync(
            threadId,
            _managerId,
            isOperator: false,
            request,
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.Success);
        result.Post.Should().NotBeNull();
        result.Post!.PostNumber.Should().Be(2);
        thread.ReplyCount.Should().Be(1);
        thread.LatestReplyAuthorName.Should().Be("Alex Ferguson");

        _forum.Posts.Should().ContainSingle(p =>
            p.ThreadId == threadId
            && p.PostNumber == 2
            && p.Body.Contains("inside forward"));
        _unitOfWork.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Overview_returns_international_subforums_and_country_general()
    {
        var overviewQuery = new GetForumOverview(_forum, _world, _clock);
        var result = await overviewQuery.ExecuteAsync(_managerId, CancellationToken.None);

        result.Sections.Should().HaveCount(2);

        var intSection = result.Sections[0];
        intSection.Id.Should().Be(ForumConstants.SectionInternational);
        intSection.LanguageNotice.Should().Be("All posts in the International Forum must be in English.");
        intSection.Subforums.Should().HaveCount(3);
        intSection.Subforums.Select(s => s.Name).Should().Contain(["General", "Development and Bugs", "Announcements"]);
        intSection.Subforums.Single(s => s.Name == "Announcements").CanCreateThreads.Should().BeFalse();
        intSection.Subforums.Single(s => s.Name == "General").CanCreateThreads.Should().BeTrue();
        intSection.Subforums.Single(s => s.Name == "Development and Bugs").CanCreateThreads.Should().BeTrue();

        var countrySection = result.Sections[1];
        countrySection.Id.Should().Be(ForumConstants.SectionCountry);
        countrySection.Subforums.Should().HaveCount(1);
        countrySection.Subforums[0].Name.Should().Be("General");
        countrySection.Subforums[0].CanCreateThreads.Should().BeTrue();
    }

    [Fact]
    public async Task Overview_without_a_club_lists_the_international_forum_alone()
    {
        _forum.ManagerContext = ("Alex Ferguson", null, null);

        var result = await new GetForumOverview(_forum, _world, _clock).ExecuteAsync(_managerId, CancellationToken.None);

        result.Sections.Should().ContainSingle().Which.Id.Should().Be(ForumConstants.SectionInternational);
    }

    [Fact]
    public async Task Overview_counts_threads_and_posts_per_subforum()
    {
        var thread = AddThread(ForumConstants.SubforumInternationalGeneral, countryId: null);
        AddPost(thread, postNumber: 1);
        AddPost(thread, postNumber: 2);

        var result = await new GetForumOverview(_forum, _world, _clock).ExecuteAsync(_managerId, CancellationToken.None);

        var general = result.Sections[0].Subforums.Single(s => s.Id == ForumConstants.SubforumInternationalGeneral);
        general.ThreadCount.Should().Be(1);
        general.PostCount.Should().Be(2);
        general.LatestActivityAt.Should().Be(thread.LastActivityAt);
        result.Sections[0].Subforums.Single(s => s.Id == ForumConstants.SubforumInternationalDevBugs).ThreadCount.Should().Be(0);
    }

    [Fact]
    public async Task Country_thread_takes_the_country_of_the_managers_club()
    {
        var result = await new CreateForumThread(_forum, _unitOfWork, _clock).ExecuteAsync(
            ForumConstants.SubforumCountryGeneral,
            _managerId,
            isOperator: false,
            new CreateForumThreadRequest("Derby day", "Who is going?"),
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.Success);
        _forum.Threads.Should().ContainSingle(t => t.CountryId == _countryId);
    }

    [Fact]
    public async Task Manager_without_a_club_cannot_start_a_country_thread()
    {
        _forum.ManagerContext = ("Alex Ferguson", null, null);

        var result = await new CreateForumThread(_forum, _unitOfWork, _clock).ExecuteAsync(
            ForumConstants.SubforumCountryGeneral,
            _managerId,
            isOperator: false,
            new CreateForumThreadRequest("Derby day", "Who is going?"),
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.CountryMismatch);
        _forum.Threads.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_subforum_is_refused()
    {
        var result = await new CreateForumThread(_forum, _unitOfWork, _clock).ExecuteAsync(
            "no-such-forum",
            _managerId,
            isOperator: false,
            new CreateForumThreadRequest("Hello there", "Anybody home?"),
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.SubforumNotFound);
    }

    [Fact]
    public async Task Reply_to_another_countrys_thread_is_refused()
    {
        var thread = AddThread(ForumConstants.SubforumCountryGeneral, countryId: Guid.NewGuid());

        var result = await new CreateForumPost(_forum, _unitOfWork, _clock).ExecuteAsync(
            thread.Id,
            _managerId,
            isOperator: false,
            new CreateForumPostRequest("Hello from abroad"),
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.CountryMismatch);
        thread.ReplyCount.Should().Be(0);
        _forum.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task Reply_to_a_missing_thread_is_refused()
    {
        var result = await new CreateForumPost(_forum, _unitOfWork, _clock).ExecuteAsync(
            Guid.NewGuid(),
            _managerId,
            isOperator: false,
            new CreateForumPostRequest("Anybody there?"),
            CancellationToken.None);

        result.Outcome.Should().Be(ForumOutcome.ThreadNotFound);
    }

    [Fact]
    public async Task Staff_reply_is_marked_as_staff()
    {
        var thread = AddThread(ForumConstants.SubforumInternationalAnnouncements, countryId: null);

        var result = await new CreateForumPost(_forum, _unitOfWork, _clock).ExecuteAsync(
            thread.Id,
            _managerId,
            isOperator: true,
            new CreateForumPostRequest("Noted."),
            CancellationToken.None);

        result.Post!.IsStaff.Should().BeTrue();
    }

    [Fact]
    public async Task Thread_list_puts_pinned_threads_first_and_keeps_the_english_notice()
    {
        var older = AddThread(ForumConstants.SubforumInternationalAnnouncements, countryId: null, pinned: true);
        var newer = AddThread(ForumConstants.SubforumInternationalAnnouncements, countryId: null);

        var list = await new GetSubforumThreads(_forum, _world).ExecuteAsync(
            ForumConstants.SubforumInternationalAnnouncements,
            _managerId,
            CancellationToken.None);

        list.Should().NotBeNull();
        list!.Threads.Select(t => t.Id).Should().Equal(older.Id, newer.Id);
        list.CanCreateThreads.Should().BeFalse();
        list.LanguageNotice.Should().Be(ForumConstants.InternationalLanguageNotice);
    }

    [Fact]
    public async Task Country_thread_list_shows_only_the_managers_country_and_has_no_language_notice()
    {
        var own = AddThread(ForumConstants.SubforumCountryGeneral, _countryId);
        AddThread(ForumConstants.SubforumCountryGeneral, Guid.NewGuid());

        var list = await new GetSubforumThreads(_forum, _world).ExecuteAsync(
            ForumConstants.SubforumCountryGeneral,
            _managerId,
            CancellationToken.None);

        list!.Threads.Should().ContainSingle().Which.Id.Should().Be(own.Id);
        list.LanguageNotice.Should().BeNull();
        list.CanCreateThreads.Should().BeTrue();
    }

    [Fact]
    public async Task Thread_list_for_an_unknown_subforum_or_a_manager_without_a_country_is_null()
    {
        var query = new GetSubforumThreads(_forum, _world);

        (await query.ExecuteAsync("no-such-forum", _managerId, CancellationToken.None)).Should().BeNull();

        _forum.ManagerContext = ("Alex Ferguson", null, null);
        (await query.ExecuteAsync(ForumConstants.SubforumCountryGeneral, _managerId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Thread_detail_of_another_countrys_thread_is_not_shown()
    {
        var other = AddThread(ForumConstants.SubforumCountryGeneral, Guid.NewGuid());
        var own = AddThread(ForumConstants.SubforumCountryGeneral, _countryId);
        AddPost(own, postNumber: 1);

        var query = new GetForumThreadDetail(_forum, _world);

        (await query.ExecuteAsync(other.Id, _managerId, CancellationToken.None)).Should().BeNull();

        var detail = await query.ExecuteAsync(own.Id, _managerId, CancellationToken.None);
        detail.Should().NotBeNull();
        detail!.Posts.Should().ContainSingle();
        detail.LanguageNotice.Should().BeNull();
    }

    [Fact]
    public async Task International_thread_detail_carries_the_english_notice_and_can_be_replied_to()
    {
        var thread = AddThread(ForumConstants.SubforumInternationalAnnouncements, countryId: null);

        var detail = await new GetForumThreadDetail(_forum, _world).ExecuteAsync(thread.Id, _managerId, CancellationToken.None);

        detail!.LanguageNotice.Should().Be(ForumConstants.InternationalLanguageNotice);
        detail.SubforumName.Should().Be("Announcements");
        detail.CanReply.Should().BeTrue();
    }

    private ForumThread AddThread(string subforumId, Guid? countryId, bool pinned = false)
    {
        var id = Guid.NewGuid();
        var thread = pinned
            ? ForumThread.CreateAnnouncement(id, subforumId, countryId, "A thread", _managerId, "Alex Ferguson", "Manchester Red", isPinned: true, Now.AddHours(-1))
            : ForumThread.Create(id, subforumId, countryId, "A thread", _managerId, "Alex Ferguson", "Manchester Red", Now);
        _forum.Threads.Add(thread);

        return thread;
    }

    private void AddPost(ForumThread thread, int postNumber) =>
        _forum.Posts.Add(ForumPost.Create(
            Guid.NewGuid(), thread.Id, postNumber, _managerId, "Alex Ferguson", "Manchester Red", isStaff: false, "Some words", Now));

    private sealed class FakeForumRepository : IForumRepository
    {
        public List<ForumThread> Threads { get; } = [];
        public List<ForumPost> Posts { get; } = [];
        public (string ManagerName, string? ClubName, Guid? CountryId)? ManagerContext { get; set; }

        public Task<IReadOnlyList<ForumThread>> GetThreadsBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken)
        {
            var res = Threads
                .Where(t => t.SubforumId == subforumId && (!countryId.HasValue || t.CountryId == countryId.Value))
                .OrderByDescending(t => t.IsPinned)
                .ThenByDescending(t => t.LastActivityAt)
                .ToList();
            return Task.FromResult<IReadOnlyList<ForumThread>>(res);
        }

        public Task<ForumThread?> GetThreadByIdAsync(Guid threadId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Threads.SingleOrDefault(t => t.Id == threadId));
        }

        public Task<IReadOnlyList<ForumPost>> GetPostsByThreadIdAsync(Guid threadId, CancellationToken cancellationToken)
        {
            var res = Posts.Where(p => p.ThreadId == threadId).OrderBy(p => p.PostNumber).ToList();
            return Task.FromResult<IReadOnlyList<ForumPost>>(res);
        }

        public Task<int> GetThreadCountBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Threads.Count(t => t.SubforumId == subforumId && (!countryId.HasValue || t.CountryId == countryId.Value)));
        }

        public Task<int> GetPostCountBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken)
        {
            var tIds = Threads.Where(t => t.SubforumId == subforumId && (!countryId.HasValue || t.CountryId == countryId.Value)).Select(t => t.Id).ToHashSet();
            return Task.FromResult(Posts.Count(p => tIds.Contains(p.ThreadId)));
        }

        public Task<DateTimeOffset?> GetLatestActivityBySubforumAsync(string subforumId, Guid? countryId, CancellationToken cancellationToken)
        {
            var latest = Threads
                .Where(t => t.SubforumId == subforumId && (!countryId.HasValue || t.CountryId == countryId.Value))
                .OrderByDescending(t => t.LastActivityAt)
                .Select(t => (DateTimeOffset?)t.LastActivityAt)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }

        public Task<(string ManagerName, string? ClubName, Guid? CountryId)?> GetManagerContextAsync(Guid managerId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ManagerContext);
        }

        public void AddThread(ForumThread thread) => Threads.Add(thread);
        public void AddPost(ForumPost post) => Posts.Add(post);
    }

    private sealed class StubWorldRepository : IWorldRepository
    {
        public Task<GameWorld?> FindWorldAsync(CancellationToken cancellationToken) => Task.FromResult<GameWorld?>(null);

        public Task<Country?> FindCountryAsync(Guid countryId, CancellationToken cancellationToken) =>
            Task.FromResult<Country?>(null);

        public Task<Division?> FindDivisionAsync(Guid divisionId, CancellationToken cancellationToken) =>
            Task.FromResult<Division?>(null);

        public void AddWorld(GameWorld world) => throw new NotSupportedException();

        public Task<Season?> FindSeasonAsync(Guid worldId, int sequenceNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Season?> FindSeasonByIdAsync(Guid seasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddSeason(Season season) => throw new NotSupportedException();

        public Task<IReadOnlyList<Country>> ListCountriesAsync(Guid worldId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddCountry(Country country) => throw new NotSupportedException();

        public Task<Division?> FindLowestActiveDivisionAsync(
            Guid countryId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Division?> FindDivisionByLeagueAsync(
            Guid countryId,
            int tierNumber,
            int leagueNumber,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Division>> ListDivisionsAsync(Guid countryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddDivision(Division division) => throw new NotSupportedException();

        public Task<DivisionSeason?> FindDivisionSeasonAsync(
            Guid divisionId,
            Guid seasonId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddDivisionSeason(DivisionSeason divisionSeason) => throw new NotSupportedException();

        public void AddClubSeasonEntry(ClubSeasonEntry entry) => throw new NotSupportedException();

        public Task<IReadOnlyList<Division>> ListActiveDivisionsAsync(
            Guid countryId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DivisionSeason>> ListDivisionSeasonsAsync(
            Guid seasonId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ClubSeasonEntry>> ListClubSeasonEntriesAsync(
            Guid divisionSeasonId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync(
            TransactionIsolation isolation,
            CancellationToken cancellationToken) =>
            Task.FromResult<IDatabaseTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IDatabaseTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
