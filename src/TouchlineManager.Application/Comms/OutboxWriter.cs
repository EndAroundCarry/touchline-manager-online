using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// Stages outbox messages in the caller's transaction (`MOD-4`, `COM-4`).
/// </summary>
/// <remarks>
/// The identity, correlation id, occurrence instant, and attempt budget are filled here rather than at each
/// call site, so a use case records "send this" and nothing more. It does not save: the row commits with the
/// domain change that produced it, which is the whole point of an outbox.
/// </remarks>
public sealed class OutboxWriter : IOutboxWriter
{
    private readonly IOutboxRepository _outbox;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;

    /// <summary>Initializes the writer.</summary>
    public OutboxWriter(IOutboxRepository outbox, IClock clock, IRequestContext requestContext)
    {
        _outbox = outbox;
        _clock = clock;
        _requestContext = requestContext;
    }

    /// <inheritdoc />
    public void Enqueue(OutboxDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var now = _clock.UtcNow;

        _outbox.Add(OutboxMessage.Enqueue(
            Guid.CreateVersion7(),
            draft.Type,
            draft.AggregateType,
            draft.AggregateId,
            _requestContext.CorrelationId,
            draft.PayloadJson,
            draft.DueAt ?? now,
            JobRetryPolicy.DefaultMaxAttempts,
            now));
    }
}
