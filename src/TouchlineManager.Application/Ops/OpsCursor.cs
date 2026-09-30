using System.Globalization;
using System.Text;

namespace TouchlineManager.Application.Ops;

/// <summary>
/// The shared decoding of an operator-console page cursor: a base64url <c>"{unixMillis}:{guid}"</c>.
/// </summary>
/// <remarks>
/// The console's lists (<see cref="AdminJobCursor"/>, <see cref="AdminAuditCursor"/>) share one wire shape,
/// so one decoder keeps them from drifting. It is a transport token, not a secret: a value this build did
/// not produce is refused rather than interpreted.
/// </remarks>
internal static class OpsCursor
{
    /// <summary>Decodes the shared cursor shape, or reports that it is not one this build produced.</summary>
    /// <param name="cursor">The cursor, which must be non-empty.</param>
    /// <param name="millis">The decoded Unix time in milliseconds.</param>
    /// <param name="id">The decoded identity.</param>
    /// <returns>Whether the cursor decoded.</returns>
    public static bool TryDecode(string cursor, out long millis, out Guid id)
    {
        millis = 0;
        id = Guid.Empty;

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

            millis = long.Parse(raw[..separator], CultureInfo.InvariantCulture);

            return Guid.TryParse(raw[(separator + 1)..], out id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
