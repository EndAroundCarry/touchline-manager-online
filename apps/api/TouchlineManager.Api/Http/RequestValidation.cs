using FluentValidation;

namespace TouchlineManager.Api.Http;

/// <summary>
/// Runs a request validator and turns a failure into a Problem Details result.
/// </summary>
/// <remarks>
/// Endpoints call this before touching a use case, so validation is a single explicit step rather
/// than advice sprinkled through handlers. The <c>errors</c> shape it produces is what the Angular
/// client renders against individual form fields.
/// </remarks>
internal static class RequestValidation
{
    /// <summary>Validates the request, returning a failure result or <see langword="null"/> when valid.</summary>
    public static async Task<IResult?> ValidateAsync<TRequest>(
        IValidator<TRequest> validator,
        TRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = await validator.ValidateAsync(request, cancellationToken);

        if (result.IsValid)
        {
            return null;
        }

        var errors = result.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return ProblemResults.Validation(errors);
    }
}
