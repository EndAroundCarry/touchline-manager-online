using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Domain.Ops;

namespace TouchlineManager.Application.Comms;

/// <summary>What one dispatch pass did.</summary>
/// <param name="Drained">How many messages the pass considered.</param>
/// <param name="Published">How many were dispatched.</param>
/// <param name="Rescheduled">How many failed transiently and were rescheduled.</param>
/// <param name="DeadLettered">How many failed permanently or spent their budget.</param>
public sealed record DispatchOutboxResult(int Drained, int Published, int Rescheduled, int DeadLettered);

/// <summary>
/// Drains the outbox and dispatches each message through its transport (`MOD-4`, `COM-4`, ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// At-least-once by construction: a message is marked published in the same transaction the pass commits, so a
/// worker that dies after sending but before committing resends on the next pass rather than losing the mail.
/// The transport is expected to be idempotent enough for that, which is why the outbox is used for
/// notifications rather than for money.
/// </para>
/// <para>
/// A transport fault reschedules with the job queue's backoff; a defect — a kind no dispatcher knows, or a
/// payload it cannot read — dead-letters at once, because retrying it would only delay the messages behind it.
/// </para>
/// </remarks>
public sealed partial class DispatchOutbox
{
    private readonly IOutboxRepository _outbox;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly OutboxOptions _options;
    private readonly ILogger<DispatchOutbox> _logger;

    /// <summary>Initializes the dispatcher.</summary>
    public DispatchOutbox(
        IOutboxRepository outbox,
        IEmailSender email,
        IClock clock,
        IUnitOfWork unitOfWork,
        IOptions<OutboxOptions> options,
        ILogger<DispatchOutbox> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _outbox = outbox;
        _email = email;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Dispatches the messages that are due.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DispatchOutboxResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        var now = _clock.UtcNow;
        var messages = await _outbox.LoadDueAsync(_options.BatchSize, now, cancellationToken);

        var published = 0;
        var rescheduled = 0;
        var deadLettered = 0;

        foreach (var message in messages)
        {
            var outcome = await DispatchOneAsync(message, now, cancellationToken);

            switch (outcome)
            {
                case DispatchOutcome.Published:
                    published++;
                    break;
                case DispatchOutcome.Rescheduled:
                    rescheduled++;
                    break;
                default:
                    deadLettered++;
                    break;
            }
        }

        if (messages.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new DispatchOutboxResult(messages.Count, published, rescheduled, deadLettered);
    }

    private async Task<DispatchOutcome> DispatchOneAsync(
        OutboxMessage message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(message.Type, OutboxTypes.Email, StringComparison.Ordinal))
        {
            message.DeadLetter($"No dispatcher knows the outbox type '{message.Type}'.", now);
            LogDeadLettered(message.Id, message.Type, "unknown type");

            return DispatchOutcome.DeadLettered;
        }

        if (!EmailOutbox.TryRead(message.PayloadJson, out var email))
        {
            message.DeadLetter("The email payload could not be read.", now);
            LogDeadLettered(message.Id, message.Type, "unreadable payload");

            return DispatchOutcome.DeadLettered;
        }

        try
        {
            await _email.SendAsync(email, cancellationToken);
            message.MarkPublished(now);

            return DispatchOutcome.Published;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var delay = JobRetryPolicy.NextDelay(message.AttemptCount + 1, Random.Shared.NextDouble());

            message.RecordFailure(exception.Message, now.Add(delay), now);

            if (message.Status == OutboxMessageStatus.DeadLettered)
            {
                LogDeadLettered(message.Id, message.Type, exception.Message);

                return DispatchOutcome.DeadLettered;
            }

            LogRescheduled(message.Id, delay);

            return DispatchOutcome.Rescheduled;
        }
    }

    [LoggerMessage(
        EventId = 5300,
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({Type}) was dead-lettered: {Reason} (COM-4).")]
    private partial void LogDeadLettered(Guid messageId, string type, string reason);

    [LoggerMessage(
        EventId = 5301,
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} failed and will be retried in {Delay}.")]
    private partial void LogRescheduled(Guid messageId, TimeSpan delay);

    private enum DispatchOutcome
    {
        Published,
        Rescheduled,
        DeadLettered,
    }
}
