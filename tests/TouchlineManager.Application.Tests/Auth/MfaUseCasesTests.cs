using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Tests.Auth;

/// <summary>
/// The multi-factor use cases: enrolment, confirmation, login completion, and the paths back (ADR-0042).
/// </summary>
public sealed class MfaUseCasesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    // A fixed secret so a code can be computed in the test, standing in for the protected stored one.
    private static readonly byte[] Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Fact]
    public async Task Enrolling_issues_a_secret_and_recovery_codes_and_audits()
    {
        var user = ActiveUser();
        var credentials = new RecordingMfaRepository();
        var audit = new RecordingAuditWriter();

        var result = await CreateEnrol(user, credentials, audit).ExecuteAsync(user.Id, CancellationToken.None);

        result.Outcome.Should().Be(MfaEnrolmentOutcome.Enrolled);
        result.Secret.Should().NotBeNullOrWhiteSpace();
        result.OtpAuthUri.Should().Contain(result.Secret!);
        result.RecoveryCodes.Should().HaveCount(MfaRecoveryCodeIssuer.CodeCount);

        credentials.Credentials.Should().ContainSingle();
        credentials.Credentials[0].IsConfirmed.Should().BeFalse("the first valid code is what confirms it");
        credentials.Credentials[0].RecoveryCodes.Should().HaveCount(MfaRecoveryCodeIssuer.CodeCount);

        audit.Entries.Should().ContainSingle()
            .Which.Action.Should().Be(AdminAuditActions.MfaEnrolmentStarted);
    }

    [Fact]
    public async Task Enrolling_twice_is_refused_once_confirmed()
    {
        var user = ActiveUser();
        var credential = ConfirmedCredential(user.Id);
        var credentials = new RecordingMfaRepository(credential);

        var result = await CreateEnrol(user, credentials, new RecordingAuditWriter())
            .ExecuteAsync(user.Id, CancellationToken.None);

        result.Outcome.Should().Be(MfaEnrolmentOutcome.AlreadyEnrolled);
        credential.ProtectedSecret.Should().Be(credentials.Credentials[0].ProtectedSecret);
    }

    [Fact]
    public async Task Confirming_with_the_right_code_confirms_the_credential()
    {
        var user = ActiveUser();
        var credential = PendingCredential(user.Id);
        var credentials = new RecordingMfaRepository(credential);
        var audit = new RecordingAuditWriter();

        var outcome = await CreateConfirm(user, credentials, audit)
            .ExecuteAsync(user.Id, CurrentCode(), CancellationToken.None);

        outcome.Should().Be(MfaConfirmOutcome.Confirmed);
        credential.IsConfirmed.Should().BeTrue();
        audit.Entries.Should().ContainSingle().Which.Action.Should().Be(AdminAuditActions.MfaEnabled);
    }

    [Fact]
    public async Task Confirming_with_the_wrong_code_is_refused()
    {
        var user = ActiveUser();
        var credential = PendingCredential(user.Id);
        var credentials = new RecordingMfaRepository(credential);

        var outcome = await CreateConfirm(user, credentials, new RecordingAuditWriter())
            .ExecuteAsync(user.Id, "000000", CancellationToken.None);

        outcome.Should().Be(MfaConfirmOutcome.InvalidCode);
        credential.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Completing_a_login_with_the_right_code_issues_an_mfa_session()
    {
        var user = ActiveUser();
        var credential = ConfirmedCredential(user.Id);
        var credentials = new RecordingMfaRepository(credential);
        var audits = new RecordingAuditWriter();
        var accessTokens = new RecordingAccessTokenIssuer();

        var result = await CreateCompleteLogin(user, credentials, audits, accessTokens)
            .ExecuteAsync("challenge", CurrentCode(), CancellationToken.None);

        result.Outcome.Should().Be(MfaLoginOutcome.Succeeded);
        result.Session.Should().NotBeNull();
        accessTokens.LastMfaCompleted.Should().BeTrue("the session earned its second factor");
        audits.Entries.Should().ContainSingle().Which.Action.Should().Be(AuthAuditActions.LoginSucceeded);
    }

    [Fact]
    public async Task Completing_a_login_with_a_wrong_code_is_refused()
    {
        var user = ActiveUser();
        var credentials = new RecordingMfaRepository(ConfirmedCredential(user.Id));

        var result = await CreateCompleteLogin(
                user,
                credentials,
                new RecordingAuditWriter(),
                new RecordingAccessTokenIssuer())
            .ExecuteAsync("challenge", "000000", CancellationToken.None);

        result.Outcome.Should().Be(MfaLoginOutcome.InvalidCode);
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task Completing_a_login_with_an_unknown_challenge_is_refused()
    {
        var user = ActiveUser();
        var credentials = new RecordingMfaRepository(ConfirmedCredential(user.Id));

        var result = await CreateCompleteLogin(
                user,
                credentials,
                new RecordingAuditWriter(),
                new RecordingAccessTokenIssuer())
            .ExecuteAsync("not-a-challenge", CurrentCode(), CancellationToken.None);

        result.Outcome.Should().Be(MfaLoginOutcome.InvalidChallenge);
    }

    [Fact]
    public async Task A_recovery_code_completes_a_login_once()
    {
        var user = ActiveUser();
        var credential = ConfirmedCredential(user.Id);
        credential.ReplaceRecoveryCodes(["hash:RECOVERY-1"], Now);
        var credentials = new RecordingMfaRepository(credential);

        var first = await CreateCompleteLogin(
                user,
                credentials,
                new RecordingAuditWriter(),
                new RecordingAccessTokenIssuer())
            .ExecuteAsync("challenge", "RECOVERY-1", CancellationToken.None);

        var second = await CreateCompleteLogin(
                user,
                credentials,
                new RecordingAuditWriter(),
                new RecordingAccessTokenIssuer())
            .ExecuteAsync("challenge", "RECOVERY-1", CancellationToken.None);

        first.Outcome.Should().Be(MfaLoginOutcome.Succeeded);
        second.Outcome.Should().Be(MfaLoginOutcome.InvalidCode, "a recovery code is single use");
        credential.RecoveryCodes.Single().IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task Disabling_is_refused_for_a_role_that_requires_a_second_factor()
    {
        var user = ActiveUser(UserRoles.Operator);
        var credentials = new RecordingMfaRepository(ConfirmedCredential(user.Id));

        var outcome = await CreateDisable(user, credentials, new RecordingAuditWriter())
            .ExecuteAsync(user.Id, CurrentCode(), CancellationToken.None);

        outcome.Should().Be(MfaDisableOutcome.RequiredForRole);
        credentials.Removed.Should().BeFalse();
    }

    [Fact]
    public async Task Disabling_removes_the_credential_for_an_account_allowed_to()
    {
        var user = ActiveUser();
        var credential = ConfirmedCredential(user.Id);
        var credentials = new RecordingMfaRepository(credential);
        var audit = new RecordingAuditWriter();

        var outcome = await CreateDisable(user, credentials, audit)
            .ExecuteAsync(user.Id, CurrentCode(), CancellationToken.None);

        outcome.Should().Be(MfaDisableOutcome.Disabled);
        credentials.Removed.Should().BeTrue();
        audit.Entries.Should().ContainSingle().Which.Action.Should().Be(AdminAuditActions.MfaDisabled);
    }

    private static string CurrentCode() => Totp.Compute(Secret, Totp.CurrentStep(Now));

    private static User ActiveUser(params string[] extraRoles)
    {
        var user = User.Register(Guid.CreateVersion7(), "ops@example.com", "Ops", "hash", "stamp", Now);
        user.MarkEmailVerified(Now);

        foreach (var role in extraRoles)
        {
            user.GrantRole(role, Now);
        }

        return user;
    }

    private static MfaCredential PendingCredential(Guid userId) =>
        MfaCredential.StartEnrolment(userId, Protector.Protect(Secret), Now);

    private static MfaCredential ConfirmedCredential(Guid userId)
    {
        var credential = PendingCredential(userId);
        credential.Confirm(Now);

        return credential;
    }

    private static EnrolMfa CreateEnrol(
        User user,
        RecordingMfaRepository credentials,
        RecordingAuditWriter audit) =>
        new(
            new FixedClock(),
            new StubUserRepository(user),
            credentials,
            Protector,
            new FakeTokenService(),
            new MfaRecoveryCodeIssuer(new FakeTokenService()),
            audit,
            new StubRequestContext(user.Id),
            new RecordingUnitOfWork());

    private static ConfirmMfaEnrolment CreateConfirm(
        User user,
        RecordingMfaRepository credentials,
        RecordingAuditWriter audit) =>
        new(
            new FixedClock(),
            new StubUserRepository(user),
            credentials,
            new MfaAuthenticator(Protector, new FakeTokenService()),
            audit,
            new StubRequestContext(user.Id),
            new RecordingUnitOfWork());

    private static DisableMfa CreateDisable(
        User user,
        RecordingMfaRepository credentials,
        RecordingAuditWriter audit) =>
        new(
            new FixedClock(),
            new StubUserRepository(user),
            credentials,
            new MfaAuthenticator(Protector, new FakeTokenService()),
            audit,
            new StubRequestContext(user.Id),
            new RecordingUnitOfWork());

    private static CompleteMfaLogin CreateCompleteLogin(
        User user,
        RecordingMfaRepository credentials,
        RecordingAuditWriter audit,
        RecordingAccessTokenIssuer accessTokens)
    {
        var tokenService = new FakeTokenService();
        var requestContext = new StubRequestContext(user.Id);
        var sessions = new RecordingRefreshSessionRepository();

        var sessionIssuer = new SessionIssuer(
            sessions,
            tokenService,
            accessTokens,
            requestContext,
            Options.Create(new AuthOptions
            {
                SigningKey = "test-only-signing-key-with-at-least-32-bytes",
                EncryptionKey = "test-only-encryption-key-with-at-least-32-bytes",
            }));

        return new CompleteMfaLogin(
            new FixedClock(),
            new StubUserRepository(user),
            credentials,
            new FakeChallengeIssuer(user.Id, user.SecurityStamp),
            new MfaAuthenticator(Protector, tokenService),
            sessionIssuer,
            tokenService,
            audit,
            requestContext,
            new RecordingUnitOfWork());
    }

    private static readonly FakeSecretProtector Protector = new();

    private sealed class FakeSecretProtector : IMfaSecretProtector
    {
        public string Protect(byte[] secret) => "enc:" + Convert.ToBase64String(secret);

        public byte[] Unprotect(string protectedSecret) => Convert.FromBase64String(protectedSecret["enc:".Length..]);
    }

    private sealed class FakeTokenService : ISecureTokenService
    {
        private int _counter = 1;

        public string CreateToken() => "token";

        public string CreateSecurityStamp() => "stamp";

        public byte[] CreateRandomBytes(int length)
        {
            var bytes = new byte[length];
            var seed = _counter++;

            for (var index = 0; index < length; index++)
            {
                bytes[index] = (byte)(seed + index + 1);
            }

            return bytes;
        }

        public string HashToken(string token) => "hash:" + token;

        public string? HashClientValue(string? value) => value is null ? null : "hash:" + value;
    }

    private sealed class RecordingMfaRepository(params MfaCredential[] credentials) : IMfaCredentialRepository
    {
        public List<MfaCredential> Credentials { get; } = [.. credentials];

        public bool Removed { get; private set; }

        public Task<MfaCredential?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Credentials.FirstOrDefault(credential => credential.UserId == userId));

        public void Add(MfaCredential credential) => Credentials.Add(credential);

        public void Remove(MfaCredential credential)
        {
            Credentials.Remove(credential);
            Removed = true;
        }
    }

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

    private sealed class RecordingRefreshSessionRepository : IRefreshSessionRepository
    {
        public List<RefreshSession> Added { get; } = [];

        public Task<RefreshSession?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RefreshSession>> FindActiveByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RevokeFamilyAsync(
            Guid familyId,
            string reason,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RevokeAllForUserAsync(
            Guid userId,
            string reason,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RevokeByIdAsync(
            Guid sessionId,
            Guid userId,
            string reason,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryRotateAsync(
            Guid sessionId,
            long expectedVersion,
            Guid replacementSessionId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Add(RefreshSession session) => Added.Add(session);
    }

    private sealed class RecordingAccessTokenIssuer : IAccessTokenIssuer
    {
        public bool? LastMfaCompleted { get; private set; }

        public AccessTokenValue Issue(
            User user,
            IReadOnlyList<string> roles,
            bool mfaCompleted,
            DateTimeOffset now)
        {
            LastMfaCompleted = mfaCompleted;

            return new AccessTokenValue("access-token", now.AddMinutes(15));
        }
    }

    private sealed class FakeChallengeIssuer(Guid userId, string securityStamp) : IMfaChallengeIssuer
    {
        public MfaChallengeValue Issue(User user, DateTimeOffset now) => new("challenge", now.AddMinutes(5));

        public Task<MfaChallengeIdentity?> ValidateAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult<MfaChallengeIdentity?>(
                string.Equals(token, "challenge", StringComparison.Ordinal)
                    ? new MfaChallengeIdentity(userId, securityStamp)
                    : null);
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

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
