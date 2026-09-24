using TouchlineManager.Api.Middleware;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Api.Auth;

/// <summary>
/// The ambient request facts an application use case can rely on.
/// </summary>
/// <remarks>
/// This is the seam that keeps <c>HttpContext</c> out of the application layer while still letting a
/// use case record who acted, from where, and under which correlation ID (master plan §12.3). Raw IP
/// and user agent values leave this type only to be hashed.
/// </remarks>
internal sealed class HttpRequestContext : IRequestContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes the context.</summary>
    public HttpRequestContext(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    /// <inheritdoc />
    public Guid? ActorUserId
    {
        get
        {
            var subject = _httpContextAccessor.HttpContext?.User.FindFirst(AuthClaimNames.Subject)?.Value;

            return Guid.TryParse(subject, out var userId) ? userId : null;
        }
    }

    /// <inheritdoc />
    public string CorrelationId =>
        _httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string ?? string.Empty;

    /// <inheritdoc />
    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <inheritdoc />
    public string? UserAgent => _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
