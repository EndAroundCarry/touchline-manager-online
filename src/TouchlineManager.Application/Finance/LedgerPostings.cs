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
/// Each factory also decides the correlation key its entry shares, because idempotency is structural: a
/// retried operation collides with its first entry on <c>unique (correlation_id, category)</c> rather than
/// posting a second time (`FIN-17`). The key therefore names the operation and the thing it moved — a
/// fixture, a club's week, a season and a club — and never the amount.
/// </para>
/// </remarks>
public static class LedgerPostings
{
    /// <summary>The template that describes a club's opening balance (`FIN-1`).</summary>
    public const string OpeningBalanceTemplate = "finance.opening_balance";

    /// <summary>The template that describes a home fixture's gate revenue (`FIN-3`).</summary>
    public const string GateReceiptTemplate = "finance.gate_receipt";

    /// <summary>The template that describes a week's sponsorship credit (`FIN-4`).</summary>
    public const string WeeklySponsorshipTemplate = "finance.weekly_sponsorship";

    /// <summary>The template that describes a week's wages (`FIN-7`).</summary>
    public const string WagesTemplate = "finance.wages";

    /// <summary>The template that describes a week's operating cost (`FIN-9`).</summary>
    public const string OperatingCostTemplate = "finance.operating_cost";

    /// <summary>The template that describes a final-position award (`FIN-5`).</summary>
    public const string PositionAwardTemplate = "finance.position_award";

    /// <summary>The template that describes an emergency grant (`FIN-16`).</summary>
    public const string EmergencyGrantTemplate = "finance.emergency_grant";

    /// <summary>The template that describes a compensating entry (`FIN-12`).</summary>
    public const string CompensationTemplate = "finance.compensation";

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

        return new LedgerPosting(
            entryId,
            LedgerCategory.OpeningBalance,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WorldSeed,
            SourceId: clubId,
            clubId.ToString("D", CultureInfo.InvariantCulture),
            OpeningBalanceTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>Builds the posting a home fixture's gate revenue makes (`FIN-3`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The host club.</param>
    /// <param name="fixtureId">The fixture played.</param>
    /// <param name="amountMinor">The gate the fixture drew, in minor units.</param>
    /// <remarks>
    /// The correlation key is the fixture, because a fixture is published once and a retried publication
    /// must not draw the gate twice (`FIN-17`).
    /// </remarks>
    public static LedgerPosting GateReceipt(Guid entryId, Guid clubId, Guid fixtureId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.GateReceipt,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Matchday,
            SourceId: fixtureId,
            $"matchday:{fixtureId:D}",
            GateReceiptTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>Builds the posting a week's sponsorship credit makes (`FIN-4`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="weekOf">The week's boundary date, which is what the run keys on.</param>
    /// <param name="tier">The tier the credit was scaled to.</param>
    /// <param name="amountMinor">The credit, in minor units.</param>
    public static LedgerPosting WeeklySponsorship(
        Guid entryId,
        Guid clubId,
        DateOnly weekOf,
        int tier,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.Sponsorship,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WeeklyRun,
            SourceId: clubId,
            WeeklyCorrelationId(weekOf, clubId),
            WeeklySponsorshipTemplate,
            Parameters(("amountMinor", amountMinor), ("tier", tier)));
    }

    /// <summary>Builds the posting a week's player wages makes (`FIN-7`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club charged.</param>
    /// <param name="weekOf">The week's boundary date.</param>
    /// <param name="playerCount">How many contracted players the wages covered.</param>
    /// <param name="amountMinor">The wage bill, in minor units, as a positive amount to be debited.</param>
    public static LedgerPosting Wages(
        Guid entryId,
        Guid clubId,
        DateOnly weekOf,
        int playerCount,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.Wages,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WeeklyRun,
            SourceId: clubId,
            WeeklyCorrelationId(weekOf, clubId),
            WagesTemplate,
            Parameters(("amountMinor", amountMinor), ("playerCount", playerCount)));
    }

