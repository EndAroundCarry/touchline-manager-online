using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Auth;

/// <summary>
/// The auth schema's database-level guarantees, against real PostgreSQL 17 (master plan §15.2).
/// </summary>
/// <remarks>
/// These are the invariants an application check cannot make: a unique index is what makes an email
/// address an identity when two requests race, and a check constraint is the last line against a
/// status the code does not know. An in-memory provider would reproduce none of it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AuthPersistenceTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AuthPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Two_accounts_cannot_share_a_normalized_email_address()
    {
        await using var scope = _fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = _fixture.Clock.UtcNow;

        users.Add(User.Register(
            Guid.CreateVersion7(),
            "same@example.com",
            "Manager One",
            "hash",
            "stamp",
            now));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        // Different casing must collide, because addresses are compared on their normalized form.
        users.Add(User.Register(
            Guid.CreateVersion7(),
            "SAME@example.com",
            "Manager Two",
            "hash",
            "stamp",
            now));

        var act = async () => await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Two_accounts_cannot_share_a_normalized_display_name()
    {
        await using var scope = _fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = _fixture.Clock.UtcNow;

        users.Add(User.Register(Guid.CreateVersion7(), "one@example.com", "Touch", "hash", "stamp", now));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        users.Add(User.Register(Guid.CreateVersion7(), "two@example.com", "touch", "hash", "stamp", now));

        var act = async () => await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_status_check_constraint_rejects_a_status_the_code_does_not_know()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var userId = await CreateUserAsync(scope);

        var act = () => db.Database.ExecuteSqlRawAsync(
            "update auth.users set status = 'archived' where id = {0}",
            userId);

        var exception = await act.Should().ThrowAsync<PostgresException>();

        exception.Which.ConstraintName.Should().Be("ck_users_status");
    }

    [Fact]
    public async Task A_session_must_belong_to_an_existing_account()
    {
        // Restrictive foreign key: session history cannot exist without its account.
        await using var scope = _fixture.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = _fixture.Clock.UtcNow;

        sessions.Add(RefreshSession.Issue(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "orphan-hash",
            Guid.CreateVersion7(),
            now,
            now.AddDays(30),
            null,
            null));

        var act = async () => await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Rotation_is_a_compare_and_set_so_only_one_refresh_can_win()
    {
        await using var scope = _fixture.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = _fixture.Clock.UtcNow;
        var userId = await CreateUserAsync(scope);

        sessions.Add(RefreshSession.Issue(
            Guid.CreateVersion7(),
            userId,
            "rotate-hash",
            Guid.CreateVersion7(),
            now,
            now.AddDays(30),
            null,
            null));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var session = await sessions.FindByTokenHashAsync("rotate-hash", CancellationToken.None);

        session.Should().NotBeNull();

        var first = await sessions.TryRotateAsync(
            session!.Id,
            session.Version,
            Guid.CreateVersion7(),
            now,
            CancellationToken.None);

        // The second caller is presenting a token that has already been consumed, which is the
        // replay signature: the compare-and-set is what makes that detectable.
        var second = await sessions.TryRotateAsync(
            session.Id,
            session.Version,
            Guid.CreateVersion7(),
            now,
            CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_a_family_revokes_every_session_in_it_and_no_others()
    {
        var familyId = Guid.CreateVersion7();

        await using (var arrange = _fixture.CreateScope())
        {
            var sessions = arrange.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
            var unitOfWork = arrange.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var now = _fixture.Clock.UtcNow;
            var userId = await CreateUserAsync(arrange);

            sessions.Add(RefreshSession.Issue(Guid.CreateVersion7(), userId, "family-a", familyId, now, now.AddDays(30), null, null));
            sessions.Add(RefreshSession.Issue(Guid.CreateVersion7(), userId, "family-b", familyId, now, now.AddDays(30), null, null));
            sessions.Add(RefreshSession.Issue(Guid.CreateVersion7(), userId, "other-family", Guid.CreateVersion7(), now, now.AddDays(30), null, null));

            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            await sessions.RevokeFamilyAsync(
                familyId,
                RefreshSessionRevocationReasons.ReuseDetected,
                now,
                CancellationToken.None);
        }

        // A set-based update does not refresh objects the previous scope had already tracked, so the
        // committed result is read back in a fresh scope — which is also what a later request would see.
        await using var assert = _fixture.CreateScope();
        var reader = assert.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
        var now2 = _fixture.Clock.UtcNow;

        (await reader.FindByTokenHashAsync("family-a", CancellationToken.None))!.RevocationReason
            .Should().Be(RefreshSessionRevocationReasons.ReuseDetected);
        (await reader.FindByTokenHashAsync("family-b", CancellationToken.None))!.RevokedAt
            .Should().Be(now2);
        (await reader.FindByTokenHashAsync("other-family", CancellationToken.None))!.IsActive(now2)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Revoking_all_sessions_for_an_account_leaves_other_accounts_alone()
    {
        await using var scope = _fixture.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = _fixture.Clock.UtcNow;
        var target = await CreateUserAsync(scope);
        var bystander = await CreateUserAsync(scope);

        sessions.Add(RefreshSession.Issue(Guid.CreateVersion7(), target, "target-1", Guid.CreateVersion7(), now, now.AddDays(30), null, null));
        sessions.Add(RefreshSession.Issue(Guid.CreateVersion7(), target, "target-2", Guid.CreateVersion7(), now, now.AddDays(30), null, null));
        sessions.Add(RefreshSession.Issue(Guid.CreateVersion7(), bystander, "bystander", Guid.CreateVersion7(), now, now.AddDays(30), null, null));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await sessions.RevokeAllForUserAsync(
            target,
            RefreshSessionRevocationReasons.LogoutAll,
            now,
            CancellationToken.None);

        var targetSessions = await sessions.FindActiveByUserIdAsync(target, CancellationToken.None);
        var bystanderSessions = await sessions.FindActiveByUserIdAsync(bystander, CancellationToken.None);

        targetSessions.Should().BeEmpty();
        bystanderSessions.Should().HaveCount(1);
    }

    private async Task<Guid> CreateUserAsync(AsyncServiceScope scope)
    {
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = _fixture.Clock.UtcNow;
        var userId = Guid.CreateVersion7();
        var suffix = userId.ToString("N");

        // The display name must be unique across the whole table, so it uses random bytes rather than
        // the UUIDv7 prefix — the prefix is a timestamp and collides within one millisecond.
        users.Add(User.Register(
            userId,
            $"{suffix}@example.com",
            $"Manager {Guid.NewGuid():N}"[..24],
            "hash",
            "stamp",
            now));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return userId;
    }
}
