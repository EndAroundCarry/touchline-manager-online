using TouchlineManager.Application.Abstractions;

namespace TouchlineManager.Infrastructure.Requests;

/// <summary>
/// The request context for work that has no HTTP request: a worker job, or an operator tool.
/// </summary>
/// <remarks>
/// <para>
/// The worker invokes the same use cases the API does, and those use cases need a correlation ID for their
/// audit rows and logs. There is no client behind a job, so the identity fields are null and the
/// correlation ID is generated once per scope — which is what makes one job's audit rows and log lines
/// share a reference even though nobody requested it.
/// </para>
/// <para>
/// The API registers its own implementation over this one, so a manager's actions are always attributed to
/// their request rather than to a generated identifier.
/// </para>
/// </remarks>
internal sealed class ServiceRequestContext : IRequestContext
{
    /// <summary>Initializes the context and generates its correlation ID.</summary>
    public ServiceRequestContext() => CorrelationId = Guid.CreateVersion7().ToString();

    /// <inheritdoc />
    public Guid? ActorUserId => null;

    /// <inheritdoc />
    public string CorrelationId { get; }

    /// <inheritdoc />
    public string? IpAddress => null;

    /// <inheritdoc />
    public string? UserAgent => null;
}
