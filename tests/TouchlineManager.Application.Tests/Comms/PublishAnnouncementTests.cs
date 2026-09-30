using FluentAssertions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Comms;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.Tests.Comms;

/// <summary>
/// The operator's announcement, published as a scoped news item (master plan §10.8, `F-46`, ADR-0045).
/// </summary>
public sealed class PublishAnnouncementTests
{
    private static readonly Guid OperatorId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_world_scoped_announcement_is_published_to_the_feed_and_audited()
    {
        var news = new RecordingNewsRepository();
        var audit = new RecordingAuditWriter();
        var world = GameWorld.Create(Guid.CreateVersion7(), "World", Now);

        var result = await Create(world, news, audit).ExecuteAsync(
            "Scheduled maintenance",
            "The game will be read-only tonight from 22:00 to 22:30 UTC.",
            countryId: null,
            divisionId: null,
            expiresAt: null,
            "a maintenance window",
            "announcement-1",
            CancellationToken.None);

        result.Outcome.Should().Be(PublishAnnouncementOutcome.Published);
        result.NewsItemId.Should().NotBeNull();
        result.PublishedAt.Should().Be(Now);

        var item = news.Items.Should().ContainSingle().Subject;

        item.Category.Should().Be(NewsCategory.Announcement);
        item.TemplateKey.Should().Be(NewsTemplates.AnnouncementPublished);
        item.CountryId.Should().BeNull();
        item.DivisionId.Should().BeNull();

        var text = NewsMessageText.Render(item.TemplateKey, item.ParametersJson);

        text.Title.Should().Be("Scheduled maintenance");
        text.Body.Should().Be("The game will be read-only tonight from 22:00 to 22:30 UTC.");

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(AdminAuditActions.AnnouncementPublished);
        audit.Entries[0].ActorUserId.Should().Be(OperatorId);
        audit.Entries[0].TargetType.Should().Be(AuditTargetTypes.NewsItem);
        audit.Entries[0].TargetId.Should().Be(item.Id);
        audit.Entries[0].CorrelationId.Should().Be("announcement-1");
        audit.Entries[0].Reason.Should().Be("a maintenance window");
    }

    [Fact]
    public async Task An_unknown_scope_is_refused()
    {
        var news = new RecordingNewsRepository();
        var audit = new RecordingAuditWriter();
        var world = GameWorld.Create(Guid.CreateVersion7(), "World", Now);

        var result = await Create(world, news, audit).ExecuteAsync(
            "Notice",
            "Body",
            countryId: Guid.CreateVersion7(),
            divisionId: null,
            expiresAt: null,
            "scoped to nowhere",
            "announcement-2",
            CancellationToken.None);

        result.Outcome.Should().Be(PublishAnnouncementOutcome.ScopeNotFound);
        news.Items.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_world_is_reported()
    {
        var result = await Create(null, new RecordingNewsRepository(), new RecordingAuditWriter()).ExecuteAsync(
            "Notice",
            "Body",
            countryId: null,
            divisionId: null,
            expiresAt: null,
            "no world",
            "announcement-3",
            CancellationToken.None);

        result.Outcome.Should().Be(PublishAnnouncementOutcome.WorldNotSeeded);
    }

    [Theory]
    [InlineData("", "Body")]
    [InlineData("   ", "Body")]
    [InlineData("Title", "")]
    [InlineData("Title", "   ")]
    public async Task A_blank_title_or_body_is_refused(string title, string body)
    {
        var world = GameWorld.Create(Guid.CreateVersion7(), "World", Now);

        var result = await Create(world, new RecordingNewsRepository(), new RecordingAuditWriter()).ExecuteAsync(
            title,
            body,
            countryId: null,
            divisionId: null,
            expiresAt: null,
            "not a notice",
            "announcement-4",
            CancellationToken.None);

        result.Outcome.Should().Be(PublishAnnouncementOutcome.InvalidAnnouncement);
    }

    [Fact]
    public async Task An_expiry_before_now_is_refused()
    {
        var world = GameWorld.Create(Guid.CreateVersion7(), "World", Now);

        var result = await Create(world, new RecordingNewsRepository(), new RecordingAuditWriter()).ExecuteAsync(
            "Notice",
            "Body",
            countryId: null,
            divisionId: null,
            expiresAt: Now.AddMinutes(-1),
            "already expired",
            "announcement-5",
            CancellationToken.None);

        result.Outcome.Should().Be(PublishAnnouncementOutcome.InvalidAnnouncement);
    }

    [Fact]
    public async Task A_blank_reason_is_refused()
    {
        var world = GameWorld.Create(Guid.CreateVersion7(), "World", Now);

        var act = async () => await Create(world, new RecordingNewsRepository(), new RecordingAuditWriter())
            .ExecuteAsync("Notice", "Body", null, null, null, " ", "announcement-6", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static PublishAnnouncement Create(
        GameWorld? world,
        RecordingNewsRepository news,
        RecordingAuditWriter audit) =>
        new(
            new FixedClock(),
            new StubWorldRepository { World = world },
            news,
            audit,
            new StubSecureTokens(),
            new StubRequestContext(),
            new RecordingUnitOfWork());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingNewsRepository : INewsRepository
    {
        public List<NewsItem> Items { get; } = [];

        public void Add(NewsItem item) => Items.Add(item);

        public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsPageQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class StubSecureTokens : ISecureTokenService
    {
        public string CreateToken() => throw new NotSupportedException();

        public string CreateSecurityStamp() => throw new NotSupportedException();

        public byte[] CreateRandomBytes(int length) => throw new NotSupportedException();

        public string HashToken(string token) => throw new NotSupportedException();

        public string? HashClientValue(string? value) => value is null ? null : "hashed";
    }

    private sealed class StubRequestContext : IRequestContext
    {
        public Guid? ActorUserId { get; } = OperatorId;

        public string CorrelationId { get; } = Guid.CreateVersion7().ToString();

        public string? IpAddress => null;

        public string? UserAgent => null;
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

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

    /// <summary>A world repository that knows only the world, and refuses every other shape.</summary>
    private sealed class StubWorldRepository : IWorldRepository
    {
        public GameWorld? World { get; init; }

        public Task<GameWorld?> FindWorldAsync(CancellationToken cancellationToken) => Task.FromResult(World);

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

        public Task<Division?> FindDivisionByTierAsync(
            Guid countryId,
            int tierNumber,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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
}
