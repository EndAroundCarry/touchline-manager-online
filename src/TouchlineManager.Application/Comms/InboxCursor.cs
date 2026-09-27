using System.Globalization;
using System.Text;
using TouchlineManager.Application.Abstractions.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// The opaque page cursor the inbox read carries (master plan §10, "lists use cursor pagination").
/// </summary>
/// <remarks>
/// <para>
/// A keyset cursor rather than an offset: the inbox grows from the top, so an offset would skip or repeat
/// messages whenever one arrives between two pages. The position is the last message of the previous page —
/// its instant and its identity, because two messages can share an instant and the identity is what makes
/// the order total.
/// </para>
/// <para>
/// It is a transport token, not a secret: base64url so it survives a query string, and refused by name when
/// it does not decode, so a hand-edited cursor is a <c>400</c> rather than a silently wrong page.
/// </para>
/// </remarks>
public static class InboxCursor
{
    /// <summary>Encodes a page position as the cursor a client sends back.</summary>
    /// <param name="position">The position.</param>
    public static string Encode(InboxCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{position.CreatedAt.ToUnixTimeMilliseconds()}:{position.Id:D}");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Decodes a cursor, or reports that it is not one this build produced.</summary>
    /// <param name="cursor">The cursor, or null for the first page.</param>
    /// <param name="position">The decoded position.</param>
    public static bool TryDecode(string? cursor, out InboxCursorPosition? position)
    {
        position = null;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return cursor is null;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = (padded.Length % 4) switch
            {
                2 => padded + "==",
                3 => padded + "=",
                0 => padded,
                _ => throw new FormatException("A cursor is not valid base64url."),
            };

            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = raw.IndexOf(':', StringComparison.Ordinal);

            if (separator <= 0)
            {
                return false;
            }

            var millis = long.Parse(raw[..separator], CultureInfo.InvariantCulture);

            if (!Guid.TryParse(raw[(separator + 1)..], out var id))
            {
                return false;
            }

            position = new InboxCursorPosition(DateTimeOffset.FromUnixTimeMilliseconds(millis), id);

            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
