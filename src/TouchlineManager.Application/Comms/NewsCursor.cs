using System.Globalization;
using System.Text;
using TouchlineManager.Application.Abstractions.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>
/// The opaque page cursor the news read carries (`COM-1`, master plan §10).
/// </summary>
/// <remarks>
/// A keyset cursor for the same reason the inbox has one: the feed grows from the top, so an offset would skip
/// or repeat items whenever one arrives between two pages. It is a transport token, not a secret, and refused
/// by name when it does not decode.
/// </remarks>
public static class NewsCursor
{
    /// <summary>Encodes a page position as the cursor a client sends back.</summary>
    /// <param name="position">The position.</param>
    public static string Encode(NewsCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{position.PublishedAt.ToUnixTimeMilliseconds()}:{position.Id:D}");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Decodes a cursor, or reports that it is not one this build produced.</summary>
    /// <param name="cursor">The cursor, or null for the first page.</param>
    /// <param name="position">The decoded position.</param>
    public static bool TryDecode(string? cursor, out NewsCursorPosition? position)
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

            position = new NewsCursorPosition(DateTimeOffset.FromUnixTimeMilliseconds(millis), id);

            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
