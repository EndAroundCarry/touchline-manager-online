using System.Text.Json;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// Reads and writes an email as an outbox payload (`COM-4`, ADR-0028).
/// </summary>
/// <remarks>
/// The payload is the email itself, versioned like every other durable document, so the dispatcher needs no
/// second read to reconstruct it and a future change to the message shape is a change to this one file. It is
/// pure, so it is exercised by tests without a mail server.
/// </remarks>
public static class EmailOutbox
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the outbox draft for an email.</summary>
    /// <param name="message">The email.</param>
    /// <param name="aggregateId">The entity the email is about, or null.</param>
    public static OutboxDraft For(EmailMessage message, Guid? aggregateId = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new OutboxDraft(
            OutboxTypes.Email,
            OutboxTypes.EmailAggregateType,
            aggregateId,
            JsonSerializer.Serialize(
                new EmailPayload(message.To, message.Subject, message.TextBody, message.HtmlBody),
                Options));
    }

    /// <summary>Reads an email back from a stored payload.</summary>
    /// <param name="payloadJson">The stored payload.</param>
    /// <param name="message">The email, when the payload was readable.</param>
    public static bool TryRead(string payloadJson, out EmailMessage message)
    {
        message = null!;

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            if (JsonSerializer.Deserialize<EmailPayload>(payloadJson, Options) is not { } payload)
            {
                return false;
            }

            message = new EmailMessage(payload.To, payload.Subject, payload.TextBody, payload.HtmlBody);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record EmailPayload(string To, string Subject, string TextBody, string HtmlBody);
}
