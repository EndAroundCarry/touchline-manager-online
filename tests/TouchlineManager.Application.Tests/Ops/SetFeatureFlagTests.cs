using FluentAssertions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Ops;

namespace TouchlineManager.Application.Tests.Ops;

/// <summary>
/// The operator's feature-flag command: a world-scoped switch whose value is an opaque JSON document
/// (master plan §10.8, §13, `F-46`, ADR-0045).
/// </summary>
public sealed class SetFeatureFlagTests
{
    private static readonly Guid OperatorId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_new_flag_is_created_and_audited()
    {
        var store = new RecordingFeatureFlagStore();
        var audit = new RecordingAuditWriter();
        var readOnly = new RecordingReadOnlyMode();

        var result = await Create(store, audit, readOnly).ExecuteAsync(
            "market.auctions_enabled",
            "false",
            rolloutMetadataJson: null,
            "pausing auctions for a data repair",
            "flag-1",
            CancellationToken.None);

        result.Outcome.Should().Be(SetFeatureFlagOutcome.Created);
        result.Flag.Should().NotBeNull();
        result.Flag!.Scope.Should().Be(SetFeatureFlag.WorldScope);
        result.Flag.Key.Should().Be("market.auctions_enabled");
        result.Flag.ValueJson.Should().Be("false");
        result.Flag.Version.Should().Be(1);

        store.Writes.Should().ContainSingle().Which.ValueJson.Should().Be("false");

        readOnly.Invalidations.Should().Be(1, "the reader applies the change at once (F-51)");

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(AdminAuditActions.FeatureFlagSet);
        audit.Entries[0].ActorType.Should().Be(AuditActorTypes.User);
        audit.Entries[0].ActorUserId.Should().Be(OperatorId);
        audit.Entries[0].TargetType.Should().Be(AuditTargetTypes.FeatureFlag);
        audit.Entries[0].TargetId.Should().Be(result.Flag.Id);
        audit.Entries[0].CorrelationId.Should().Be("flag-1");
        audit.Entries[0].Reason.Should().Be("pausing auctions for a data repair");
    }

    [Fact]
    public async Task An_existing_flag_is_updated_and_its_version_increments()
    {
        var existing = new FeatureFlagSnapshot(
            Guid.CreateVersion7(),
            SetFeatureFlag.WorldScope,
            "market.auctions_enabled",
            "true",
            null,
            Version: 3);

        var store = new RecordingFeatureFlagStore { Existing = existing };
        var audit = new RecordingAuditWriter();

        var result = await Create(store, audit).ExecuteAsync(
            "market.auctions_enabled",
            "false",
            rolloutMetadataJson: """{"reason":"incident"}""",
            "turning auctions off",
            "flag-2",
            CancellationToken.None);

        result.Outcome.Should().Be(SetFeatureFlagOutcome.Updated);
        result.Flag!.Version.Should().Be(4, "the row's version increments on a change");
        store.Writes.Should().ContainSingle();
        audit.Entries.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Market.Auctions")]
    [InlineData(" leading-space")]
    [InlineData("has space")]
    [InlineData("")]
    public async Task An_invalid_key_is_refused(string key)
    {
        var store = new RecordingFeatureFlagStore();
        var audit = new RecordingAuditWriter();

        var result = await Create(store, audit).ExecuteAsync(
            key,
            "true",
            null,
            "not a valid key",
            "flag-3",
            CancellationToken.None);

        result.Outcome.Should().Be(SetFeatureFlagOutcome.InvalidFlag);
        store.Writes.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_value_that_is_not_json_is_refused()
    {
        var store = new RecordingFeatureFlagStore();
        var audit = new RecordingAuditWriter();

        var result = await Create(store, audit).ExecuteAsync(
            "market.auctions_enabled",
            "not-json",
            null,
            "not valid",
            "flag-4",
            CancellationToken.None);

        result.Outcome.Should().Be(SetFeatureFlagOutcome.InvalidFlag);
        store.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var act = async () => await Create(new RecordingFeatureFlagStore(), new RecordingAuditWriter())
            .ExecuteAsync("market.auctions_enabled", "true", null, reason, "flag-5", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static SetFeatureFlag Create(
        RecordingFeatureFlagStore store,
        RecordingAuditWriter audit,
        IReadOnlyMode? readOnly = null) =>
        new(
            new FixedClock(),
            store,
            readOnly ?? new RecordingReadOnlyMode(),
            audit,
            new StubSecureTokens(),
            new StubRequestContext(),
            new RecordingUnitOfWork());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingReadOnlyMode : IReadOnlyMode
    {
        public int Invalidations { get; private set; }

        public Task<ReadOnlyModeState> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ReadOnlyModeState.Off);

        public void Invalidate() => Invalidations++;
    }

    private sealed class RecordingFeatureFlagStore : IFeatureFlagStore
    {
        public FeatureFlagSnapshot? Existing { get; init; }

        public List<(string Scope, string Key, string ValueJson, string? RolloutMetadataJson)> Writes { get; } = [];

        public Task<FeatureFlagSnapshot?> FindAsync(
            string scope,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Existing is not null && Existing.Scope == scope && Existing.Key == key ? Existing : null);

        public FeatureFlagSnapshot Upsert(
            string scope,
            string key,
            string valueJson,
            string? rolloutMetadataJson,
            DateTimeOffset now)
        {
            Writes.Add((scope, key, valueJson, rolloutMetadataJson));

            return new FeatureFlagSnapshot(
                Existing?.Id ?? Guid.CreateVersion7(),
                scope,
                key,
                valueJson,
                rolloutMetadataJson,
                (Existing?.Version ?? 0) + 1);
        }
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
}
