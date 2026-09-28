namespace TouchlineManager.Domain.Market;

/// <summary>What an AI club did in one market evaluation (`TRF-12`).</summary>
public enum AiMarketAction
{
    /// <summary>The club put a surplus player up for sale.</summary>
    Listed = 0,

    /// <summary>The club placed a bid on an open listing.</summary>
    Bid = 1,
}

/// <summary>Stable codes and storage representation for <see cref="AiMarketAction"/>.</summary>
/// <remarks>
/// Codes rather than ordinals, so a reordered enum cannot move a recorded decision between shelves and a
/// column holds every value the type can produce.
/// </remarks>
public static class AiMarketActions
{
    /// <summary>The code for <see cref="AiMarketAction.Listed"/>.</summary>
    public const string ListedCode = "listed";

    /// <summary>The code for <see cref="AiMarketAction.Bid"/>.</summary>
    public const string BidCode = "bid";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 6;

    /// <summary>Converts an action to its stable code.</summary>
    /// <param name="action">The action.</param>
    public static string ToCode(this AiMarketAction action) => action switch
    {
        AiMarketAction.Listed => ListedCode,
        AiMarketAction.Bid => BidCode,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown AI market action."),
    };

    /// <summary>Parses a stable code back to its action.</summary>
    /// <param name="code">The stable code.</param>
    public static AiMarketAction FromCode(string code) => code switch
    {
        ListedCode => AiMarketAction.Listed,
        BidCode => AiMarketAction.Bid,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown AI market action code."),
    };

    /// <summary>Reports whether a code is one the type recognises.</summary>
    /// <param name="code">The stable code.</param>
    public static bool IsKnown(string code) => code is ListedCode or BidCode;
}

/// <summary>
/// One decision an AI club's policy made, kept as an append-only record (`TRF-12`, master plan §6.7).
/// </summary>
/// <remarks>
/// <para>
/// The market's own account of what the AI did and why: the club, the instant, the action, the player, the
/// listing or bid it produced, the digest of the inputs the policy read, and the policy version. It is what
/// an operator or a future collusion review reads to answer "where did this listing come from", and it is
/// the reason the AI's market behaviour is auditable rather than merely observable in its effects.
/// </para>
/// <para>
/// Written once and never mutated. The action decides which of the two resulting identities is set, so a
/// row always names exactly the listing or the bid it is about.
/// </para>
/// </remarks>
public sealed class AiMarketDecision
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private AiMarketDecision()
    {
    }

    /// <summary>Gets the decision identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club whose policy decided.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the instant the evaluation ran.</summary>
    public DateTimeOffset EvaluatedAt { get; private set; }

    /// <summary>Gets the action the club took.</summary>
    public AiMarketAction Action { get; private set; }

    /// <summary>Gets the player the decision concerns.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the listing the decision produced, when the club listed a player, or null.</summary>
    public Guid? ListingId { get; private set; }

    /// <summary>Gets the bid the decision produced, when the club bid, or null.</summary>
    public Guid? BidId { get; private set; }

    /// <summary>Gets the digest of the inputs the policy decided from (`ai-market-v1`).</summary>
    public string InputsHash { get; private set; } = string.Empty;

    /// <summary>Gets the policy version that made the decision.</summary>
    public string PolicyVersion { get; private set; } = string.Empty;

    /// <summary>Gets when the decision was recorded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a decision the policy made and the writer carried out.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club whose policy decided.</param>
    /// <param name="evaluatedAt">The instant the evaluation ran.</param>
    /// <param name="action">The action taken.</param>
    /// <param name="playerId">The player concerned.</param>
    /// <param name="listingId">The listing produced, for a listing action.</param>
    /// <param name="bidId">The bid produced, for a bid action.</param>
    /// <param name="inputsHash">The digest of the inputs the policy decided from.</param>
    /// <param name="policyVersion">The policy version that made the decision.</param>
    /// <param name="now">The current instant.</param>
    public static AiMarketDecision Record(
        Guid id,
        Guid clubId,
        DateTimeOffset evaluatedAt,
        AiMarketAction action,
        Guid playerId,
        Guid? listingId,
        Guid? bidId,
        string inputsHash,
        string policyVersion,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputsHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);

        if (action == AiMarketAction.Listed && listingId is null)
        {
            throw new ArgumentException("A listing decision names the listing it produced (TRF-12).", nameof(listingId));
        }

        if (action == AiMarketAction.Bid && bidId is null)
        {
            throw new ArgumentException("A bid decision names the bid it produced (TRF-12).", nameof(bidId));
        }

        if (action == AiMarketAction.Listed && bidId is not null)
        {
            throw new ArgumentException("A listing decision names no bid (TRF-12).", nameof(bidId));
        }

        if (action == AiMarketAction.Bid && listingId is not null)
        {
            throw new ArgumentException("A bid decision names no listing (TRF-12).", nameof(listingId));
        }

        return new AiMarketDecision
        {
            Id = id,
            ClubId = clubId,
            EvaluatedAt = evaluatedAt,
            Action = action,
            PlayerId = playerId,
            ListingId = listingId,
            BidId = bidId,
            InputsHash = inputsHash,
            PolicyVersion = policyVersion,
            CreatedAt = now,
        };
    }
}
