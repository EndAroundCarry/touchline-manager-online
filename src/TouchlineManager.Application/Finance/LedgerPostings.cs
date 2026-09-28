using System.Globalization;
using System.Text.Json;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Finance;

/// <summary>
/// Builds the ledger postings the game makes, so the template keys and their parameters have one home
/// (master plan §6.8, §8.6).
/// </summary>
/// <remarks>
/// <para>
/// This is the finance module's counterpart of <c>InboxTemplates</c>: a description is a stable key and a
/// parameter document rather than finished prose, so a ledger line written today can be rendered in another
/// language later without being rewritten. The key a reader does not know is a defect worth seeing, so the
/// keys live here once, beside the writer.
/// </para>
/// <para>
/// Only the opening balance is here so far, because it is the only posting the game makes yet: the gate
/// receipt, the weekly run, the awards, and the market arrive with the milestones that own them, and each
/// adds its own factory and key beside this one rather than inventing prose.
/// </para>
/// </remarks>
public static class LedgerPostings
{
    /// <summary>The template that describes a club's opening balance (`FIN-1`).</summary>
    public const string OpeningBalanceTemplate = "finance.opening_balance";

    /// <summary>
    /// Builds the posting that funds a newly generated club's account.
    /// </summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club being funded.</param>
    /// <param name="amountMinor">The opening cash, in minor units.</param>
    /// <remarks>
    /// The correlation key is the club's own identity, because a club is funded exactly once: a second
    /// opening posting for the same club carries the same key and the same category and is refused by the
    /// ledger's unique index rather than quietly doubling the balance (`FIN-17`).
    /// </remarks>
    public static LedgerPosting OpeningBalance(Guid entryId, Guid clubId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountMinor);

        var parameters = JsonSerializer.Serialize(new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["amountMinor"] = amountMinor,
        });

        return new LedgerPosting(
            entryId,
            LedgerCategory.OpeningBalance,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WorldSeed,
            SourceId: clubId,
            clubId.ToString("D", CultureInfo.InvariantCulture),
            OpeningBalanceTemplate,
            parameters);
    }
}
