using System.Globalization;
using TouchlineManager.Application.Market;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Comms;

/// <summary>The English a stored message renders to: a headline and a sentence (`MAT-8`, master plan §8.6).</summary>
/// <param name="Title">The headline.</param>
/// <param name="Body">The detail.</param>
public sealed record InboxText(string Title, string Body);

/// <summary>
/// Renders a stored inbox message's template and parameters into English (master plan §8.6, F-41).
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="InboxTemplates"/>: it reads the durable key and parameter document and
/// produces the sentence a manager reads, so the stored message never holds prose that a later language
/// change would have to rewrite. It is pure — no clock, database, or culture-sensitive formatting — so the
/// same message renders the same text on every host.
/// </para>
/// <para>
/// A key it does not know, or a document it cannot read, is refused by name rather than rendered as a vague
/// placeholder: the writer and the reader share one set of constants, so either is a defect worth seeing
/// rather than a message worth hiding.
/// </para>
/// </remarks>
public static class InboxMessageText
{
    /// <summary>Renders one message.</summary>
    /// <param name="templateKey">The stored template key.</param>
    /// <param name="parametersJson">The stored parameter document.</param>
    /// <exception cref="InvalidOperationException">When the key is not one this build renders.</exception>
    public static InboxText Render(string templateKey, string parametersJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);

