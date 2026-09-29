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

    /// <summary>The template that describes a leading bid's reservation (`FIN-10`, `TRF-7`).</summary>
    public const string BidReservationTemplate = "finance.bid_reservation";

    /// <summary>The template that describes the release of a bid's reservation (`TRF-7`, `TRF-15`).</summary>
    public const string ReservationReleaseTemplate = "finance.reservation_release";

    /// <summary>The template that describes a buyer paying a transfer fee (`FIN-8`, `TRF-10`).</summary>
    public const string TransferPaymentTemplate = "finance.transfer_payment";

    /// <summary>The template that describes a seller receiving transfer income (`FIN-6`, `TRF-10`).</summary>
    public const string TransferProceedsTemplate = "finance.transfer_proceeds";

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
            $"rollover:{seasonId:N}:{clubId:N}",
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

    /// <summary>
    /// Builds the posting that reserves a leading bid's funds without moving cash (`FIN-10`, `TRF-7`).
    /// </summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The bidding club.</param>
    /// <param name="bidId">The bid holding the reservation, which the correlation key names.</param>
    /// <param name="amountMinor">The amount reserved, in minor units.</param>
    /// <remarks>
    /// Reserving raises <c>reserved_minor</c> and leaves cash untouched, so the money is committed but not
    /// spent (`FIN-10`). The correlation key names the bid, so a retried placement collides with its first
    /// reservation rather than reserving twice (`FIN-17`).
    /// </remarks>
    public static LedgerPosting BidReservation(Guid entryId, Guid clubId, Guid bidId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.BidReservation,
            CashDeltaMinor: 0,
            ReservedDeltaMinor: amountMinor,
            LedgerSourceType.Transfer,
            SourceId: bidId,
            ReservationCorrelationId(bidId, amountMinor),
            BidReservationTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>
    /// Builds the posting that releases a bid's reservation without moving cash (`TRF-7`, `TRF-15`).
    /// </summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club whose reservation is released.</param>
    /// <param name="bidId">The bid whose reservation is released.</param>
    /// <param name="amountMinor">The amount released, in minor units.</param>
    public static LedgerPosting ReservationRelease(Guid entryId, Guid clubId, Guid bidId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.ReservationRelease,
            CashDeltaMinor: 0,
            ReservedDeltaMinor: -amountMinor,
            LedgerSourceType.Transfer,
            SourceId: bidId,
            ReleaseCorrelationId(bidId, amountMinor),
            ReservationReleaseTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>
    /// Builds the posting a buyer makes when its winning bid settles: the fee leaves cash and its reservation
    /// at once (`FIN-8`, `TRF-10`).
    /// </summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The buying club.</param>
    /// <param name="listingId">The listing that settled, which the correlation key names.</param>
    /// <param name="amountMinor">The fee, in minor units.</param>
    /// <remarks>
    /// Both deltas are negative because the money was reserved when the bid led and is paid when it wins: a
    /// payment that moved only cash would leave a stale reservation behind (`FIN-10`).
    /// </remarks>
    public static LedgerPosting TransferPayment(Guid entryId, Guid clubId, Guid listingId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.TransferPayment,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: -amountMinor,
            LedgerSourceType.Transfer,
            SourceId: listingId,
            TransferCorrelationId(listingId),
            TransferPaymentTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>
    /// Builds the posting a seller receives when a listing settles (`FIN-6`, `TRF-10`).
    /// </summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The selling club.</param>
    /// <param name="listingId">The listing that settled, which the correlation key names.</param>
    /// <param name="amountMinor">The fee received, in minor units.</param>
    public static LedgerPosting TransferProceeds(Guid entryId, Guid clubId, Guid listingId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.TransferProceeds,
            CashDeltaMinor: amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Transfer,
            SourceId: listingId,
            TransferCorrelationId(listingId),
            TransferProceedsTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>The correlation key of a bid's reservation (`FIN-17`, `TRF-7`).</summary>
    /// <param name="bidId">The bid.</param>
    /// <param name="amountMinor">The amount reserved, which makes each reservation event its own key.</param>
    /// <remarks>
    /// The amount is part of the key because a raise reserves again: the earlier reservation keeps its own key
    /// and the raise posts a distinct entry rather than colliding with it, while a retry of either is still a
    /// no-op (`FIN-17`).
    /// </remarks>
    public static string ReservationCorrelationId(Guid bidId, long amountMinor) =>
        $"bid:{bidId:D}:reserve:{amountMinor}";

    /// <summary>The correlation key of the release of a bid's reservation (`FIN-17`, `TRF-7`).</summary>
    /// <param name="bidId">The bid.</param>
    /// <param name="amountMinor">The amount released, which makes each release event its own key.</param>
    public static string ReleaseCorrelationId(Guid bidId, long amountMinor) =>
        $"bid:{bidId:D}:release:{amountMinor}";

    /// <summary>The correlation key a settled listing's payment and proceeds share (`FIN-17`, `TRF-10`).</summary>
    /// <param name="listingId">The listing.</param>
    /// <remarks>
    /// The two entries share the key and differ by category, so a retried resolution collides with both
    /// rather than paying or crediting a second time.
    /// </remarks>
    public static string TransferCorrelationId(Guid listingId) =>
        $"transfer:{listingId:D}";

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
