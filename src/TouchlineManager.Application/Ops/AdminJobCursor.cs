using System.Globalization;
using System.Text;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Application.Ops;

/// <summary>
/// The opaque page cursor the job-queue read carries (master plan §10, "lists use cursor pagination").
/// </summary>
/// <remarks>
/// A keyset cursor rather than an offset: the queue grows from the top as jobs are enqueued, so an offset
/// would skip or repeat rows whenever one arrived between two pages. The position is the last job of the
/// previous page — its creation instant and its identity, because a burst of jobs can share an instant and
/// the identity is what makes the order total. It is base64url so it survives a query string, and refused
/// by name when it does not decode.
/// </remarks>
public static class AdminJobCursor
{
    /// <summary>Encodes a page position as the cursor a client sends back.</summary>
    /// <param name="position">The position.</param>
    public static string Encode(AdminJobCursorPosition position)
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
    public static bool TryDecode(string? cursor, out AdminJobCursorPosition? position)
    {
        position = null;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return cursor is null;
        }

        if (!OpsCursor.TryDecode(cursor, out var millis, out var id))
        {
            return false;
        }

        position = new AdminJobCursorPosition(DateTimeOffset.FromUnixTimeMilliseconds(millis), id);

        return true;
    }
}
