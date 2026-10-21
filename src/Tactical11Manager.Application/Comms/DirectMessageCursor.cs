using Tactical11Manager.Application.Abstractions.Comms;

namespace Tactical11Manager.Application.Comms;

/// <summary>
/// The page cursor of the conversation list and of a thread (`COM-10`).
/// </summary>
/// <remarks>
/// The same keyset token the inbox uses: an instant and an identity, base64url. Both lists order by one and
/// break a tie with the other, so the inbox's format is exactly what they need and a second one would only
/// be a second place to get the encoding wrong.
/// </remarks>
internal static class DirectMessageCursor
{
    /// <summary>Encodes the last row of a page as the cursor a client sends back.</summary>
    /// <param name="at">The row's instant.</param>
    /// <param name="id">The row's identity.</param>
    public static string Encode(DateTimeOffset at, Guid id) => InboxCursor.Encode(new InboxCursorPosition(at, id));

    /// <summary>Decodes a cursor, or reports that it is not one this build produced.</summary>
    /// <param name="cursor">The cursor, or null for the first page.</param>
    /// <param name="position">The decoded position.</param>
    public static bool TryDecode(string? cursor, out DirectMessageCursorPosition? position)
    {
        position = null;

        if (!InboxCursor.TryDecode(cursor, out var decoded))
        {
            return false;
        }

        position = decoded is null ? null : new DirectMessageCursorPosition(decoded.CreatedAt, decoded.Id);

        return true;
    }
}
