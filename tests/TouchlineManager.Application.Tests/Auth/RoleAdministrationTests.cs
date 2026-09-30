using FluentAssertions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Tests.Auth;

/// <summary>
/// Role administration: how operator, support, and admin accounts come to exist, and the audited record
/// of it (master plan §6.2, §10.8, ADR-0042).
/// </summary>
public sealed class RoleAdministrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Granting_a_role_applies_and_audits_the_reason()
    {
        var user = CreateUser();
        var audit = new RecordingAuditWriter();

        var result = await CreateGrant(user, audit, actorUserId: Guid.CreateVersion7())
            .ExecuteAsync(user.Id, UserRoles.Operator, "approved at standup", CancellationToken.None);

        result.Outcome.Should().Be(RoleChangeOutcome.Applied);
        user.HasRole(UserRoles.Operator).Should().BeTrue();

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(AdminAuditActions.RoleGranted);
        audit.Entries[0].ActorType.Should().Be(AuditActorTypes.User);
        audit.Entries[0].TargetType.Should().Be(AuditTargetTypes.User);
        audit.Entries[0].TargetId.Should().Be(user.Id);
        audit.Entries[0].Reason.Should().Be("approved at standup");
    }

    [Fact]
    public async Task Granting_a_role_the_account_already_holds_changes_nothing()
    {
        var user = CreateUser();
        user.GrantRole(UserRoles.Support, Now);
        var audit = new RecordingAuditWriter();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateGrant(user, audit, actorUserId: Guid.CreateVersion7(), unitOfWork)
            .ExecuteAsync(user.Id, UserRoles.Support, "again", CancellationToken.None);

        result.Outcome.Should().Be(RoleChangeOutcome.Unchanged);
        audit.Entries.Should().BeEmpty("nothing changed, so there is nothing to audit");
        unitOfWork.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Granting_an_unknown_role_is_refused()
    {
        var result = await CreateGrant(CreateUser(), new RecordingAuditWriter(), actorUserId: Guid.CreateVersion7())
            .ExecuteAsync(Guid.CreateVersion7(), "superuser", "no", CancellationToken.None);

        result.Outcome.Should().Be(RoleChangeOutcome.InvalidRole);
    }

    [Fact]
    public async Task Granting_to_a_missing_account_reports_not_found()
    {
        var result = await new GrantRole(
                new FixedClock(),
                new StubUserRepository(user: null),
                new RecordingAuditWriter(),
                new StubRequestContext(Guid.CreateVersion7()),
                new RecordingUnitOfWork())
            .ExecuteAsync(Guid.CreateVersion7(), UserRoles.Admin, "no such account", CancellationToken.None);

        result.Outcome.Should().Be(RoleChangeOutcome.UserNotFound);
    }

    [Fact]
    public async Task A_change_with_no_request_actor_is_audited_as_a_service_action()
    {
        // The access-administration tool grants through this same use case with no authenticated request.
        var user = CreateUser();
        var audit = new RecordingAuditWriter();

        await CreateGrant(user, audit, actorUserId: null)
            .ExecuteAsync(user.Id, UserRoles.Admin, "bootstrap", CancellationToken.None);

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].ActorType.Should().Be(AuditActorTypes.Service);
        audit.Entries[0].ActorUserId.Should().BeNull();
    }

    [Fact]
    public async Task Revoking_a_role_applies_and_audits()
    {
        var user = CreateUser();
        user.GrantRole(UserRoles.Operator, Now);
        var audit = new RecordingAuditWriter();

        var result = await CreateRevoke(user, audit, actorUserId: Guid.CreateVersion7())
            .ExecuteAsync(user.Id, UserRoles.Operator, "offboarding", CancellationToken.None);

        result.Outcome.Should().Be(RoleChangeOutcome.Applied);
        user.HasRole(UserRoles.Operator).Should().BeFalse();

        audit.Entries.Should().ContainSingle();
        audit.Entries[0].Action.Should().Be(AdminAuditActions.RoleRevoked);
        audit.Entries[0].Reason.Should().Be("offboarding");
    }

    [Fact]
    public async Task Revoking_the_base_player_role_is_refused()
    {
        var result = await CreateRevoke(CreateUser(), new RecordingAuditWriter(), actorUserId: Guid.CreateVersion7())
            .ExecuteAsync(Guid.CreateVersion7(), UserRoles.Player, "no", CancellationToken.None);

        result.Outcome.Should().Be(RoleChangeOutcome.BaseRoleNotRevocable);
    }

    [Fact]
    public async Task Revoking_a_role_the_account_does_not_hold_changes_nothing()
    {
        var user = CreateUser();
        var audit = new RecordingAuditWriter();

        var result = await CreateRevoke(user, audit, actorUserId: Guid.CreateVersion7())
            .ExecuteAsync(user.Id, UserRoles.Admin, "not held", CancellationToken.None);

        result.Outcome.Should().Be(RoleChangeOutcome.Unchanged);
        audit.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        var act = async () => await CreateGrant(CreateUser(), new RecordingAuditWriter(), Guid.CreateVersion7())
            .ExecuteAsync(Guid.CreateVersion7(), UserRoles.Operator, reason, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static GrantRole CreateGrant(
        User? user,
        RecordingAuditWriter audit,
        Guid? actorUserId,
        RecordingUnitOfWork? unitOfWork = null) =>
        new(
            new FixedClock(),
            new StubUserRepository(user),
            audit,
            new StubRequestContext(actorUserId),
            unitOfWork ?? new RecordingUnitOfWork());

    private static RevokeRole CreateRevoke(User? user, RecordingAuditWriter audit, Guid? actorUserId) =>
        new(
            new FixedClock(),
            new StubUserRepository(user),
            audit,
            new StubRequestContext(actorUserId),
            new RecordingUnitOfWork());

    private static User CreateUser() => User.Register(
        Guid.CreateVersion7(),
        "ops@example.com",
        "Ops",
        "password-hash",
        "security-stamp",
        Now);

    private sealed class StubUserRepository(User? user) : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(user?.Id == id ? user : null);

        public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(user?.NormalizedEmail == normalizedEmail ? user : null);

        public Task<User?> FindByNormalizedDisplayNameAsync(
            string normalizedDisplayName,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DisplayNameExistsAsync(string normalizedDisplayName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<UserConsent>> ListConsentsAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Add(User userToAdd) => throw new NotSupportedException();

        public void AddConsent(UserConsent consent) => throw new NotSupportedException();
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;

            return Task.FromResult(0);
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync(
            TransactionIsolation isolation,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubRequestContext(Guid? actorUserId) : IRequestContext
    {
        public Guid? ActorUserId { get; } = actorUserId;

        public string CorrelationId { get; } = Guid.CreateVersion7().ToString();

        public string? IpAddress => null;

        public string? UserAgent => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = Now;
    }
}
