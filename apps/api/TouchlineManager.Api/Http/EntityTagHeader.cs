using System.Globalization;
using TouchlineManager.Contracts.Http;

namespace TouchlineManager.Api.Http;

/// <summary>
/// Reads and writes the strong entity tag used for optimistic concurrency (ADR-0009, CONC-1).
/// </summary>
/// <remarks>
/// Only strong tags are honoured. A weak tag tells a cache that representations are equivalent, which
/// is not the same claim as "this is the version you read", and accepting it would silently defeat
/// the precondition.
/// </remarks>
internal static class EntityTagHeader
{
    /// <summary>Formats a version as a strong entity tag.</summary>
    public static string ForVersion(long version) =>
        string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");

    /// <summary>
    /// Parses an <c>If-Match</c> header into a version.
    /// </summary>
    /// <returns>The requested version, or <see langword="null"/> when the header is absent or unusable.</returns>
    public static long? ParseIfMatch(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return null;
        }

        var value = ifMatch.Trim();

        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            return null;
        }

        if (value == "*")
        {
            return null;
        }

        value = value.Trim('"');

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : null;
    }
}
