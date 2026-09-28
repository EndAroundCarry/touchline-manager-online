namespace TouchlineManager.Application.Abstractions.Comms;

/// <summary>An intention to reach outside the database, recorded in the transaction that caused it (`COM-4`).</summary>
/// <param name="Type">The dispatch kind, e.g. <c>email</c>.</param>
/// <param name="AggregateType">The kind of aggregate the message is about, for tracing.</param>
/// <param name="AggregateId">The aggregate, or null.</param>
/// <param name="PayloadJson">The versioned payload the dispatcher reads.</param>
/// <param name="DueAt">When it becomes due, or null for the current instant.</param>
public sealed record OutboxDraft(
    string Type,
    string AggregateType,
    Guid? AggregateId,
    string PayloadJson,
    DateTimeOffset? DueAt = null);

/// <summary>
/// Stages an outbox message in the caller's unit of work (`MOD-4`).
/// </summary>
/// <remarks>
/// The seam that lets a use case record "email this manager" without knowing the transport: it fills in the
/// identity, the correlation id, and the attempt budget, so every caller records an intention the same way
/// and the row commits with the domain change that produced it.
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>Stages a message for dispatch.</summary>
    /// <param name="draft">The intention.</param>
    void Enqueue(OutboxDraft draft);
}
