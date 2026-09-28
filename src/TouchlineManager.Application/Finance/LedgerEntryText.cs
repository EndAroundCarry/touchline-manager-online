using System.Globalization;
using System.Text.Json;

namespace TouchlineManager.Application.Finance;

/// <summary>
/// Renders a stored ledger entry's template and parameters into the line a manager reads (`FIN-12`,
/// master plan §8.6).
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="LedgerPostings"/>: it reads the durable key and parameter document and
/// produces the description, so the stored entry never holds prose a later language change would have to
/// rewrite — the same contract the match commentary and the inbox use (`MAT-8`).
/// </para>
/// <para>
/// The description names the <em>kind</em> of move, not the amount: the amount is the entry's own columns,
/// and repeating it in prose would be a second statement of the same fact that could disagree with the
/// first. A key this build does not know is refused by name rather than rendered as a vague placeholder,
/// because the writer and the reader share one set of constants.
/// </para>
/// </remarks>
public static class LedgerEntryText
{
    /// <summary>Renders one entry's description.</summary>
    /// <param name="templateKey">The stored template key.</param>
    /// <param name="parametersJson">The stored parameter document.</param>
    /// <exception cref="InvalidOperationException">When the key is not one this build renders.</exception>
    public static string Render(string templateKey, string parametersJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);

        var parameters = Read(parametersJson);

        return templateKey switch
        {
            LedgerPostings.OpeningBalanceTemplate => "Opening balance",
            LedgerPostings.GateReceiptTemplate => "Home-match gate revenue",
            LedgerPostings.WeeklySponsorshipTemplate =>
                string.Create(CultureInfo.InvariantCulture, $"Weekly sponsorship credit (tier {Number(parameters, "tier")})"),
            LedgerPostings.WagesTemplate =>
                string.Create(CultureInfo.InvariantCulture, $"Weekly player wages ({Number(parameters, "playerCount")} players)"),
            LedgerPostings.OperatingCostTemplate => "Weekly operating cost",
            LedgerPostings.PositionAwardTemplate =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Final position award (tier {Number(parameters, "tier")}, rank {Number(parameters, "rank")})"),
            LedgerPostings.EmergencyGrantTemplate => "Emergency grant",
            LedgerPostings.CompensationTemplate => "Compensating entry",
            _ => throw new InvalidOperationException(
                $"'{templateKey}' is not a ledger template this build renders."),
        };
    }

    private static long Number(Dictionary<string, long> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value : 0;

    private static Dictionary<string, long> Read(string parametersJson) =>
        JsonSerializer.Deserialize<Dictionary<string, long>>(parametersJson)
        ?? throw new InvalidOperationException("A stored ledger entry carries no parameters.");
}
