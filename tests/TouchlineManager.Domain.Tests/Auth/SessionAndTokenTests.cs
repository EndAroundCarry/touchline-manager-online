using FluentAssertions;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Domain.Tests.Auth;

/// <summary>
/// Refresh-session and email-token invariants. These are what rotation, reuse detection, and
/// single-use links are built on (ADR-0002).
/// </summary>
public sealed class RefreshSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_issued_session_is_active_until_it_expires()
    {
        var session = Issue();

        session.IsActive(Now).Should().BeTrue();
        session.IsActive(Now.AddDays(29)).Should().BeTrue();
        session.IsActive(Now.AddDays(31)).Should().BeFalse();
        session.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void A_session_must_expire_in_the_future()
    {
        var act = () => RefreshSession.Issue(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "hash",
            Guid.CreateVersion7(),
            Now,
            Now,
            null,
            null);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Rotating_a_session_records_its_replacement_and_reason()
    {
        var session = Issue();
        var replacementId = Guid.CreateVersion7();

        session.Rotate(replacementId, Now.AddMinutes(1));

        session.RevokedAt.Should().Be(Now.AddMinutes(1));
        session.RevocationReason.Should().Be(RefreshSessionRevocationReasons.Rotated);
        session.ReplacedBySessionId.Should().Be(replacementId);
        session.IsActive(Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Rotating_twice_keeps_the_first_replacement()
    {
        var session = Issue();
        var firstReplacement = Guid.CreateVersion7();

        session.Rotate(firstReplacement, Now.AddMinutes(1));
        session.Rotate(Guid.CreateVersion7(), Now.AddMinutes(2));

        session.ReplacedBySessionId.Should().Be(firstReplacement);
    }

    [Fact]
    public void Rotation_and_revocation_advance_the_version()
    {
        var session = Issue();

        session.Rotate(Guid.CreateVersion7(), Now.AddMinutes(1));

        session.Version.Should().Be(2);
    }

    [Fact]
    public void Revoking_is_idempotent_and_keeps_the_first_reason()
    {
        var session = Issue();

        session.Revoke(RefreshSessionRevocationReasons.Logout, Now.AddMinutes(1));
        session.Revoke(RefreshSessionRevocationReasons.ReuseDetected, Now.AddMinutes(2));

        session.RevocationReason.Should().Be(RefreshSessionRevocationReasons.Logout);
        session.Version.Should().Be(2);
    }

    [Fact]
    public void Revoking_requires_a_reason()
    {
        var session = Issue();

        var act = () => session.Revoke(" ", Now);

        act.Should().Throw<ArgumentException>();
    }

    private static RefreshSession Issue() => RefreshSession.Issue(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "token-hash",
        Guid.CreateVersion7(),
        Now,
        Now.AddDays(30),
        "ip-hash",
        "agent-hash");
}

/// <summary>Single-use, purpose-scoped email tokens (ADR-0002).</summary>
public sealed class EmailTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_token_is_usable_until_it_expires()
    {
        var token = EmailToken.Issue(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EmailTokenPurpose.VerifyEmail,
            "hash",
            Now,
            TimeSpan.FromHours(24));

        token.IsUsable(Now).Should().BeTrue();
        token.IsUsable(Now.AddHours(23)).Should().BeTrue();
        token.IsUsable(Now.AddHours(25)).Should().BeFalse();
    }

    [Fact]
    public void Consuming_a_token_makes_it_unusable_for_a_second_attempt()
    {
        var token = Issue();

        token.Consume(Now.AddMinutes(1));

        token.ConsumedAt.Should().Be(Now.AddMinutes(1));
        token.IsUsable(Now.AddMinutes(2)).Should().BeFalse();

        var act = () => token.Consume(Now.AddMinutes(2));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_expired_token_cannot_be_consumed()
    {
        var token = Issue();

        var act = () => token.Consume(Now.AddHours(25));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Superseding_invalidates_a_live_token()
    {
        var token = Issue();

        token.Supersede(Now.AddMinutes(1));

        token.IsUsable(Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Superseding_an_already_consumed_token_keeps_the_first_instant()
    {
        var token = Issue();
        token.Consume(Now.AddMinutes(1));

        token.Supersede(Now.AddMinutes(2));

        token.ConsumedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void A_token_lifetime_must_be_positive()
    {
        var act = () => EmailToken.Issue(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EmailTokenPurpose.ResetPassword,
            "hash",
            Now,
            TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(EmailTokenPurpose.VerifyEmail, "verify_email")]
    [InlineData(EmailTokenPurpose.ResetPassword, "reset_password")]
    public void Purposes_round_trip_through_their_storage_codes(EmailTokenPurpose purpose, string code)
    {
        purpose.ToStorageValue().Should().Be(code);
        EmailTokenPurposes.FromStorageValue(code).Should().Be(purpose);
    }

    private static EmailToken Issue() => EmailToken.Issue(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        EmailTokenPurpose.VerifyEmail,
        "hash",
        Now,
        TimeSpan.FromHours(24));
}

/// <summary>Consent records are append-only evidence (master plan §12.4).</summary>
public sealed class UserConsentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Recording_a_consent_captures_the_document_version()
    {
        var userId = Guid.CreateVersion7();

        var consent = UserConsent.Record(
            Guid.CreateVersion7(),
            userId,
            ConsentDocumentTypes.Terms,
            "2026-01-01",
            Now,
            "ip-hash");

        consent.UserId.Should().Be(userId);
        consent.DocumentType.Should().Be(ConsentDocumentTypes.Terms);
        consent.Version.Should().Be("2026-01-01");
        consent.AcceptedAt.Should().Be(Now);
        consent.IpHash.Should().Be("ip-hash");
    }

    [Fact]
    public void An_unknown_document_type_is_rejected()
    {
        var act = () => UserConsent.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "marketing",
            "1",
            Now,
            null);

        act.Should().Throw<ArgumentException>();
    }
}
