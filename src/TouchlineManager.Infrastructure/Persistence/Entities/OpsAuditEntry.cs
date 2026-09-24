namespace TouchlineManager.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence model for a row in <c>ops.audit_log</c> (master plan §6.9).
/// </summary>
/// <remarks>
/// This is the ops module's storage shape. It is append-only: corrections are new rows, never edits
/// (master plan §13), which is why there is no <c>version</c> column and no update path.
/// </remarks>
public sealed class OpsAuditEntry
{
    /// <summary>Gets or sets the audit row identity.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets what kind of actor acted: <c>user</c>, <c>service</c>, or <c>anonymous</c>.</summary>
    public string ActorType { get; set; } = string.Empty;

    /// <summary>Gets or sets the acting account, when one is authenticated.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Gets or sets the action performed, e.g. <c>auth.login.succeeded</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets the kind of entity acted upon.</summary>
    public string? TargetType { get; set; }

    /// <summary>Gets or sets the identity of the entity acted upon.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Gets or sets the correlation ID that ties this row to logs and jobs.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the real instant the action occurred.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets the hashed client IP, never the raw address.</summary>
    public string? IpHash { get; set; }

    /// <summary>
    /// Gets or sets the state before an operator repair. Populated by admin repair workflows
    /// (Stage 14); null for ordinary manager actions.
    /// </summary>
    public string? BeforeMetadata { get; set; }

    /// <summary>Gets or sets the state after an operator repair. Populated by admin repairs.</summary>
    public string? AfterMetadata { get; set; }

    /// <summary>Gets or sets why the action was taken, for operator actions.</summary>
    public string? Reason { get; set; }
}
