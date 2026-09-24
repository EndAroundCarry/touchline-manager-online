using FluentValidation;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Application.Auth.Validation;

/// <summary>
/// The shared field rules for auth requests.
/// </summary>
/// <remarks>
/// Centralised so register, reset, and login cannot disagree about what a valid password or display
/// name is. These are request-shape rules; the authoritative password policy is enforced on the way
/// in and re-checked whenever the policy changes.
/// </remarks>
public static class AuthValidationRules
{
    /// <summary>Minimum password length.</summary>
    public const int PasswordMinLength = 12;

    /// <summary>Maximum password length accepted. Bounded so a huge body cannot be used as a hash bomb.</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Minimum display-name length.</summary>
    public const int DisplayNameMinLength = 3;

    /// <summary>Maximum display-name length.</summary>
    public const int DisplayNameMaxLength = 32;

    /// <summary>Maximum email length, per RFC 5321.</summary>
    public const int EmailMaxLength = 254;

    /// <summary>Maximum accepted opaque-token length.</summary>
    public const int TokenMaxLength = 512;

    /// <summary>Applies the email rules.</summary>
    public static IRuleBuilderOptions<T, string> EmailRules<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("An email address is required.")
        .MaximumLength(EmailMaxLength)
        .EmailAddress().WithMessage("That does not look like an email address.");

    /// <summary>Applies the display-name rules.</summary>
    public static IRuleBuilderOptions<T, string> DisplayNameRules<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("A display name is required.")
        .Length(DisplayNameMinLength, DisplayNameMaxLength)
        .Matches("^[A-Za-z0-9][A-Za-z0-9 _'-]*$")
        .WithMessage("Use letters, digits, spaces, apostrophes, hyphens and underscores only.");

    /// <summary>Applies the password rules.</summary>
    public static IRuleBuilderOptions<T, string> PasswordRules<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("A password is required.")
        .Length(PasswordMinLength, PasswordMaxLength)
        .WithMessage($"A password must be between {PasswordMinLength} and {PasswordMaxLength} characters.");
}

/// <summary>Validates <see cref="RegisterRequest"/>.</summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    /// <summary>Initializes the validator.</summary>
    public RegisterRequestValidator()
    {
        RuleFor(request => request.Email).EmailRules();
        RuleFor(request => request.DisplayName).DisplayNameRules();
        RuleFor(request => request.Password).PasswordRules();
        RuleFor(request => request.AcceptTerms)
            .Equal(true)
            .WithMessage("The terms of service and privacy policy must be accepted.");
    }
}

/// <summary>Validates <see cref="LoginRequest"/>.</summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    /// <summary>Initializes the validator.</summary>
    public LoginRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().MaximumLength(AuthValidationRules.EmailMaxLength);
        RuleFor(request => request.Password).NotEmpty().MaximumLength(AuthValidationRules.PasswordMaxLength);
    }
}

/// <summary>Validates <see cref="VerifyEmailRequest"/>.</summary>
public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    /// <summary>Initializes the validator.</summary>
    public VerifyEmailRequestValidator()
    {
        RuleFor(request => request.UserId).NotEmpty();
        RuleFor(request => request.Token)
            .NotEmpty()
            .MaximumLength(AuthValidationRules.TokenMaxLength);
    }
}

/// <summary>Validates <see cref="ResendVerificationRequest"/>.</summary>
public sealed class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    /// <summary>Initializes the validator.</summary>
    public ResendVerificationRequestValidator() => RuleFor(request => request.Email).EmailRules();
}

/// <summary>Validates <see cref="ForgotPasswordRequest"/>.</summary>
public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    /// <summary>Initializes the validator.</summary>
    public ForgotPasswordRequestValidator() => RuleFor(request => request.Email).EmailRules();
}

/// <summary>Validates <see cref="ResetPasswordRequest"/>.</summary>
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    /// <summary>Initializes the validator.</summary>
    public ResetPasswordRequestValidator()
    {
        RuleFor(request => request.UserId).NotEmpty();
        RuleFor(request => request.Token)
            .NotEmpty()
            .MaximumLength(AuthValidationRules.TokenMaxLength);
        RuleFor(request => request.NewPassword).PasswordRules();
    }
}

/// <summary>Validates <see cref="UpdateProfileRequest"/>.</summary>
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    /// <summary>Initializes the validator.</summary>
    public UpdateProfileRequestValidator() => RuleFor(request => request.DisplayName).DisplayNameRules();
}

/// <summary>Validates <see cref="DeleteAccountRequest"/>.</summary>
public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    /// <summary>Initializes the validator.</summary>
    public DeleteAccountRequestValidator() => RuleFor(request => request.Password)
        .NotEmpty()
        .MaximumLength(AuthValidationRules.PasswordMaxLength);
}
