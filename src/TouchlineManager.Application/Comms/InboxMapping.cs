using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Contracts.Comms;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// Maps the inbox's stored rows to their transport projection (master plan §10.7).
/// </summary>
/// <remarks>
/// One place, so the unread count and the list cannot be shaped differently by two screens. The rendered
/// text is derived here from the row's template and parameters rather than stored, which is what keeps a
/// message's wording a presentation concern instead of a fact baked into the row.
/// </remarks>
public static class InboxMapping
{
    /// <summary>Projects one page, naming the cursor that continues it.</summary>
    /// <param name="page">The page.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static InboxResponse ToResponse(this InboxPage page, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(page);

        var messages = page.Messages
            .Select(message => message.ToResponse())
            .ToList();

        var next = page.HasMore && page.Messages.Count > 0
            ? InboxCursor.Encode(new InboxCursorPosition(page.Messages[^1].CreatedAt, page.Messages[^1].Id))
            : null;

        return new InboxResponse(messages, page.UnreadCount, next, serverTime);
    }

    /// <summary>Projects one stored message, rendering its template.</summary>
    /// <param name="row">The stored row.</param>
    public static InboxMessageResponse ToResponse(this InboxMessageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var text = InboxMessageText.Render(row.TemplateKey, row.ParametersJson);

        return new InboxMessageResponse(
            row.Id,
            row.Category.ToCode(),
            row.TemplateKey,
            text.Title,
            text.Body,
            row.RelatedEntityId,
            row.ReadAt is not null,
            row.CreatedAt,
            row.ReadAt,
            text.Spoiler);
    }
}
