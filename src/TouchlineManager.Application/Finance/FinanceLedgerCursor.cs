using System.Globalization;

namespace TouchlineManager.Application.Finance;

/// <summary>
/// Encodes and decodes the ledger's keyset cursor (`FIN-11`, master plan §10).
/// </summary>
/// <remarks>
/// The ledger is walked backwards by sequence, so the cursor is the last sequence a page returned and the
/// next page is everything below it. A number rather than an opaque blob, because the sequence is the club's
/// own ordered walk and naming it is what makes the page boundary understandable to a client.
/// </remarks>
public static class FinanceLedgerCursor
{
    /// <summary>Encodes the sequence a page continues from.</summary>
    /// <param name="sequence">The smallest sequence on the page.</param>
    public static string Encode(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    /// <summary>Decodes a cursor, treating null or empty as the first page.</summary>
    /// <param name="cursor">The cursor, or null/empty for the first page.</param>
    /// <param name="beforeSequence">The exclusive upper bound, or null for the first page.</param>
    /// <returns>Whether the cursor decoded.</returns>
    public static bool TryDecode(string? cursor, out long? beforeSequence)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            beforeSequence = null;

            return true;
        }

        if (long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            beforeSequence = value;

            return true;
        }

        beforeSequence = null;

        return false;
    }
}
