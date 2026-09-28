namespace TouchlineManager.Domain.Finance;

/// <summary>
/// What a ledger entry is: the reason money or a reservation moved (master plan §6.8, game rules §13).
/// </summary>
/// <remarks>
/// A category is the durable answer to "why did this balance change", so it is a stable code rather than an
/// ordinal: a client groups and filters by it, and a reordered enum must not move an entry between
/// accounts. The values mirror the income and expense sources `FIN-3`…`FIN-9` name, plus the reservation
/// movements `FIN-10` makes and the two deliberate repairs the rules describe — an emergency grant
/// (`FIN-16`) and a compensating correction (`FIN-12`).
/// </remarks>
public enum LedgerCategory
{
    /// <summary>The club's account was opened with its starting balance (`FIN-1`).</summary>
    OpeningBalance = 0,

    /// <summary>Home-match gate revenue (`FIN-3`).</summary>
    GateReceipt = 1,

    /// <summary>The weekly sponsorship credit (`FIN-4`).</summary>
    Sponsorship = 2,

    /// <summary>The weekly player wages (`FIN-7`).</summary>
    Wages = 3,

    /// <summary>The small fixed weekly operating cost (`FIN-9`).</summary>
    OperatingCost = 4,

    /// <summary>A promotion or final-position award (`FIN-5`).</summary>
    PositionAward = 5,

    /// <summary>A transfer fee the buying club paid (`FIN-8`).</summary>
    TransferPayment = 6,

    /// <summary>A transfer fee the selling club received (`FIN-6`).</summary>
    TransferProceeds = 7,

    /// <summary>Funds committed to a leading bid (`FIN-10`, `TRF-7`).</summary>
    BidReservation = 8,

    /// <summary>Funds freed when a bid was outbid, cancelled, or lost (`TRF-7`, `TRF-15`).</summary>
    ReservationRelease = 9,

    /// <summary>A logged emergency grant that preserved competition integrity (`FIN-16`).</summary>
    EmergencyGrant = 10,

    /// <summary>A compensating entry that corrects an earlier one; balances are never edited (`FIN-12`).</summary>
    Compensation = 11,
}

/// <summary>Stable codes and parsing for <see cref="LedgerCategory"/>.</summary>
public static class LedgerCategories
{
    /// <summary>The longest code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 19;

    /// <summary>Every category, in declaration order.</summary>
    public static readonly IReadOnlyList<LedgerCategory> All = [.. Enum.GetValues<LedgerCategory>()];

    /// <summary>Converts a category to its stable code.</summary>
    /// <param name="category">The category.</param>
    public static string ToCode(this LedgerCategory category) => category switch
    {
        LedgerCategory.OpeningBalance => "opening_balance",
        LedgerCategory.GateReceipt => "gate_receipt",
        LedgerCategory.Sponsorship => "sponsorship",
        LedgerCategory.Wages => "wages",
        LedgerCategory.OperatingCost => "operating_cost",
        LedgerCategory.PositionAward => "position_award",
        LedgerCategory.TransferPayment => "transfer_payment",
        LedgerCategory.TransferProceeds => "transfer_proceeds",
        LedgerCategory.BidReservation => "bid_reservation",
        LedgerCategory.ReservationRelease => "reservation_release",
        LedgerCategory.EmergencyGrant => "emergency_grant",
        LedgerCategory.Compensation => "compensation",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown ledger category."),
    };

    /// <summary>Parses a stable code back to its category.</summary>
    /// <param name="code">The stable code.</param>
    public static LedgerCategory FromCode(string code) => code switch
    {
        "opening_balance" => LedgerCategory.OpeningBalance,
        "gate_receipt" => LedgerCategory.GateReceipt,
        "sponsorship" => LedgerCategory.Sponsorship,
        "wages" => LedgerCategory.Wages,
        "operating_cost" => LedgerCategory.OperatingCost,
        "position_award" => LedgerCategory.PositionAward,
        "transfer_payment" => LedgerCategory.TransferPayment,
        "transfer_proceeds" => LedgerCategory.TransferProceeds,
        "bid_reservation" => LedgerCategory.BidReservation,
        "reservation_release" => LedgerCategory.ReservationRelease,
        "emergency_grant" => LedgerCategory.EmergencyGrant,
        "compensation" => LedgerCategory.Compensation,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown ledger category code."),
    };
}
