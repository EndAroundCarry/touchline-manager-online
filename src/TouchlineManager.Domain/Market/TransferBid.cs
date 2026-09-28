namespace TouchlineManager.Domain.Market;

/// <summary>The lifecycle state of a transfer bid (`TRF-4`, `TRF-6`, `TRF-8`).</summary>
public enum BidStatus
{
    /// <summary>The highest valid bid on an open listing. A club has at most one of these per listing (`TRF-6`).</summary>
    Leading = 0,

    /// <summary>A higher valid bid replaced it, and its reservation was released (`TRF-7`).</summary>
    Outbid = 1,

    /// <summary>It won the listing and settled as a transfer (`TRF-10`).</summary>
    Won = 2,

    /// <summary>The listing was cancelled or another bid won, and its reservation was released (`TRF-15`).</summary>
    Released = 3,

    /// <summary>It was skipped at resolution because it failed a revalidated invariant (`TRF-11`).</summary>
    Invalid = 4,
}

/// <summary>Stable codes and storage representation for <see cref="BidStatus"/>.</summary>
public static class BidStatuses
{
    /// <summary>The code for <see cref="BidStatus.Leading"/>.</summary>
    public const string LeadingCode = "leading";

    /// <summary>The code for <see cref="BidStatus.Outbid"/>.</summary>
    public const string OutbidCode = "outbid";

    /// <summary>The code for <see cref="BidStatus.Won"/>.</summary>
    public const string WonCode = "won";

    /// <summary>The code for <see cref="BidStatus.Released"/>.</summary>
    public const string ReleasedCode = "released";

    /// <summary>The code for <see cref="BidStatus.Invalid"/>.</summary>
    public const string InvalidCode = "invalid";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 8;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The bid status.</param>
    public static string ToCode(this BidStatus status) => status switch
    {
        BidStatus.Leading => LeadingCode,
        BidStatus.Outbid => OutbidCode,
        BidStatus.Won => WonCode,
        BidStatus.Released => ReleasedCode,
        BidStatus.Invalid => InvalidCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown bid status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static BidStatus FromCode(string code) => code switch
    {
        LeadingCode => BidStatus.Leading,
        OutbidCode => BidStatus.Outbid,
        WonCode => BidStatus.Won,
        ReleasedCode => BidStatus.Released,
        InvalidCode => BidStatus.Invalid,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown bid status code."),
    };
}

/// <summary>
/// One club's ascending offer on a listing, whose leading amount holds a reservation (`TRF-4`, `TRF-7`).
/// </summary>
/// <remarks>
/// <para>
/// A club has at most one active bid per listing and raises it rather than adding a second (`TRF-6`); the
/// partial unique index enforces the "at most one" and the aggregate enforces that an amount is positive and
/// that only a leading bid can be outbid, won, or released.
/// </para>
/// <para>
/// <see cref="BidSequence"/> is assigned by the database when the row is inserted, so a tie between two equal
/// amounts resolves to the bid committed first and never to arrival time the server observed (`TRF-8`, `T-5`).
/// </para>
/// </remarks>
public sealed class TransferBid
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TransferBid()
    {
    }

    /// <summary>Gets the bid identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the listing bid on.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>Gets the club bidding.</summary>
    public Guid BidderClubId { get; private set; }

    /// <summary>Gets the amount, in minor units (`TRF-4`).</summary>
    public long AmountMinor { get; private set; }

    /// <summary>Gets the database-assigned sequence used to break ties (`TRF-8`).</summary>
    public long BidSequence { get; private set; }

    /// <summary>Gets when the bid was placed.</summary>
    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public BidStatus Status { get; private set; }

    /// <summary>Gets the ledger correlation key of the reservation this bid holds (`FIN-17`).</summary>
    public string ReservationCorrelationId { get; private set; } = string.Empty;

    /// <summary>Gets the idempotency key the placing request carried, which a retry is matched against (`T-4`).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Gets when the bid was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the bid was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets whether the bid is leading, and so holds a reservation. A club has at most one per listing.</summary>
    public bool IsLeading => Status == BidStatus.Leading;

    /// <summary>Gets the ordering key that decides which of several bids wins (`TRF-8`).</summary>
    public BidOrderKey OrderKey => new(AmountMinor, BidSequence, Id);

