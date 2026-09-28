using System.Globalization;
using System.Text;
using System.Text.Json;
using TouchlineManager.Application.Abstractions.Market;

namespace TouchlineManager.Application.Market;

/// <summary>
/// The opaque keyset cursors the market's list reads carry (master plan §10, "lists use cursor pagination").
/// </summary>
/// <remarks>
/// <para>
/// Keyset rather than offset, so a listing or a transfer arriving between two pages never makes a client skip
/// or repeat a row. Search pages on the sort key and the player identity, because the sort key alone is not a
/// total order; listing and history pages on the row identity alone.
/// </para>
/// <para>
/// A transport token, not a secret: base64url so it survives a query string, and refused by name when it does
/// not decode, so a hand-edited cursor is a <c>400</c> rather than a silently wrong page.
/// </para>
/// </remarks>
public static class MarketCursor
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Encodes a search page position (`SCT-1`).</summary>
    /// <param name="sort">The sort the page was ordered by.</param>
    /// <param name="sortKey">The last row's primary sort value, as a string.</param>
    /// <param name="id">The last row's player identity.</param>
    public static string Encode(PlayerSort sort, string sortKey, Guid id) =>
        EncodeRaw(new PageCursor((int)sort, sortKey, id));

    /// <summary>Decodes a search cursor, or reports that it is not one this build produced.</summary>
    /// <param name="cursor">The cursor, or null for the first page.</param>
    /// <param name="sort">The decoded sort.</param>
    /// <param name="sortKey">The decoded primary sort value.</param>
    /// <param name="id">The decoded player identity.</param>
    public static bool TryDecode(string? cursor, out PlayerSort sort, out string? sortKey, out Guid id)
    {
        sort = PlayerSort.Name;
        sortKey = null;
        id = Guid.Empty;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return cursor is null;
        }

        if (!TryRead(cursor, out var page) || page.Sort is not { } ordinal || page.Key is null
            || !Enum.IsDefined((PlayerSort)ordinal) || page.Id == Guid.Empty)
        {
            return false;
        }

        sort = (PlayerSort)ordinal;
        sortKey = page.Key;
        id = page.Id;

        return true;
    }

    /// <summary>Encodes a page position for a list ordered by row identity alone.</summary>
    /// <param name="id">The last row's identity.</param>
    public static string EncodeId(Guid id) => EncodeRaw(new PageCursor(null, null, id));

    /// <summary>Decodes an identity cursor, or reports that it is not one this build produced.</summary>
    /// <param name="cursor">The cursor, or null for the first page.</param>
    /// <param name="id">The decoded identity.</param>
    public static bool TryDecodeId(string? cursor, out Guid id)
    {
        id = Guid.Empty;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return cursor is null;
        }

        if (!TryRead(cursor, out var page) || page.Id == Guid.Empty)
        {
            return false;
        }

        id = page.Id;

        return true;
    }

    private static string EncodeRaw(PageCursor cursor) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor, Options)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryRead(string cursor, out PageCursor page)
    {
        page = new PageCursor(null, null, Guid.Empty);

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

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var decoded = JsonSerializer.Deserialize<PageCursor>(json, Options);

            if (decoded is null)
            {
                return false;
            }

            page = decoded;

            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed record PageCursor(int? Sort, string? Key, Guid Id);
}
