using FluentAssertions;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Domain.Tests.Auth;

/// <summary>
/// The account lifecycle invariants (ADR-0002, master plan §6.2). These are the rules the product
/// promises about verification, suspension, and lockout, asserted without HTTP or a database.
/// </summary>
public sealed class UserAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registration_creates_an_unverified_pending_account_with_the_player_role()
    {
        var user = Register();

        user.Status.Should().Be(UserStatus.Pending);
        user.EmailVerifiedAt.Should().BeNull();
        user.FailedLoginCount.Should().Be(0);
        user.LockoutUntil.Should().BeNull();
        user.RoleNames().Should().Equal(UserRoles.Player);
        user.Version.Should().Be(1);
    }

    [Fact]
    public void Registration_normalizes_the_identity_for_comparison()
    {
        var user = Register(email: "  Manager@Example.COM ", displayName: "  Touch  ");

        user.Email.Should().Be("Manager@Example.COM", "the address is stored as entered");
        user.NormalizedEmail.Should().Be("manager@example.com");
        user.DisplayName.Should().Be("Touch");
        user.NormalizedDisplayName.Should().Be("TOUCH");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Registration_rejects_a_blank_email(string email)
    {
        var act = () => User.Register(Guid.CreateVersion7(), email, "Touch", "hash", "stamp", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Registering_without_a_password_hash_or_stamp_is_refused()
    {
        var noHash = () => User.Register(Guid.CreateVersion7(), "a@example.com", "Touch", "  ", "stamp", Now);
        var noStamp = () => User.Register(Guid.CreateVersion7(), "a@example.com", "Touch", "hash", "  ", Now);

        noHash.Should().Throw<ArgumentException>();
        noStamp.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verifying_an_email_activates_the_account_and_records_the_instant()
    {
        var user = Register();

        user.MarkEmailVerified(Now.AddMinutes(5));

        user.Status.Should().Be(UserStatus.Active);
        user.EmailVerifiedAt.Should().Be(Now.AddMinutes(5));
        user.Version.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Verifying_twice_keeps_the_first_verification_instant()
    {
        var user = Register();

        user.MarkEmailVerified(Now.AddMinutes(5));
        user.MarkEmailVerified(Now.AddHours(1));

        user.EmailVerifiedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void A_suspended_account_cannot_verify_an_email()
    {
        var user = Register();
        user.Suspend("new-stamp", Now);

        var act = () => user.MarkEmailVerified(Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_verified_account_may_change_its_display_name_and_the_normalized_form_follows()
    {
        var user = Register();
        user.MarkEmailVerified(Now);

        user.ChangeDisplayName("  New Name ", Now.AddMinutes(1));

        user.DisplayName.Should().Be("New Name");
        user.NormalizedDisplayName.Should().Be("NEW NAME");
    }

    [Fact]
    public void An_unverified_account_may_not_change_its_display_name()
    {
        // Verification is required before a display-name change (ADR-0002).
        var user = Register();

        var act = () => user.ChangeDisplayName("New Name", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failed_logins_accumulate_and_lock_the_account_at_the_threshold()
    {
        var user = Register();

        for (var attempt = 1; attempt < AccountLockoutPolicy.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
            user.IsLockedOut(Now).Should().BeFalse();
        }

        user.RecordFailedLogin(Now);

        user.FailedLoginCount.Should().Be(AccountLockoutPolicy.MaxFailedAttempts);
        user.LockoutUntil.Should().Be(Now.AddMinutes(1));
        user.IsLockedOut(Now).Should().BeTrue();
        user.CanAuthenticate(Now).Should().BeFalse();
    }

    [Fact]
    public void A_successful_login_clears_the_lockout_state()
    {
        var user = Register();

        for (var attempt = 0; attempt < AccountLockoutPolicy.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        user.RecordSuccessfulLogin(Now.AddMinutes(2));

        user.FailedLoginCount.Should().Be(0);
        user.LockoutUntil.Should().BeNull();
        user.LastLoginAt.Should().Be(Now.AddMinutes(2));
        user.CanAuthenticate(Now.AddMinutes(2)).Should().BeTrue();
    }

    [Fact]
    public void Suspension_invalidates_existing_tokens_and_blocks_authentication()
    {
        var user = Register();
        user.MarkEmailVerified(Now);
        var stampBefore = user.SecurityStamp;

        user.Suspend("rotated-stamp", Now.AddMinutes(1));

        user.Status.Should().Be(UserStatus.Suspended);
        user.SecurityStamp.Should().Be("rotated-stamp").And.NotBe(stampBefore);
        user.CanAuthenticate(Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Restoring_a_suspended_account_returns_it_to_its_verification_state()
    {
        var verified = Register();
        verified.MarkEmailVerified(Now);
        verified.Suspend("stamp-1", Now);
        verified.Restore(Now.AddMinutes(1));
        verified.Status.Should().Be(UserStatus.Active);

        var unverified = Register();
        unverified.Suspend("stamp-2", Now);
        unverified.Restore(Now.AddMinutes(1));
        unverified.Status.Should().Be(UserStatus.Pending);
    }

    [Fact]
    public void Restoring_an_account_that_is_not_suspended_is_a_programming_error()
    {
        var user = Register();

        var act = () => user.Restore(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Revoking_all_sessions_changes_the_stamp_without_changing_status()
    {
        var user = Register();
        user.MarkEmailVerified(Now);

        user.RevokeAllSessions("fresh-stamp", Now.AddMinutes(1));

        user.SecurityStamp.Should().Be("fresh-stamp");
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void Changing_the_password_clears_lockout_and_rotates_the_stamp()
    {
        var user = Register();

        for (var attempt = 0; attempt < AccountLockoutPolicy.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        user.ChangePassword("rehashed", "rotated-stamp", Now.AddMinutes(1));

        user.PasswordHash.Should().Be("rehashed");
        user.SecurityStamp.Should().Be("rotated-stamp");
        user.FailedLoginCount.Should().Be(0);
        user.LockoutUntil.Should().BeNull();
    }

    [Fact]
    public void Upgrading_the_password_hash_leaves_the_stamp_alone()
    {
        // Transparent rehash happens during a successful login; rotating the stamp there would
        // invalidate the token that login is about to issue.
        var user = Register();
        var stamp = user.SecurityStamp;

        user.UpgradePasswordHash("rehashed-with-higher-work-factor", Now.AddMinutes(1));

        user.PasswordHash.Should().Be("rehashed-with-higher-work-factor");
        user.SecurityStamp.Should().Be(stamp);
    }

    [Fact]
    public void Requesting_deletion_closes_access_and_rotates_the_stamp()
    {
        var user = Register();
        user.MarkEmailVerified(Now);

        user.RequestDeletion("rotated-stamp", Now.AddMinutes(1));

        user.Status.Should().Be(UserStatus.DeletionPending);
        user.SecurityStamp.Should().Be("rotated-stamp");
        user.CanAuthenticate(Now.AddMinutes(1)).Should().BeFalse();
        UserStatusRules.IsTerminal(user.Status).Should().BeFalse();
    }

    [Fact]
    public void Requesting_deletion_twice_keeps_the_first_state()
    {
        var user = Register();
        user.RequestDeletion("stamp-1", Now);

        user.RequestDeletion("stamp-2", Now.AddMinutes(1));

        user.SecurityStamp.Should().Be("stamp-1");
    }

    [Fact]
    public void Granting_a_role_is_idempotent_and_rejects_unknown_roles()
    {
        var user = Register();

        user.GrantRole(UserRoles.Player, Now);
        user.GrantRole(UserRoles.Operator, Now);

        user.RoleNames().Should().Equal(UserRoles.Operator, UserRoles.Player);
        user.HasRole(UserRoles.Operator).Should().BeTrue();

        var act = () => user.GrantRole("superuser", Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Role_names_are_returned_in_a_stable_order()
    {
        var user = Register();
        user.GrantRole(UserRoles.Support, Now);
        user.GrantRole(UserRoles.Admin, Now);

        user.RoleNames().Should().Equal(UserRoles.Admin, UserRoles.Player, UserRoles.Support);
    }

    [Fact]
    public void Pending_accounts_may_authenticate_but_not_write()
    {
        var user = Register();

        UserStatusRules.CanAuthenticate(user.Status).Should().BeTrue();
        UserStatusRules.CanWrite(user.Status).Should().BeFalse();
    }

    private static User Register(
        string email = "manager@example.com",
        string displayName = "Touch") =>
        User.Register(
            Guid.CreateVersion7(),
            email,
            displayName,
            "password-hash",
            "security-stamp",
            Now);
}

/// <summary>
/// The status codes are part of the transport contract, so the mapping is pinned.
/// </summary>
public sealed class UserStatusesTests
{
    [Theory]
    [InlineData(UserStatus.Pending, "pending")]
    [InlineData(UserStatus.Active, "active")]
    [InlineData(UserStatus.Suspended, "suspended")]
    [InlineData(UserStatus.DeletionPending, "deletion_pending")]
    [InlineData(UserStatus.Anonymized, "anonymized")]
    public void Every_status_round_trips_through_its_code(UserStatus status, string code)
    {
        status.ToCode().Should().Be(code);
        UserStatuses.FromCode(code).Should().Be(status);
    }

    [Fact]
    public void An_unknown_code_is_a_programming_error()
    {
        var act = () => UserStatuses.FromCode("archived");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
