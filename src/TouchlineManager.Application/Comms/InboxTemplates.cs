using System.Text.Json;
using TouchlineManager.Domain.Comms;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Comms;

/// <summary>A message that has not been stored yet: its shelf, its template, and the facts it names.</summary>
/// <param name="Category">The shelf the message sits on.</param>
/// <param name="TemplateKey">The stable template that renders it.</param>
/// <param name="ParametersJson">The template's parameters, as the document that gets stored.</param>
/// <param name="RelatedEntityId">The entity the message is about, or null.</param>
public sealed record InboxDraft(
    InboxCategory Category,
    string TemplateKey,
    string ParametersJson,
    Guid? RelatedEntityId);

/// <summary>
/// The inbox's stable templates and the facts that fill them (master plan §6.9, F-41, master plan §8.6).
/// </summary>
/// <remarks>
/// <para>
/// One file owns every template, so a template key is written and read against the same constant and a
/// message can never be stored with a key nothing renders. The writer stores a key and a JSON parameter
/// document; the reader (<see cref="InboxMessageText"/>) turns them back into English. That is the
/// master-plan §8.6 contract for the commentary applied to the inbox: durable tokens, derived prose, so a
/// message can be re-rendered in another language without rewriting history.
/// </para>
/// <para>
/// The parameter documents are this namespace's own versioned shape. They are implementation detail rather
/// than a port, so they are internal and only tests exercise them through the factory and the renderer.
/// </para>
/// </remarks>
public static class InboxTemplates
{
    /// <summary>A result the club played.</summary>
    public const string ResultRecorded = "inbox.result.recorded";

    /// <summary>The club's league position changed after a round.</summary>
    public const string TableMoved = "inbox.table.moved";

    /// <summary>A player's cards earned a suspension.</summary>
    public const string SuspensionImposed = "inbox.discipline.suspension";

    /// <summary>A player was injured.</summary>
    public const string InjuryReported = "inbox.injury.reported";

    /// <summary>The club's frozen side needed a repair.</summary>
    public const string TeamSheetRepaired = "inbox.team_sheet.repaired";

    /// <summary>The suspension reason for an accumulation of bookings.</summary>
    public const string BookingsReason = "bookings";

    /// <summary>The suspension reason for a sending-off.</summary>
    public const string RedCardReason = "red_card";

    /// <summary>The result code for a win.</summary>
    public const string WinOutcome = "win";

    /// <summary>The result code for a draw.</summary>
    public const string DrawOutcome = "draw";

    /// <summary>The result code for a defeat.</summary>
    public const string LossOutcome = "loss";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the message a club's result in a round produces.</summary>
    /// <param name="roundNumber">The round, 1–34.</param>
    /// <param name="opponentName">The opponent's generated name.</param>
    /// <param name="isHome">Whether the club hosted.</param>
    /// <param name="goalsFor">The club's goals.</param>
    /// <param name="goalsAgainst">The opponent's goals.</param>
    /// <param name="outcome">One of <see cref="WinOutcome"/>, <see cref="DrawOutcome"/>, <see cref="LossOutcome"/>.</param>
    /// <param name="position">The club's league position after the round.</param>
    /// <param name="matchId">The played match, which the message links to.</param>
    public static InboxDraft Result(
        int roundNumber,
        string opponentName,
        bool isHome,
        int goalsFor,
        int goalsAgainst,
        string outcome,
        int position,
        Guid matchId) =>
        Write(
            InboxCategory.Result,
            ResultRecorded,
            new ResultParameters(roundNumber, opponentName, isHome, goalsFor, goalsAgainst, outcome, position),
            matchId);

    /// <summary>Builds the message a club's changed league position produces.</summary>
    /// <param name="roundNumber">The round, 1–34.</param>
    /// <param name="position">The club's new position.</param>
    /// <param name="previousPosition">The position it held before the round.</param>
    public static InboxDraft Table(int roundNumber, int position, int previousPosition) =>
        Write(
            InboxCategory.Table,
            TableMoved,
            new TableParameters(roundNumber, position, previousPosition),
            relatedEntityId: null);

    /// <summary>Builds the message a player's suspension produces.</summary>
    /// <param name="playerName">The suspended player.</param>
    /// <param name="reasons">Why, each one <see cref="BookingsReason"/> or <see cref="RedCardReason"/>.</param>
    /// <param name="fixtures">How many fixtures the suspension covers.</param>
    /// <param name="playerId">The player, which the message links to.</param>
    public static InboxDraft Suspension(
        string playerName,
        IReadOnlyList<string> reasons,
        int fixtures,
        Guid playerId) =>
        Write(
            InboxCategory.Discipline,
            SuspensionImposed,
            new SuspensionParameters(playerName, reasons, fixtures),
            playerId);

    /// <summary>Builds the message an injury produces.</summary>
    /// <param name="playerName">The injured player.</param>
    /// <param name="fixtures">How many fixtures the injury rules them out for.</param>
    /// <param name="severity">The injury's band, as an <see cref="InjurySeverity"/> code.</param>
    /// <param name="playerId">The player, which the message links to.</param>
    public static InboxDraft Injury(string playerName, int fixtures, InjurySeverity severity, Guid playerId) =>
        Write(
            InboxCategory.Injury,
            InjuryReported,
            new InjuryParameters(playerName, fixtures, severity.ToCode()),
            playerId);

    /// <summary>Builds the message a repaired side produces (`DIS-7`).</summary>
    /// <param name="roundNumber">The round the side was frozen for.</param>
    /// <param name="repairs">The decisions the builder made, in slot order.</param>
    /// <param name="fixtureId">The fixture, which the message links to.</param>
    public static InboxDraft Repair(
        int roundNumber,
        IReadOnlyList<RepairParameter> repairs,
        Guid fixtureId) =>
        Write(
            InboxCategory.Squad,
            TeamSheetRepaired,
            new TeamSheetParameters(roundNumber, repairs),
            fixtureId);

    /// <summary>One repair a manager's side needed, as the message names it.</summary>
    /// <param name="SlotNumber">The slot the decision was made in, 1–18.</param>
    /// <param name="Reason">The reason, as a <c>SnapshotRepairReasons</c> code.</param>
    /// <param name="ReplacementName">The player who took the slot, or null when it was left empty.</param>
    public sealed record RepairParameter(int SlotNumber, string Reason, string? ReplacementName);

    private static InboxDraft Write<TParameters>(
        InboxCategory category,
        string templateKey,
        TParameters parameters,
        Guid? relatedEntityId) =>
        new(category, templateKey, JsonSerializer.Serialize(parameters, Options), relatedEntityId);

    /// <summary>Reads a stored parameter document back.</summary>
    internal static TParameters Read<TParameters>(string parametersJson) =>
        JsonSerializer.Deserialize<TParameters>(parametersJson, Options)
            ?? throw new InvalidOperationException("A stored inbox message carries no parameters.");

    internal sealed record ResultParameters(
        int RoundNumber,
        string OpponentName,
        bool IsHome,
        int GoalsFor,
        int GoalsAgainst,
        string Outcome,
        int Position);

    internal sealed record TableParameters(int RoundNumber, int Position, int PreviousPosition);

    internal sealed record SuspensionParameters(string PlayerName, IReadOnlyList<string> Reasons, int Fixtures);

    internal sealed record InjuryParameters(string PlayerName, int Fixtures, string Severity);

    internal sealed record TeamSheetParameters(int RoundNumber, IReadOnlyList<RepairParameter> Repairs);
}
