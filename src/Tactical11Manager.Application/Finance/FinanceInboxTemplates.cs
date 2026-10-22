using System.Text.Json;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Domain.Comms;

namespace Tactical11Manager.Application.Finance;

/// <summary>
/// The inbox templates the club's money produces: going into the red, administration, the board's listings and the manager's dismissal
/// (`DEBT-2`…`DEBT-5`).
/// </summary>
/// <remarks>
/// The same durable-token contract the rest of the inbox uses (`MAT-8`): a stable key and a parameter document, rendered by
/// <see cref="InboxMessageText"/>. They sit on the squad shelf, as the market's do, because the MVP does not add a shelf for them.
/// </remarks>
public static class FinanceInboxTemplates
{
    /// <summary>The club's cash fell below zero (`DEBT-2`).</summary>
    public const string InTheRed = "inbox.finance.in_the_red";

    /// <summary>The club's cash came back to zero or above (`DEBT-2`).</summary>
    public const string BackInTheBlack = "inbox.finance.back_in_the_black";

    /// <summary>The club's cash fell past its overdraft limit (`DEBT-2`).</summary>
    public const string AdministrationEntered = "inbox.finance.administration_entered";

    /// <summary>The club came back inside its overdraft limit (`DEBT-2`).</summary>
    public const string AdministrationLeft = "inbox.finance.administration_left";

    /// <summary>The board put players on the market while the club is in administration (`DEBT-4`).</summary>
    public const string BoardListedPlayers = "inbox.finance.board_listed";

    /// <summary>The board released a player nobody bought (`DEBT-4`).</summary>
    public const string BoardReleasedPlayer = "inbox.finance.board_released";

    /// <summary>A leading bid was withdrawn because the club needed the money to pay its wages (`FIN-10`).</summary>
    public const string BidWithdrawn = "inbox.finance.bid_withdrawn";

    /// <summary>The manager was dismissed after the club stayed in administration (`DEBT-5`).</summary>
    public const string ManagerDismissed = "inbox.finance.manager_dismissed";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the message a club going into the red produces.</summary>
    /// <param name="cashMinor">The club's cash, below zero.</param>
    /// <param name="overdraftLimitMinor">How far into the red the club may go before administration.</param>
    public static InboxDraft InTheRedMessage(long cashMinor, long overdraftLimitMinor) =>
        Write(InTheRed, new RedParameters(cashMinor, overdraftLimitMinor), relatedEntityId: null);

    /// <summary>Builds the message a club coming back to zero produces.</summary>
    /// <param name="cashMinor">The club's cash.</param>
    public static InboxDraft BackInTheBlackMessage(long cashMinor) =>
        Write(BackInTheBlack, new CashParameters(cashMinor), relatedEntityId: null);

    /// <summary>Builds the message a club going into administration produces.</summary>
    /// <param name="cashMinor">The club's cash, below minus the overdraft limit.</param>
    /// <param name="overdraftLimitMinor">The overdraft limit.</param>
    /// <param name="matchdaysLeft">How many matchdays the club has before its manager is dismissed.</param>
    public static InboxDraft AdministrationEnteredMessage(long cashMinor, long overdraftLimitMinor, int matchdaysLeft) =>
        Write(AdministrationEntered, new AdministrationParameters(cashMinor, overdraftLimitMinor, matchdaysLeft), relatedEntityId: null);

    /// <summary>Builds the message a club leaving administration produces.</summary>
    /// <param name="cashMinor">The club's cash.</param>
    public static InboxDraft AdministrationLeftMessage(long cashMinor) =>
        Write(AdministrationLeft, new CashParameters(cashMinor), relatedEntityId: null);

    /// <summary>Builds the message the board's listings produce.</summary>
    /// <param name="playerNames">The players listed.</param>
    /// <param name="matchdaysLeft">How many matchdays the club has before its manager is dismissed.</param>
    public static InboxDraft BoardListedMessage(IReadOnlyList<string> playerNames, int matchdaysLeft) =>
        Write(BoardListedPlayers, new BoardListedParameters(playerNames, matchdaysLeft), relatedEntityId: null);

    /// <summary>Builds the message the board's release of an unsold player produces.</summary>
    /// <param name="playerName">The player released.</param>
    /// <param name="feeMinor">What his agent paid the club.</param>
    /// <param name="playerId">The player, which the message links to.</param>
    public static InboxDraft BoardReleasedMessage(string playerName, long feeMinor, Guid playerId) =>
        Write(BoardReleasedPlayer, new BoardReleasedParameters(playerName, feeMinor), playerId);

    /// <summary>Builds the message a withdrawn bid produces.</summary>
    /// <param name="playerName">The player the club was bidding for.</param>
    /// <param name="amountMinor">The bid, which is free again.</param>
    /// <param name="listingId">The listing, which the message links to.</param>
    public static InboxDraft BidWithdrawnMessage(string playerName, long amountMinor, Guid listingId) =>
        Write(BidWithdrawn, new BidWithdrawnParameters(playerName, amountMinor), listingId);

    /// <summary>Builds the message a manager's dismissal produces.</summary>
    /// <param name="clubName">The club.</param>
    /// <param name="matchdays">How many matchdays the club stayed in administration.</param>
    public static InboxDraft ManagerDismissedMessage(string clubName, int matchdays) =>
        Write(ManagerDismissed, new DismissedParameters(clubName, matchdays), relatedEntityId: null);

    /// <summary>Reads a stored parameter document back.</summary>
    internal static TParameters Read<TParameters>(string parametersJson) =>
        JsonSerializer.Deserialize<TParameters>(parametersJson, Options)
        ?? throw new InvalidOperationException("A stored inbox message carries no parameters.");

    internal sealed record RedParameters(long CashMinor, long OverdraftLimitMinor);

    internal sealed record CashParameters(long CashMinor);

    internal sealed record AdministrationParameters(long CashMinor, long OverdraftLimitMinor, int MatchdaysLeft);

    internal sealed record BoardListedParameters(IReadOnlyList<string> PlayerNames, int MatchdaysLeft);

    internal sealed record BoardReleasedParameters(string PlayerName, long FeeMinor);

    internal sealed record BidWithdrawnParameters(string PlayerName, long AmountMinor);

    internal sealed record DismissedParameters(string ClubName, int Matchdays);

    private static InboxDraft Write<TParameters>(string templateKey, TParameters parameters, Guid? relatedEntityId) =>
        new(
            InboxCategory.Squad,
            templateKey,
            JsonSerializer.Serialize(parameters, Options),
            relatedEntityId);
}
