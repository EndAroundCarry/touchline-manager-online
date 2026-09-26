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
    /// Formats an opaque token as a strong entity tag, for an immutable representation.
    /// </summary>
    /// <remarks>
    /// Used where the tag is a fact about the representation rather than a concurrency version — a match
    /// replay is tagged with the output hash of the result it was derived from, so the same bytes are
    /// always cached under the same tag (`MAT-9`, §9.5).
    /// </remarks>
    public static string ForToken(string token) => $"\"{token}\"";

    /// <summary>
    /// Whether an <c>If-None-Match</c> header selects the given entity tag.
    /// </summary>
    /// <remarks>
    /// The weak comparison of RFC 9110 §13.1.2: <c>If-None-Match</c> asks "is this representation already
    /// held", for which a weak tag is equivalent to its strong form. <c>*</c> matches any representation.
    /// </remarks>
    /// <param name="ifNoneMatch">The header value, if any.</param>
    /// <param name="entityTag">The tag the current representation would carry.</param>
    public static bool MatchesIfNoneMatch(string? ifNoneMatch, string entityTag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        var value = ifNoneMatch.Trim();

        if (value == "*")
        {
            return true;
        }

        foreach (var candidate in value.Split(','))
        {
            var trimmed = candidate.Trim();

            if (string.Equals(trimmed, entityTag, StringComparison.Ordinal))
            {
                return true;
            }

            if (trimmed.StartsWith("W/", StringComparison.Ordinal)
                && string.Equals(trimmed[2..], entityTag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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
