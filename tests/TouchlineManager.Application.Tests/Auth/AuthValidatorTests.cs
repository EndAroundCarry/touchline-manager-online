using FluentAssertions;
using TouchlineManager.Application.Auth.Validation;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Application.Tests.Auth;

/// <summary>
/// Request-shape validation. These are the rules a client sees before any use case runs, so they are
/// pinned here rather than discovered by a manager at the form.
/// </summary>
public sealed class AuthValidatorTests
{
    [Fact]
    public void A_well_formed_registration_is_accepted()
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest
        {
            Email = "manager@example.com",
            DisplayName = "Touch",
            Password = "correct-horse-battery",
            AcceptTerms = true,
        });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    public void A_password_that_is_too_short_is_rejected(string password)
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest
        {
            Email = "manager@example.com",
            DisplayName = "Touch",
            Password = password,
            AcceptTerms = true,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void An_unaccepted_terms_checkbox_is_rejected()
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest
        {
            Email = "manager@example.com",
            DisplayName = "Touch",
            Password = "correct-horse-battery",
            AcceptTerms = false,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == nameof(RegisterRequest.AcceptTerms));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void A_malformed_email_is_rejected(string email)
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest
        {
            Email = email,
            DisplayName = "Touch",
            Password = "correct-horse-battery",
            AcceptTerms = true,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == nameof(RegisterRequest.Email));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("this-display-name-is-far-too-long-for-the-limit")]
    [InlineData("bad!chars")]
    public void A_display_name_outside_the_allowed_shape_is_rejected(string displayName)
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest
        {
            Email = "manager@example.com",
            DisplayName = displayName,
            Password = "correct-horse-battery",
            AcceptTerms = true,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(failure => failure.PropertyName == nameof(RegisterRequest.DisplayName));
    }

    [Fact]
    public void Login_requires_both_fields_without_imposing_the_registration_password_policy()
    {
        var validator = new LoginRequestValidator();

        // An existing account may predate a policy change, so login only checks presence.
        validator.Validate(new LoginRequest { Email = "manager@example.com", Password = "old" })
            .IsValid.Should().BeTrue();

        validator.Validate(new LoginRequest { Email = "manager@example.com", Password = string.Empty })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_reset_requires_an_account_and_a_password_that_meets_the_policy()
    {
        var validator = new ResetPasswordRequestValidator();

        validator.Validate(new ResetPasswordRequest
        {
            UserId = Guid.CreateVersion7(),
            Token = "token",
            NewPassword = "correct-horse-battery",
        }).IsValid.Should().BeTrue();

        validator.Validate(new ResetPasswordRequest
        {
            UserId = Guid.Empty,
            Token = "token",
            NewPassword = "short",
        }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_verification_request_requires_an_account_and_a_token()
    {
        var validator = new VerifyEmailRequestValidator();

        validator.Validate(new VerifyEmailRequest { UserId = Guid.Empty, Token = string.Empty })
            .IsValid.Should().BeFalse();

        validator.Validate(new VerifyEmailRequest { UserId = Guid.CreateVersion7(), Token = "token" })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Forgot_password_and_resend_verification_share_the_email_rules()
    {
        new ForgotPasswordRequestValidator().Validate(new ForgotPasswordRequest { Email = "bad" })
            .IsValid.Should().BeFalse();

        new ResendVerificationRequestValidator().Validate(new ResendVerificationRequest { Email = "bad" })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Deleting_an_account_requires_the_confirming_password()
    {
        new DeleteAccountRequestValidator().Validate(new DeleteAccountRequest { Password = string.Empty })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void The_password_length_bounds_match_the_shared_rules()
    {
        AuthValidationRules.PasswordMinLength.Should().Be(12);
        AuthValidationRules.PasswordMaxLength.Should().Be(128);
        AuthValidationRules.DisplayNameMinLength.Should().Be(3);
        AuthValidationRules.DisplayNameMaxLength.Should().Be(32);
    }
}
