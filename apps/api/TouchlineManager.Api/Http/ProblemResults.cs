using TouchlineManager.Contracts.Http;

namespace TouchlineManager.Api.Http;

/// <summary>
/// Builds RFC 9457 Problem Details responses with a stable machine-readable <c>code</c>.
/// </summary>
/// <remarks>
/// Every failure the product returns goes through here, so a client can rely on <c>code</c> being
/// present and the correlation ID being attached by the Problem Details customization in
/// <c>Program</c>.
/// </remarks>
internal static class ProblemResults
{
    /// <summary>Returns <c>400</c> with per-field validation messages.</summary>
    public static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(
            errors,
            statusCode: StatusCodes.Status400BadRequest,
            title: "The request was not valid.",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = ApiErrorCodes.ValidationFailed,
            });

    /// <summary>Returns the given status with a code, title, and detail.</summary>
    public static IResult Code(int statusCode, string code, string title, string detail) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = code,
            });

    /// <summary>Returns <c>401</c> with the unauthenticated code.</summary>
    public static IResult Unauthenticated(string detail) =>
        Code(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthenticated, "Not signed in.", detail);

    /// <summary>Returns <c>403</c> with the forbidden code.</summary>
    public static IResult Forbidden(string detail) =>
        Code(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Not permitted.", detail);
}
