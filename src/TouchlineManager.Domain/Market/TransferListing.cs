namespace TouchlineManager.Domain.Market;

/// <summary>The lifecycle state of a transfer listing (`TRF-1`, `TRF-14`, `TRF-15`).</summary>
public enum ListingStatus
{
    /// <summary>Accepting bids. A player has at most one of these (`TRF-14`).</summary>
    Open = 0,

    /// <summary>Resolved with a winner, whose bid became a transfer (`TRF-10`).</summary>
    Sold = 1,

    /// <summary>Withdrawn by the seller before resolution, releasing every reservation (`TRF-15`).</summary>
    Cancelled = 2,

    /// <summary>Reached its end with no valid bid (`TRF-11`).</summary>
    Expired = 3,
}

/// <summary>Stable codes and storage representation for <see cref="ListingStatus"/>.</summary>
public static class ListingStatuses
{
    /// <summary>The code for <see cref="ListingStatus.Open"/>.</summary>
    public const string OpenCode = "open";

    /// <summary>The code for <see cref="ListingStatus.Sold"/>.</summary>
    public const string SoldCode = "sold";

    /// <summary>The code for <see cref="ListingStatus.Cancelled"/>.</summary>
    public const string CancelledCode = "cancelled";

    /// <summary>The code for <see cref="ListingStatus.Expired"/>.</summary>
    public const string ExpiredCode = "expired";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 9;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The listing status.</param>
    public static string ToCode(this ListingStatus status) => status switch
    {
        ListingStatus.Open => OpenCode,
        ListingStatus.Sold => SoldCode,
        ListingStatus.Cancelled => CancelledCode,
        ListingStatus.Expired => ExpiredCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown listing status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static ListingStatus FromCode(string code) => code switch
    {
        OpenCode => ListingStatus.Open,
        SoldCode => ListingStatus.Sold,
        CancelledCode => ListingStatus.Cancelled,
        ExpiredCode => ListingStatus.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown listing status code."),
    };

    /// <summary>Tries to parse a stable code, so a filter can reject an unknown value rather than throw.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="status">The parsed status, when the code is known.</param>
    public static bool TryFromCode(string code, out ListingStatus status)
    {
        switch (code)
        {
            case OpenCode:
                status = ListingStatus.Open;
                return true;
            case SoldCode:
                status = ListingStatus.Sold;
                return true;
            case CancelledCode:
                status = ListingStatus.Cancelled;
                return true;
            case ExpiredCode:
                status = ListingStatus.Expired;
                return true;
            default:
                status = ListingStatus.Open;
                return false;
        }
    }
}

/// <summary>
/// A player offered for sale at a minimum fee, resolving at a fixed daily window (`TRF-1`, `TRF-2`).
/// </summary>
/// <remarks>
/// <para>
/// The listing carries the buyer's terms — the wage and contract length the successful bidder will sign —
/// because they must be shown before a bid is placed (`CON-5`). They are computed once when the listing
/// opens so the terms a manager sees cannot drift while the auction runs.
/// </para>
/// <para>
/// "At most one open listing per player" (`TRF-14`) is a partial unique index in the database; the aggregate
/// enforces that a fee is positive and that a listing resolves after it opens (`TRF-1`, `TRF-2`).
/// </para>
/// </remarks>
public sealed class TransferListing
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TransferListing()
    {
    }

    /// <summary>Gets the listing identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the listed player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club selling the player.</summary>
    public Guid SellerClubId { get; private set; }

    /// <summary>Gets the minimum fee the seller will accept, in minor units (`TRF-1`).</summary>
    public long MinimumFeeMinor { get; private set; }

    /// <summary>Gets the weekly wage the buyer will sign the player on, in minor units (`CON-5`).</summary>
    public long GeneratedBuyerWageMinor { get; private set; }

    /// <summary>Gets the length the buyer's contract will run, in game seasons (`CON-1`, `CON-5`).</summary>
    public int GeneratedContractSeasons { get; private set; }

    /// <summary>Gets when the listing opened.</summary>
    public DateTimeOffset OpensAt { get; private set; }

    /// <summary>Gets when the listing resolves (`TRF-2`).</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public ListingStatus Status { get; private set; }

    /// <summary>Gets the idempotency key the opening request carried, which a retry is matched against (`T-4`).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Gets when the listing was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the listing was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets whether the listing is still accepting bids. A player has at most one of these (`TRF-14`).</summary>
    public bool IsOpen => Status == ListingStatus.Open;

    /// <summary>Opens a listing for an eligible player (`TRF-1`, `TRF-2`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="playerId">The listed player.</param>
    /// <param name="sellerClubId">The selling club.</param>
    /// <param name="minimumFeeMinor">The minimum fee, which must be positive (`TRF-1`).</param>
    /// <param name="generatedBuyerWageMinor">The weekly wage the buyer will sign, in minor units.</param>
    /// <param name="generatedContractSeasons">The buyer's contract length, 1–3 game seasons (`CON-1`).</param>
    /// <param name="opensAt">When the listing opens.</param>
    /// <param name="endsAt">When the listing resolves, which must be after it opens (`TRF-2`).</param>
    /// <param name="idempotencyKey">The opening request's idempotency key, or null.</param>
    /// <param name="now">The current instant.</param>
    public static TransferListing Open(
        Guid id,
        Guid playerId,
        Guid sellerClubId,
        long minimumFeeMinor,
        long generatedBuyerWageMinor,
        int generatedContractSeasons,
        DateTimeOffset opensAt,
        DateTimeOffset endsAt,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumFeeMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(generatedBuyerWageMinor);

        if (generatedContractSeasons is < Rules.WorldRuleSet.ContractMinSeasons or > Rules.WorldRuleSet.ContractMaxSeasons)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generatedContractSeasons),
                generatedContractSeasons,
                $"A contract is between {Rules.WorldRuleSet.ContractMinSeasons} and {Rules.WorldRuleSet.ContractMaxSeasons} game seasons (CON-1).");
        }

        if (endsAt <= opensAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt),
                endsAt,
                "A listing resolves after it opens (TRF-2).");
        }

        return new TransferListing
        {
            Id = id,
            PlayerId = playerId,
            SellerClubId = sellerClubId,
            MinimumFeeMinor = minimumFeeMinor,
            GeneratedBuyerWageMinor = generatedBuyerWageMinor,
            GeneratedContractSeasons = generatedContractSeasons,
            OpensAt = opensAt,
            EndsAt = endsAt,
            Status = ListingStatus.Open,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Marks the listing resolved with a winner (`TRF-10`).</summary>
    /// <param name="now">The current instant.</param>
    public void MarkSold(DateTimeOffset now) => Transition(ListingStatus.Sold, now);

    /// <summary>Ends the listing with no valid bid (`TRF-11`).</summary>
    /// <param name="now">The current instant.</param>
    public void Expire(DateTimeOffset now) => Transition(ListingStatus.Expired, now);

    /// <summary>Withdraws the listing, releasing every reservation (`TRF-15`).</summary>
    /// <param name="now">The current instant.</param>
    public void Cancel(DateTimeOffset now) => Transition(ListingStatus.Cancelled, now);

    private void Transition(ListingStatus status, DateTimeOffset now)
    {
        if (Status != ListingStatus.Open)
        {
            throw new InvalidOperationException($"A listing cannot leave '{Status}' because it is not open.");
        }

        Status = status;

        UpdatedAt = now;
        Version++;
    }
}
