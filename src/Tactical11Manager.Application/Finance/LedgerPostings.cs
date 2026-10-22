using System.Globalization;
using System.Text.Json;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Finance;

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

    /// <summary>The template that describes a club paying for new places in its stadium (`STAD-4`).</summary>
    public const string StadiumConstructionTemplate = "finance.stadium_construction";

    /// <summary>The template that describes a club paying to release a player (`CON-12`).</summary>
    public const string ReleasePayoutTemplate = "finance.release_payout";

    /// <summary>The template that describes the snack bar's takings for a home match (`STRC-4`).</summary>
    public const string SnackBarTakingsTemplate = "finance.snack_bar";

    /// <summary>The template that describes the merchandise shop's takings for a week (`STRC-6`).</summary>
    public const string MerchandiseTakingsTemplate = "finance.merchandise";

    /// <summary>The template that describes a week's upkeep for both buildings (`STRC-8`).</summary>
    public const string StructureUpkeepTemplate = "finance.structure_upkeep";

    /// <summary>The template that describes a club paying for a building's upgrade (`STRC-3`).</summary>
    public const string StructureUpgradeTemplate = "finance.structure_upgrade";

    /// <summary>The template that describes a matchday's share of the league's television money (`FIN-4`).</summary>
    public const string LeagueTvTemplate = "finance.league_tv";

    /// <summary>The template that describes a matchday's share of the sponsor's fixed payment (`SPN-3`).</summary>
    public const string SponsorMatchdayTemplate = "finance.sponsor_matchday";

    /// <summary>The template that describes the sponsor's payment for the matchdays played before a deal was signed (`SPN-3`).</summary>
    public const string SponsorBackPayTemplate = "finance.sponsor_back_pay";

    /// <summary>The template that describes a matchday's player wages (`FIN-7`).</summary>
    public const string MatchdayWagesTemplate = "finance.matchday_wages";

    /// <summary>The template that describes the sponsor's bonus for the season's objective (`OBJ-4`).</summary>
    public const string SponsorBonusTemplate = "finance.sponsor_bonus";

    /// <summary>The template that describes a relegated club's parachute (`FIN-5`).</summary>
    public const string ParachuteTemplate = "finance.parachute";

    /// <summary>The template that describes a week's upkeep of the places in the ground (`STAD-7`).</summary>
    public const string GroundUpkeepTemplate = "finance.ground_upkeep";

    /// <summary>The template that describes the fee a player's agent pays when the board releases him unsold (`DEBT-4`).</summary>
    public const string ReleaseFeeTemplate = "finance.release_fee";

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
            RolloverCorrelationId(seasonId, clubId),
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
    /// <param name="reversesEntryId">The entry that is being corrected, or null when the operator names none.</param>
    public static LedgerPosting Compensation(
        Guid entryId,
        Guid clubId,
        long cashDeltaMinor,
        string correlationId,
        Guid? reversesEntryId = null)
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
            Parameters(("amountMinor", cashDeltaMinor)),
            ReversesEntryId: reversesEntryId);
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

    /// <summary>Builds the posting a club's stadium works make (`STAD-4`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club paying.</param>
    /// <param name="stadiumId">The ground that grew, which is what the entry's source names.</param>
    /// <param name="stadiumVersion">The ground's version once the places were added.</param>
    /// <param name="stand">The kind of place built.</param>
    /// <param name="count">How many places were built.</param>
    /// <param name="amountMinor">The price, in minor units, as a positive amount to be debited.</param>
    /// <remarks>
    /// The correlation key is the ground at the version the works produced, because each build moves the
    /// ground's version on by one: a retried order that was already paid for collides with its first entry
    /// instead of charging the club twice, while a genuine second order is a different version and a different
    /// key (`FIN-17`).
    /// </remarks>
    public static LedgerPosting StadiumConstruction(
        Guid entryId,
        Guid clubId,
        Guid stadiumId,
        long stadiumVersion,
        StadiumStand stand,
        int count,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        return new LedgerPosting(
            entryId,
            LedgerCategory.StadiumConstruction,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Stadium,
            SourceId: stadiumId,
            $"stadium:{stadiumId:D}:v{stadiumVersion}",
            StadiumConstructionTemplate,
            Parameters(("amountMinor", amountMinor), ("stand", (long)stand), ("count", count)));
    }

    /// <summary>Builds the posting a released player's payout makes (`CON-12`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club paying.</param>
    /// <param name="contractId">The contract that was closed, which the key and the source name.</param>
    /// <param name="remainingWeeks">The weeks of wages the payout is half of.</param>
    /// <param name="amountMinor">The payout, in minor units, as a positive amount to be debited.</param>
    /// <remarks>
    /// The correlation key is the contract, because a contract closes once: a retried release that was already
    /// paid for collides with its first entry instead of charging the club twice (`FIN-17`).
    /// </remarks>
    public static LedgerPosting ReleasePayout(
        Guid entryId,
        Guid clubId,
        Guid contractId,
        int remainingWeeks,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.ReleasePayout,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Contract,
            SourceId: contractId,
            $"release:{contractId:D}",
            ReleasePayoutTemplate,
            Parameters(("amountMinor", amountMinor), ("weeks", remainingWeeks)));
    }

    /// <summary>Builds the posting the snack bar's takings for one home match make (`STRC-4`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The host club.</param>
    /// <param name="fixtureId">The fixture played.</param>
    /// <param name="level">The snack bar's level.</param>
    /// <param name="spectators">The crowd the takings are for.</param>
    /// <param name="bonusBp">The result bonus, in basis points.</param>
    /// <param name="amountMinor">The takings, in minor units.</param>
    /// <remarks>
    /// The correlation key is the fixture, the same as the gate's: the two entries differ by category, so a
    /// retried publication collides with both instead of selling the crowd its snacks twice (`FIN-17`).
    /// </remarks>
    public static LedgerPosting SnackBarTakings(
        Guid entryId,
        Guid clubId,
        Guid fixtureId,
        int level,
        int spectators,
        int bonusBp,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.SnackBar,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Matchday,
            SourceId: fixtureId,
            $"matchday:{fixtureId:D}",
            SnackBarTakingsTemplate,
            Parameters(
                ("amountMinor", amountMinor), ("level", level), ("spectators", spectators), ("bonusBp", bonusBp)));
    }

    /// <summary>Builds the posting the merchandise shop's takings for one week make (`STRC-6`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="weekOf">The week's boundary date.</param>
    /// <param name="level">The shop's level.</param>
    /// <param name="fans">The fans the takings are for.</param>
    /// <param name="bonusBp">The mood bonus, in basis points.</param>
    /// <param name="amountMinor">The takings, in minor units.</param>
    public static LedgerPosting MerchandiseTakings(
        Guid entryId,
        Guid clubId,
        DateOnly weekOf,
        int level,
        int fans,
        int bonusBp,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.Merchandise,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WeeklyRun,
            SourceId: clubId,
            WeeklyCorrelationId(weekOf, clubId),
            MerchandiseTakingsTemplate,
            Parameters(("amountMinor", amountMinor), ("level", level), ("fans", fans), ("bonusBp", bonusBp)));
    }

    /// <summary>Builds the posting a week's upkeep for both buildings makes (`STRC-8`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club charged.</param>
    /// <param name="weekOf">The week's boundary date.</param>
    /// <param name="snackBarLevel">The snack bar's level.</param>
    /// <param name="merchandiseLevel">The merchandise shop's level.</param>
    /// <param name="amountMinor">The combined upkeep, in minor units, as a positive amount to be debited.</param>
    /// <remarks>
    /// One entry for both buildings, because the ledger allows one entry per category per correlation key; the
    /// per-building split is in the takings rows (`STRC-8`).
    /// </remarks>
    public static LedgerPosting StructureUpkeep(
        Guid entryId,
        Guid clubId,
        DateOnly weekOf,
        int snackBarLevel,
        int merchandiseLevel,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.StructureUpkeep,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WeeklyRun,
            SourceId: clubId,
            WeeklyCorrelationId(weekOf, clubId),
            StructureUpkeepTemplate,
            Parameters(
                ("amountMinor", amountMinor), ("snackBarLevel", snackBarLevel), ("merchandiseLevel", merchandiseLevel)));
    }

    /// <summary>Builds the posting a building's upgrade makes (`STRC-3`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club paying.</param>
    /// <param name="structureId">The building, which the entry's source names.</param>
    /// <param name="structureVersion">The building's version once the upgrade was started.</param>
    /// <param name="kind">Which building.</param>
    /// <param name="targetLevel">The level the work will reach.</param>
    /// <param name="amountMinor">The price, in minor units, as a positive amount to be debited.</param>
    /// <remarks>
    /// The correlation key is the building at the version the order produced, because each order moves the
    /// version on by one: a retried order collides with its first entry instead of charging the club twice,
    /// while the next upgrade is a different version and a different key (`FIN-17`).
    /// </remarks>
    public static LedgerPosting StructureUpgrade(
        Guid entryId,
        Guid clubId,
        Guid structureId,
        long structureVersion,
        StructureKind kind,
        int targetLevel,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.StructureUpgrade,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Structure,
            SourceId: structureId,
            $"structure:{structureId:D}:v{structureVersion}",
            StructureUpgradeTemplate,
            Parameters(("amountMinor", amountMinor), ("kind", (long)kind), ("level", targetLevel)));
    }

    /// <summary>Builds the posting a matchday's share of the league's television money makes (`FIN-4`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="matchdayId">The matchday, which the key names.</param>
    /// <param name="tier">The tier the credit was scaled to.</param>
    /// <param name="amountMinor">The credit, in minor units.</param>
    /// <remarks>
    /// The postings of a matchday for one club (the television money, the sponsor's fixed part and the wages) share one correlation key and
    /// differ by category, so a retried publication collides with each of them instead of paying twice (`FIN-17`).
    /// </remarks>
    public static LedgerPosting LeagueTv(Guid entryId, Guid clubId, Guid matchdayId, int tier, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.LeagueTv,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Matchday,
            SourceId: matchdayId,
            MatchdayMoneyCorrelationId(matchdayId, clubId),
            LeagueTvTemplate,
            Parameters(("amountMinor", amountMinor), ("tier", tier)));
    }

    /// <summary>Builds the posting a matchday's share of the sponsor's fixed payment makes (`SPN-3`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="matchdayId">The matchday, which the key names.</param>
    /// <param name="amountMinor">The credit, in minor units.</param>
    public static LedgerPosting SponsorMatchday(Guid entryId, Guid clubId, Guid matchdayId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.SponsorMatchday,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Matchday,
            SourceId: matchdayId,
            MatchdayMoneyCorrelationId(matchdayId, clubId),
            SponsorMatchdayTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>Builds the posting that pays the sponsor's fixed part for the matchdays already played when a deal is signed (`SPN-3`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="dealId">The deal, which the key and the source name.</param>
    /// <param name="matchdays">How many matchdays the payment covers.</param>
    /// <param name="amountMinor">The credit, in minor units.</param>
    /// <remarks>
    /// The matchdays it pays for are matchdays, so the source is the matchday workflow, named by the deal that is paying them. The key is the
    /// deal, so a retried signing collides with its first payment (`FIN-17`).
    /// </remarks>
    public static LedgerPosting SponsorBackPay(Guid entryId, Guid clubId, Guid dealId, int matchdays, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.SponsorMatchday,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Matchday,
            SourceId: dealId,
            $"sponsor-signing:{dealId:N}",
            SponsorBackPayTemplate,
            Parameters(("amountMinor", amountMinor), ("matchdays", matchdays)));
    }

    /// <summary>Builds the posting a matchday's player wages make (`FIN-7`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club charged.</param>
    /// <param name="matchdayId">The matchday, which the key names.</param>
    /// <param name="playerCount">How many contracted players the wages covered.</param>
    /// <param name="amountMinor">The wage bill for the matchday, in minor units, as a positive amount to be debited.</param>
    public static LedgerPosting MatchdayWages(Guid entryId, Guid clubId, Guid matchdayId, int playerCount, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.Wages,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.Matchday,
            SourceId: matchdayId,
            MatchdayMoneyCorrelationId(matchdayId, clubId),
            MatchdayWagesTemplate,
            Parameters(("amountMinor", amountMinor), ("playerCount", playerCount)));
    }

    /// <summary>Builds the emergency grant the integrity net makes on a matchday (`FIN-16`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club rescued.</param>
    /// <param name="matchdayId">The matchday, which the key names.</param>
    /// <param name="amountMinor">The grant, in minor units.</param>
    public static LedgerPosting MatchdayEmergencyGrant(Guid entryId, Guid clubId, Guid matchdayId, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.EmergencyGrant,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.SafetyJob,
            SourceId: clubId,
            MatchdayMoneyCorrelationId(matchdayId, clubId),
            EmergencyGrantTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>Builds the posting the sponsor's bonus for the season's objective makes (`OBJ-4`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="seasonId">The season the bonus belongs to, which the key names.</param>
    /// <param name="targetPlace">The place the objective asked for.</param>
    /// <param name="finalPlace">The place the club finished in.</param>
    /// <param name="amountMinor">The bonus, in minor units.</param>
    public static LedgerPosting SponsorBonus(
        Guid entryId,
        Guid clubId,
        Guid seasonId,
        int targetPlace,
        int finalPlace,
        long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.SponsorBonus,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.SeasonRollover,
            SourceId: seasonId,
            RolloverCorrelationId(seasonId, clubId),
            SponsorBonusTemplate,
            Parameters(("amountMinor", amountMinor), ("target", targetPlace), ("rank", finalPlace)));
    }

    /// <summary>Builds the posting a relegated club's parachute makes (`FIN-5`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="seasonId">The season the club was relegated from, which the key names.</param>
    /// <param name="fromTier">The tier the club was relegated from.</param>
    /// <param name="amountMinor">The parachute, in minor units.</param>
    public static LedgerPosting Parachute(Guid entryId, Guid clubId, Guid seasonId, int fromTier, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.Parachute,
            amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.SeasonRollover,
            SourceId: seasonId,
            RolloverCorrelationId(seasonId, clubId),
            ParachuteTemplate,
            Parameters(("amountMinor", amountMinor), ("tier", fromTier)));
    }

    /// <summary>Builds the posting a week's ground upkeep makes (`STAD-7`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club charged.</param>
    /// <param name="weekOf">The week's boundary date.</param>
    /// <param name="places">How many places the ground holds.</param>
    /// <param name="amountMinor">The upkeep, in minor units, as a positive amount to be debited.</param>
    public static LedgerPosting GroundUpkeep(Guid entryId, Guid clubId, DateOnly weekOf, int places, long amountMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        return new LedgerPosting(
            entryId,
            LedgerCategory.GroundUpkeep,
            CashDeltaMinor: -amountMinor,
            ReservedDeltaMinor: 0,
            LedgerSourceType.WeeklyRun,
            SourceId: clubId,
            WeeklyCorrelationId(weekOf, clubId),
            GroundUpkeepTemplate,
            Parameters(("amountMinor", amountMinor), ("places", places)));
    }

    /// <summary>Builds the posting a player's agent makes when the board of a club in administration releases him unsold (`DEBT-4`).</summary>
    /// <param name="entryId">A server-generated entry identity.</param>
    /// <param name="clubId">The club credited.</param>
    /// <param name="listingId">The listing nobody bid on, which the key names.</param>
    /// <param name="amountMinor">The fee, in minor units.</param>
    public static LedgerPosting ReleaseFee(Guid entryId, Guid clubId, Guid listingId, long amountMinor)
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
            ReleaseFeeTemplate,
            Parameters(("amountMinor", amountMinor)));
    }

    /// <summary>The correlation key a matchday's postings for one club share (`FIN-17`).</summary>
    /// <param name="matchdayId">The matchday.</param>
    /// <param name="clubId">The club.</param>
    public static string MatchdayMoneyCorrelationId(Guid matchdayId, Guid clubId) =>
        $"money:{matchdayId:N}:{clubId:N}";

    /// <summary>The correlation key a season's end-of-season postings for one club share (`FIN-17`).</summary>
    /// <param name="seasonId">The season.</param>
    /// <param name="clubId">The club.</param>
    public static string RolloverCorrelationId(Guid seasonId, Guid clubId) =>
        $"rollover:{seasonId:N}:{clubId:N}";

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