    /// <summary>Places a bid on a listing, reserving its amount (`TRF-4`, `TRF-7`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="listingId">The listing bid on.</param>
    /// <param name="bidderClubId">The club bidding.</param>
    /// <param name="amountMinor">The amount, which must be positive.</param>
    /// <param name="reservationCorrelationId">The ledger correlation key of the reservation.</param>
    /// <param name="idempotencyKey">The placing request's idempotency key, or null.</param>
    /// <param name="now">The current instant.</param>
    public static TransferBid Place(
        Guid id,
        Guid listingId,
        Guid bidderClubId,
        long amountMinor,
        string reservationCorrelationId,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationCorrelationId);

        return new TransferBid
        {
            Id = id,
            ListingId = listingId,
            BidderClubId = bidderClubId,
            AmountMinor = amountMinor,
            PlacedAt = now,
            Status = BidStatus.Leading,
            ReservationCorrelationId = reservationCorrelationId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Raises the bid's amount, keeping it leading (`TRF-6`).</summary>
    /// <param name="amountMinor">The new, higher amount.</param>
    /// <param name="idempotencyKey">The raising request's idempotency key, or null.</param>
    /// <param name="now">The current instant.</param>
    public void Raise(long amountMinor, string? idempotencyKey, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);

        if (!IsLeading)
        {
            throw new InvalidOperationException("Only a leading bid can be raised (TRF-6).");
        }

        AmountMinor = amountMinor;
        IdempotencyKey = idempotencyKey;

        Touch(now);
    }

    /// <summary>Marks the bid outbid, once its reservation has been released (`TRF-7`).</summary>
    /// <param name="now">The current instant.</param>
    public void Outbid(DateTimeOffset now) => Move(BidStatus.Outbid, now);

    /// <summary>Marks the bid the winner of its listing (`TRF-10`).</summary>
    /// <param name="now">The current instant.</param>
    public void Win(DateTimeOffset now) => Move(BidStatus.Won, now);

    /// <summary>Marks the bid released, once its reservation has been released (`TRF-11`, `TRF-15`).</summary>
    /// <param name="now">The current instant.</param>
    public void Release(DateTimeOffset now) => Move(BidStatus.Released, now);

    /// <summary>Marks the bid skipped at resolution because it failed a revalidated invariant (`TRF-11`).</summary>
    /// <param name="now">The current instant.</param>
    public void Invalidate(DateTimeOffset now) => Move(BidStatus.Invalid, now);

    private void Move(BidStatus status, DateTimeOffset now)
    {
        if (!IsLeading)
        {
            throw new InvalidOperationException($"A bid in '{Status}' cannot move again.");
        }

        Status = status;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>
/// The ordering that decides which bid wins a listing (`TRF-8`).
/// </summary>
/// <remarks>
/// The highest amount wins; equal amounts resolve to the lowest database-assigned sequence, and only then to
/// the immutable identity. Sorting ascending therefore puts the winner first, which is the order the
/// resolution query and the in-memory revalidation both use.
/// </remarks>
/// <param name="AmountMinor">The bid's amount.</param>
/// <param name="BidSequence">The database-assigned sequence.</param>
/// <param name="Id">The immutable bid identity.</param>
public readonly record struct BidOrderKey(long AmountMinor, long BidSequence, Guid Id) : IComparable<BidOrderKey>
{
    /// <summary>Reports whether one key sorts before another.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    public static bool operator <(BidOrderKey left, BidOrderKey right) => left.CompareTo(right) < 0;

    /// <summary>Reports whether one key sorts before or level with another.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    public static bool operator <=(BidOrderKey left, BidOrderKey right) => left.CompareTo(right) <= 0;

    /// <summary>Reports whether one key sorts after another.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    public static bool operator >(BidOrderKey left, BidOrderKey right) => left.CompareTo(right) > 0;

    /// <summary>Reports whether one key sorts after or level with another.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    public static bool operator >=(BidOrderKey left, BidOrderKey right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public int CompareTo(BidOrderKey other)
    {
        var byAmount = other.AmountMinor.CompareTo(AmountMinor);

        if (byAmount != 0)
        {
            return byAmount;
        }

        var bySequence = BidSequence.CompareTo(other.BidSequence);

        return bySequence != 0 ? bySequence : Id.CompareTo(other.Id);
    }
}
