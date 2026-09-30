using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Auth;

/// <summary>
/// The multi-factor credential's storage contract, against real PostgreSQL 17 (master plan §13, `F-46`,
/// ADR-0042).
/// </summary>
/// <remarks>
/// The point of these tests is the threat model's I-10: a disclosed database must not yield a usable
/// second factor. That is a property of the stored bytes, not of an in-memory object, so it is asserted
/// against the column.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MfaPersistenceTests
{
    private static readonly byte[] Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public MfaPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_stored_secret_is_ciphertext_and_never_the_raw_secret()
    {
        await using var scope = _fixture.CreateScope();
        var userId = await CreateUserAsync(scope);
        var protector = scope.ServiceProvider.GetRequiredService<IMfaSecretProtector>();
        var credentials = scope.ServiceProvider.GetRequiredService<IMfaCredentialRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        credentials.Add(MfaCredential.StartEnrolment(userId, protector.Protect(Secret), _fixture.Clock.UtcNow));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var stored = await db.Database
            .SqlQueryRaw<string>(
                "select protected_secret as \"Value\" from auth.mfa_credentials where user_id = {0}",
                userId)
            .SingleAsync();

        stored.Should().StartWith("v1.", "the stored value carries the protection scheme's version");
        stored.Should().NotContain(Base32.Encode(Secret), "the base32 secret must never be stored");
        stored.Should().NotContain(Encoding.ASCII.GetString(Secret), "the raw secret must never be stored");

        // And it recovers exactly, so the protection is reversible by the server and nothing else.
        var credential = await credentials.FindByUserIdAsync(userId, CancellationToken.None);

        protector.Unprotect(credential!.ProtectedSecret).Should().Equal(Secret);
    }

    [Fact]
    public async Task Recovery_codes_are_stored_as_hashes_and_go_with_the_credential()
    {
        Guid userId;

        await using (var scope = _fixture.CreateScope())
        {
            userId = await CreateUserAsync(scope);
            var protector = scope.ServiceProvider.GetRequiredService<IMfaSecretProtector>();
            var credentials = scope.ServiceProvider.GetRequiredService<IMfaCredentialRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var credential = MfaCredential.StartEnrolment(
                userId,
                protector.Protect(Secret),
                _fixture.Clock.UtcNow);

            credential.Confirm(_fixture.Clock.UtcNow);
            credential.ReplaceRecoveryCodes(["hash-one", "hash-two"], _fixture.Clock.UtcNow);

            credentials.Add(credential);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using (var scope = _fixture.CreateScope())
        {
            var credentials = scope.ServiceProvider.GetRequiredService<IMfaCredentialRepository>();
            var loaded = await credentials.FindByUserIdAsync(userId, CancellationToken.None);

            loaded!.RecoveryCodes.Should().HaveCount(2);
            loaded.IsConfirmed.Should().BeTrue();
        }

        // Removing the credential takes its codes with it: they are the account's own secret, never history.
        await using (var scope = _fixture.CreateScope())
        {
            var credentials = scope.ServiceProvider.GetRequiredService<IMfaCredentialRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var loaded = await credentials.FindByUserIdAsync(userId, CancellationToken.None);

            credentials.Remove(loaded!);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using (var assert = _fixture.CreateScope())
        {
            var db = assert.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (await db.MfaCredentials.CountAsync(candidate => candidate.UserId == userId)).Should().Be(0);
            (await db.MfaRecoveryCodes.CountAsync(code => code.UserId == userId))
                .Should().Be(0, "the codes cascade with the credential");
        }
    }

    [Fact]
    public async Task One_account_has_at_most_one_credential()
    {
        var userId = Guid.CreateVersion7();

        await using (var first = _fixture.CreateScope())
        {
            var users = first.ServiceProvider.GetRequiredService<IUserRepository>();
            var unitOfWork = first.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var protector = first.ServiceProvider.GetRequiredService<IMfaSecretProtector>();
            var credentials = first.ServiceProvider.GetRequiredService<IMfaCredentialRepository>();
            var now = _fixture.Clock.UtcNow;

            users.Add(User.Register(
                userId,
                $"{userId:N}@example.com",
                $"Manager {Guid.NewGuid():N}"[..24],
                "hash",
                "stamp",
                now));

            credentials.Add(MfaCredential.StartEnrolment(userId, protector.Protect(Secret), now));

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        // A separate scope, so the constraint is what refuses the second credential rather than the
        // change tracker noticing a duplicate key in memory.
        await using var second = _fixture.CreateScope();
        var secondProtector = second.ServiceProvider.GetRequiredService<IMfaSecretProtector>();
        var secondCredentials = second.ServiceProvider.GetRequiredService<IMfaCredentialRepository>();
        var secondUnitOfWork = second.ServiceProvider.GetRequiredService<IUnitOfWork>();

        secondCredentials.Add(
            MfaCredential.StartEnrolment(userId, secondProtector.Protect(Secret), _fixture.Clock.UtcNow));

        var act = async () => await secondUnitOfWork.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task<Guid> CreateUserAsync(AsyncServiceScope scope)
    {
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = _fixture.Clock.UtcNow;
        var userId = Guid.CreateVersion7();

        users.Add(User.Register(
            userId,
            $"{userId:N}@example.com",
            $"Manager {Guid.NewGuid():N}"[..24],
            "hash",
            "stamp",
            now));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return userId;
    }
}
