namespace TouchlineManager.Application.Abstractions;

/// <summary>
/// The ambient facts about the request being served: who is acting, and from where.
/// </summary>
/// <remarks>
/// <para>
/// Use cases need the correlation ID for audit rows and the client fingerprint for abuse analysis,
/// but they must not depend on <c>HttpContext</c> — the worker invokes the same use cases with no
/// HTTP request at all. This port is the seam: the API implements it over the current request, and
/// the worker supplies an empty implementation.
/// </para>
/// <para>
/// Raw IP addresses and user agents never leave this boundary unhashed (data-classification §4).
/// </para>
/// </remarks>
public interface IRequestContext
{
    /// <summary>Gets the authenticated account id, or <see langword="null"/> for anonymous requests.</summary>
    Guid? ActorUserId { get; }

    /// <summary>Gets the correlation ID threaded through logs, audit rows, and job records.</summary>
    string CorrelationId { get; }

    /// <summary>Gets the client IP address, or <see langword="null"/> when unavailable.</summary>
    string? IpAddress { get; }

    /// <summary>Gets the client user agent, or <see langword="null"/> when unavailable.</summary>
    string? UserAgent { get; }
}
