using System.Globalization;
using System.Text;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Application.Ops;

/// <summary>
/// The opaque page cursor the audit search carries (master plan §10, "lists use cursor pagination").
/// </summary>
/// <remarks>
/// The same keyset shape as <see cref="AdminJobCursor"/>: the audit trail is append-only and grows from
/// the top, so an offset would skip or repeat rows whenever one was written between two pages. The
/// position is the last row of the previous page — its occurrence instant and its identity — base64url so
/// it survives a query string, and refused by name when it does not decode.
/// </remarks>
public static class AdminAuditCursor
{
    /// <summary>Encodes a page position as the cursor a client sends back.</summary>
    /// <param name="position">The position.</param>
    public static string Encode(AdminAuditCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{position.OccurredAt.ToUnixTimeMilliseconds()}:{position.Id:D}");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Decodes a cursor, or reports that it is not one this build produced.</summary>
    /// <param name="cursor">The cursor, or null for the first page.</param>
    /// <param name="position">The decoded position.</param>
    public static bool TryDecode(string? cursor, out AdminAuditCursorPosition? position)
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

        position = new AdminAuditCursorPosition(DateTimeOffset.FromUnixTimeMilliseconds(millis), id);

        return true;
    }
}