    /// <summary>Builds the posting a week's operating cost makes (`FIN-9`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club charged.</param>
    /// <param name="weekOf">The week's boundary date.</param>
    /// <param name="amountMinor">The cost, in minor units, as a positive amount to be debited.</param>
    public static LedgerPosting OperatingCost(Guid entryId, Guid clubId, DateOnly weekOf, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.OperatingCost,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WeeklyRun,
            SourceId: clubId,
            WeeklyCorrelationId(weekOf, clubId),
            OperatingCostTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>Builds the posting a final-position award makes (`FIN-5`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club awarded.</param>
    /// <param name="seasonId">The season the award belongs to, which is what the key names.</param>
    /// <param name="tier">The tier the club finished in.</param>
    /// <param name="rank">The club's final position, 1–18.</param>
    /// <param name="amountMinor">The award, in minor units.</param>
    /// <remarks>
    /// Settled at rollover by Stage 12; the factory and its key land here with the rest of the stage's
    /// postings.
    /// </remarks>
    public static LedgerPosting PositionAward(
        Guid entryId,
        Guid clubId,
        Guid seasonId,
        int tier,
        int rank,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.PositionAward,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.SeasonRollover,
            SourceId: seasonId,
            $"rollover:{seasonId:D}:{clubId:D}",
            PositionAwardTemplate,
            Parameters(("amountMinor", amountMinor), ("tier", tier), ("rank", rank)));
    }

    /// <summary>Builds the posting an emergency grant makes (`FIN-16`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club rescued.</param>
    /// <param name="weekOf">The week's boundary date the grant belongs to.</param>
    /// <param name="amountMinor">The grant, in minor units.</param>
    /// <remarks>
    /// The grant shares the week's correlation key and carries its own category, so a retried safety
    /// decision collides with the grant it already made rather than granting again (`FIN-17`).
    /// </remarks>
    public static LedgerPosting EmergencyGrant(
        Guid entryId,
        Guid clubId,
        DateOnly weekOf,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.EmergencyGrant,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.SafetyJob,
            SourceId: clubId,
            WeeklyCorrelationId(weekOf, clubId),
            EmergencyGrantTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>Builds a compensating entry that corrects an earlier one (`FIN-12`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club corrected.</param>
    /// <param name="cashDeltaMinor">The signed correction to cash, which may be positive or negative.</param>
    /// <param name="correlationId">The operator's correlation key, which makes the repair idempotent.</param>
    public static LedgerPosting Compensation(
        Guid entryId,
        Guid clubId,
        long cashDeltaMinor,
        string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (cashDeltaMinor == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cashDeltaMinor),
                cashDeltaMinor,
                "A compensating entry must move something (FIN-11).");
        }

        return new LedgerPosting(
            entryId,
            LedgerCategory.Compensation,
            cashDeltaMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.AdminRepair,
            SourceId: clubId,
            correlationId,
            CompensationTemplate,
            Parameters(("amountMinor", cashDeltaMinor)));
    }

    /// <summary>The correlation key a week's postings for one club share (`CON-2`, `FIN-17`).</summary>
    /// <remarks>
    /// Public because the weekly run's audit entry carries the same key, so an operator can follow a week's
    /// ledger lines to the grant, the wages, and the credit that share it.
    /// </remarks>
    /// <param name="weekOf">The week's boundary date.</param>
    /// <param name="clubId">The club the week belongs to.</param>
    public static string WeeklyCorrelationId(DateOnly weekOf, Guid clubId) =>
        $"weekly-run:{weekOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{clubId:D}";

    /// <summary>
    /// Serializes a posting's parameters as the stored document, with an ordinal key order so the same
    /// posting always writes the same bytes.
    /// </summary>
    private static string Parameters(params (string Key, long Value)[] entries)
    {
        var parameters = new Dictionary<string, long>(entries.Length, StringComparer.Ordinal);

        foreach (var (key, value) in entries)
        {
            parameters[key] = value;
        }

        return JsonSerializer.Serialize(parameters);
    }
}