        return templateKey switch
        {
            InboxTemplates.ResultRecorded => Result(
                InboxTemplates.Read<InboxTemplates.ResultParameters>(parametersJson)),
            InboxTemplates.TableMoved => Table(
                InboxTemplates.Read<InboxTemplates.TableParameters>(parametersJson)),
            InboxTemplates.SuspensionImposed => Suspension(
                InboxTemplates.Read<InboxTemplates.SuspensionParameters>(parametersJson)),
            InboxTemplates.InjuryReported => Injury(
                InboxTemplates.Read<InboxTemplates.InjuryParameters>(parametersJson)),
            InboxTemplates.TeamSheetRepaired => RepairedSide(
                InboxTemplates.Read<InboxTemplates.TeamSheetParameters>(parametersJson)),
            InboxTemplates.OnboardingWelcome => Welcome(
                InboxTemplates.Read<InboxTemplates.WelcomeParameters>(parametersJson)),
            InboxTemplates.InactivityWarning => Warned(
                InboxTemplates.Read<InboxTemplates.InactivityParameters>(parametersJson)),
            InboxTemplates.InactivityClosed => ClosedForInactivity(
                InboxTemplates.Read<InboxTemplates.InactivityParameters>(parametersJson)),
            InboxTemplates.TeamSheetReminder => Reminder(
                InboxTemplates.Read<InboxTemplates.ReminderParameters>(parametersJson)),
            MarketInboxTemplates.Outbid => Outbid(
                MarketInboxTemplates.Read<MarketInboxTemplates.OutbidParameters>(parametersJson)),
            MarketInboxTemplates.BidWon => BidWon(
                MarketInboxTemplates.Read<MarketInboxTemplates.BidWonParameters>(parametersJson)),
            MarketInboxTemplates.PlayerSold => PlayerSold(
                MarketInboxTemplates.Read<MarketInboxTemplates.PlayerSoldParameters>(parametersJson)),
            _ => throw new InvalidOperationException($"'{templateKey}' is not an inbox template this build renders."),
        };
    }

    private static InboxText Outbid(MarketInboxTemplates.OutbidParameters parameters) =>
        new(
            "You were outbid",
            $"Your bid for {parameters.PlayerName} was beaten by a higher offer of "
            + $"{Money(parameters.NewAmountMinor)}.");

    private static InboxText BidWon(MarketInboxTemplates.BidWonParameters parameters) =>
        new(
            "Transfer completed",
            $"You signed {parameters.PlayerName} from {parameters.SellerClubName} for "
            + $"{Money(parameters.FeeMinor)}.");

    private static InboxText PlayerSold(MarketInboxTemplates.PlayerSoldParameters parameters) =>
        new(
            "Player sold",
            $"{parameters.PlayerName} has joined {parameters.BuyerClubName} for {Money(parameters.FeeMinor)}.");

    private static string Money(long minorUnits) =>
        minorUnits.ToString("N0", CultureInfo.InvariantCulture);

    private static InboxText Result(InboxTemplates.ResultParameters parameters)
    {
        var verb = parameters.Outcome switch
        {
            InboxTemplates.WinOutcome => "won",
            InboxTemplates.LossOutcome => "lost",
            _ => "drew",
        };

        var venue = parameters.IsHome
            ? $"At home to {parameters.OpponentName}"
            : $"Away to {parameters.OpponentName}";

        var score = string.Create(
            CultureInfo.InvariantCulture,
            $"{parameters.GoalsFor}\u2013{parameters.GoalsAgainst}");

        return new InboxText(
            $"Round {parameters.RoundNumber}: {verb} {score}",
            $"{venue}. You are {Ordinal(parameters.Position)} in the table after round {parameters.RoundNumber}.");
    }

    private static InboxText Table(InboxTemplates.TableParameters parameters)
    {
        var direction = parameters.Position < parameters.PreviousPosition ? "up" : "down";

        return new InboxText(
            $"You are {Ordinal(parameters.Position)}",
            $"After round {parameters.RoundNumber} you moved {direction} to {Ordinal(parameters.Position)} "
            + $"from {Ordinal(parameters.PreviousPosition)}.");
    }

    private static InboxText Suspension(InboxTemplates.SuspensionParameters parameters)
    {
        var reasons = parameters.Reasons.Select(Reason).ToList();
        var clause = Join(reasons);

        return new InboxText(
            $"{parameters.PlayerName} is suspended",
            $"{parameters.PlayerName} misses {Fixtures(parameters.Fixtures)} after {clause}.");
    }

    private static InboxText Injury(InboxTemplates.InjuryParameters parameters) =>
        new(
            $"{parameters.PlayerName} is injured",
            $"{parameters.PlayerName} is out for {Fixtures(parameters.Fixtures)} with a "
            + $"{Severity(Unavailabilities.SeverityFromCode(parameters.Severity))} injury.");

    private static InboxText RepairedSide(InboxTemplates.TeamSheetParameters parameters)
    {
        var places = parameters.Repairs.Select(Repair).ToList();

        return new InboxText(
            "Your side was changed",
            $"Before round {parameters.RoundNumber}, these places in your side were decided for you: "
            + $"{string.Join("; ", places)}.");
    }

    private static string Repair(InboxTemplates.RepairParameter repair)
    {
        var reason = SnapshotRepairReasons.FromCode(repair.Reason) switch
        {
            SnapshotRepairReason.SlotEmpty => "it was empty",
            SnapshotRepairReason.PlayerUnavailable => "the player was unavailable",
            SnapshotRepairReason.PlayerIneligible => "the player was no longer eligible",
            SnapshotRepairReason.PlayerDuplicated => "the player was already picked",
            SnapshotRepairReason.GoalkeeperRequired => "a goalkeeper was required",
            _ => "the goalkeeper could not play outfield",
        };

        return repair.ReplacementName is null
            ? $"slot {repair.SlotNumber}: {reason}"
            : $"slot {repair.SlotNumber}: {reason}, {repair.ReplacementName} came in";
    }

    private static string Reason(string reason) => reason switch
    {
        InboxTemplates.RedCardReason => "a sending-off",
        _ => "accumulating bookings",
    };

    private static InboxText Welcome(InboxTemplates.WelcomeParameters parameters) =>
        new(
            $"Welcome to {parameters.ClubName}",
            $"You are now the manager of {parameters.ClubName} in {parameters.DivisionName}. "
            + "Set your tactics and team sheet before the first round.");

    private static InboxText Warned(InboxTemplates.InactivityParameters parameters) =>
        new(
            "We have not seen you in a while",
            $"You have been away for {Days(parameters.DaysInactive)}. Log in to keep full control of your "
            + $"club: after {(int)WorldRuleSet.InactivityAiAssistanceAfter.TotalDays} days the AI steps in, "
            + $"and after {(int)WorldRuleSet.InactivityCloseAfter.TotalDays} days you lose the club.");

    private static InboxText ClosedForInactivity(InboxTemplates.InactivityParameters parameters) =>
        new(
            "Your club has returned to the AI",
            $"After {Days(parameters.DaysInactive)} away from the game, your tenure has ended and your club is "
            + "AI-controlled again. Its squad, finances, and history are unchanged.");

    private static InboxText Reminder(InboxTemplates.ReminderParameters parameters)
    {
        var venue = parameters.IsHome
            ? $"At home to {parameters.OpponentName}"
            : $"Away to {parameters.OpponentName}";

        return new InboxText(
            $"Round {parameters.RoundNumber}: submit your team sheet",
            $"{venue}. Your team sheet locks at {Moment(parameters.LockAt)}.");
    }

    private static string Days(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} day{(count == 1 ? string.Empty : "s")}");

    private static string Moment(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("ddd d MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Severity(InjurySeverity severity) => severity switch
    {
        InjurySeverity.Moderate => "moderate",
        InjurySeverity.Major => "major",
        _ => "minor",
    };

    private static string Fixtures(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} fixture{(count == 1 ? string.Empty : "s")}");

    private static string Join(List<string> clauses) => clauses.Count switch
    {
        0 => "a disciplinary decision",
        1 => clauses[0],
        _ => $"{string.Join(", ", clauses.Take(clauses.Count - 1))} and {clauses[^1]}",
    };

    private static string Ordinal(int value)
    {
        var suffix = (value % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            },
        };

        return string.Create(CultureInfo.InvariantCulture, $"{value}{suffix}");
    }
}
