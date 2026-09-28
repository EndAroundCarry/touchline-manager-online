namespace TouchlineManager.Domain.Ops;

/// <summary>The lifecycle of an outbox message (master plan §6.9, `MOD-4`).</summary>
public enum OutboxMessageStatus
{
    /// <summary>Written in a domain transaction and not yet dispatched.</summary>
    Pending = 0,

    /// <summary>Dispatched.</summary>
    Published = 1,

    /// <summary>Refused after its attempt budget ran out, and awaiting an operator.</summary>
    DeadLettered = 2,
}

/// <summary>Storage and transport representation of <see cref="OutboxMessageStatus"/>.</summary>
public static class OutboxMessageStatuses
{
    /// <summary>The longest code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 16;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The status.</param>
    public static string ToCode(this OutboxMessageStatus status) => status switch
    {
        OutboxMessageStatus.Pending => "pending",
        OutboxMessageStatus.Published => "published",
        OutboxMessageStatus.DeadLettered => "dead_letter",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown outbox status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static OutboxMessageStatus FromCode(string code) => code switch
    {
        "pending" => OutboxMessageStatus.Pending,
        "published" => OutboxMessageStatus.Published,
        "dead_letter" => OutboxMessageStatus.DeadLettered,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown outbox status code."),
    };
}

/// <summary>
/// An intent written in the same transaction as the domain change that caused it, and dispatched later
/// (`MOD-4`, master plan §6.9).
/// </summary>
/// <remarks>
/// <para>
/// The inbox and news rows are the game's own record and are written directly; the outbox exists for the
/// side effects that reach outside the database — email today — so that a result and the intention to email
/// about it commit together and a failure to send cannot lose the fact that it was owed (`COM-4`).
/// </para>
/// <para>
/// A row is dispatched at or after <see cref="DueAt"/>. A transient failure reschedules it with the same
/// backoff the job queue uses; exhausting its attempts dead-letters it and raises an operations alert rather
/// than silently dropping a manager's mail (`ADR-0028`).
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private OutboxMessage()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the dispatch kind, e.g. <c>email</c>.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Gets the kind of aggregate the message is about, for tracing.</summary>
    public string AggregateType { get; private set; } = string.Empty;

    /// <summary>Gets the aggregate the message is about, or null.</summary>
    public Guid? AggregateId { get; private set; }

    /// <summary>Gets the operation key the message belongs to (`CONC-3`).</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Gets the versioned payload the dispatcher reads.</summary>
    public string PayloadJson { get; private set; } = string.Empty;

    /// <summary>Gets when the message occurred.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Gets when the message next becomes due for dispatch.</summary>
    public DateTimeOffset DueAt { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public OutboxMessageStatus Status { get; private set; }

    /// <summary>Gets how many dispatch attempts have been made.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Gets the attempt budget before dead-lettering.</summary>
    public int MaxAttempts { get; private set; }

    /// <summary>Gets the last dispatch error, when one occurred.</summary>
    public string? LastError { get; private set; }

    /// <summary>Gets when the message was dispatched.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Enqueues a message to be dispatched.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="type">The dispatch kind.</param>
    /// <param name="aggregateType">The kind of aggregate the message is about.</param>
    /// <param name="aggregateId">The aggregate, or null.</param>
    /// <param name="correlationId">The operation key.</param>
    /// <param name="payloadJson">The versioned payload.</param>
    /// <param name="dueAt">When it becomes due. Usually the current instant.</param>
    /// <param name="maxAttempts">The attempt budget.</param>
    /// <param name="now">The current instant.</param>
    public static OutboxMessage Enqueue(
        Guid id,
        string type,
        string aggregateType,
        Guid? aggregateId,
        string correlationId,
        string payloadJson,
        DateTimeOffset dueAt,
        int maxAttempts,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "A message gets at least one attempt.");
        }

        return new OutboxMessage
        {
            Id = id,
            Type = type,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            CorrelationId = correlationId,
            PayloadJson = payloadJson,
            OccurredAt = now,
            DueAt = dueAt,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Marks the message dispatched. Idempotent.</summary>
    /// <param name="now">The current instant.</param>
    public void MarkPublished(DateTimeOffset now)
    {
        if (Status == OutboxMessageStatus.Published)
        {
            return;
        }

        Status = OutboxMessageStatus.Published;
        PublishedAt = now;
        LastError = null;

        Touch(now);
    }

    /// <summary>
    /// Records a failed attempt and reschedules the message, or dead-letters it when its budget is spent.
    /// </summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="nextDueAt">When to try again, when another attempt remains.</param>
    /// <param name="now">The current instant.</param>
    public void RecordFailure(string error, DateTimeOffset nextDueAt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        AttemptCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;

        if (AttemptCount >= MaxAttempts)
        {
            Status = OutboxMessageStatus.DeadLettered;
        }
        else
        {
            DueAt = nextDueAt;
        }

        Touch(now);
    }

    /// <summary>Dead-letters a message that can never succeed, without spending its retry budget.</summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// Reserved for defects rather than transport faults: a message kind no dispatcher understands, or a
    /// payload it cannot read, will not become dispatchable on the next attempt, so retrying it would only
    /// delay a worker that could be sending a manager's mail. A transport fault goes through
    /// <see cref="RecordFailure"/> instead and keeps its budget.
    /// </remarks>
    public void DeadLetter(string error, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        AttemptCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        Status = OutboxMessageStatus.DeadLettered;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}