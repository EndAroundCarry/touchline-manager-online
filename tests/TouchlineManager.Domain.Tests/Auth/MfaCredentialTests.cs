using FluentAssertions;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Domain.Tests.Auth;

/// <summary>
/// The multi-factor credential's lifecycle and the base-role requirement (ADR-0042).
/// </summary>
public sealed class MfaCredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_credential_is_unconfirmed_and_carries_no_recovery_codes()
    {
        var credential = MfaCredential.StartEnrolment(Guid.CreateVersion7(), "protected-secret", Now);

        credential.IsConfirmed.Should().BeFalse();
        credential.ConfirmedAt.Should().BeNull();
        credential.ProtectedSecret.Should().Be("protected-secret");
        credential.RecoveryCodes.Should().BeEmpty();
        credential.Version.Should().Be(1);
    }

    [Fact]
    public void Confirming_records_the_first_instant_and_is_idempotent()
    {
        var credential = MfaCredential.StartEnrolment(Guid.CreateVersion7(), "protected-secret", Now);

        credential.Confirm(Now.AddMinutes(1));
        credential.Confirm(Now.AddMinutes(2));

        credential.IsConfirmed.Should().BeTrue();
        credential.ConfirmedAt.Should().Be(Now.AddMinutes(1), "the first valid code confirms it");
    }

    [Fact]
    public void Replacing_the_secret_returns_the_credential_to_unconfirmed()
    {
        var credential = MfaCredential.StartEnrolment(Guid.CreateVersion7(), "protected-secret", Now);
        credential.Confirm(Now);

        credential.ReplaceSecret("new-protected-secret", Now.AddMinutes(1));

        credential.ProtectedSecret.Should().Be("new-protected-secret");
        credential.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public void Replacing_recovery_codes_replaces_the_whole_set_for_the_account()
    {
        var userId = Guid.CreateVersion7();
        var credential = MfaCredential.StartEnrolment(userId, "protected-secret", Now);

        credential.ReplaceRecoveryCodes(["hash-1", "hash-2"], Now);
        credential.ReplaceRecoveryCodes(["hash-3"], Now.AddMinutes(1));

        credential.RecoveryCodes.Should().ContainSingle()
            .Which.CodeHash.Should().Be("hash-3");
        credential.RecoveryCodes.Should().OnlyContain(code => code.UserId == userId);
    }

    [Fact]
    public void A_recovery_code_is_consumed_once()
    {
        var credential = MfaCredential.StartEnrolment(Guid.CreateVersion7(), "protected-secret", Now);
        credential.ReplaceRecoveryCodes(["hash-1"], Now);
        var code = credential.RecoveryCodes.Single();

        code.IsUsed.Should().BeFalse();

        code.Consume(Now.AddMinutes(1));
        code.Consume(Now.AddMinutes(2));

        code.IsUsed.Should().BeTrue();
        code.UsedAt.Should().Be(Now.AddMinutes(1), "the first use is the one that counts");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_the_operator_roles_make_a_second_factor_mandatory(bool expected)
    {
        string[] roles = expected
            ? [UserRoles.Player, UserRoles.Operator]
            : [UserRoles.Player];

        UserRoles.RequiresSecondFactor(roles).Should().Be(expected);
    }
}
