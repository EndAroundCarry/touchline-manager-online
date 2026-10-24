using System.Text.Json;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Comms;

/// <summary>What a play-off match meant for a club, as its result message names it once the manager asks for the result (`PR-4`, `PR-6`).</summary>
public static class PlayoffFates
{
    /// <summary>A semi-final won: the club is in the final.</summary>
    public const string ToFinal = "to_final";

    /// <summary>A semi-final lost, or a final the challenger lost: the club stays where it was.</summary>
    public const string Out = "out";

    /// <summary>A final won by the challenger: the club goes up.</summary>
    public const string Promoted = "promoted";

    /// <summary>A final won by the holder: the club stays up.</summary>
    public const string StaysUp = "stays_up";

    /// <summary>A final lost by the holder: the club goes down.</summary>
    public const string Relegated = "relegated";

    /// <summary>A final lost by the challenger: the club stays in its league.</summary>
    public const string StaysDown = "stays_down";

    /// <summary>Works out what a decided match meant for one of its clubs.</summary>
    /// <param name="stage">The match's stage.</param>
    /// <param name="isHolder">Whether the club is the higher league's club in the final.</param>
    /// <param name="won">Whether the club won the tie.</param>
    public static string For(PlayoffStage stage, bool isHolder, bool won) => stage switch
    {
        PlayoffStage.SemiFinal => won ? ToFinal : Out,
        _ when isHolder => won ? StaysUp : Relegated,
        _ => won ? Promoted : StaysDown,
    };
}

/// <summary>
/// The inbox templates of the promotion play-off: the draw, and a final that is set (`PR-4`, ADR-0087).
/// </summary>
/// <remarks>
/// A result of a play-off match rides the ordinary result template (<see cref="InboxTemplates.PlayoffResult"/>), because it
/// is a result like any other; these two are the messages that only a play-off has. Both sit on the table shelf, as the
/// season movement does, so the play-off does not need a category of its own.
/// </remarks>
public static class PlayoffInboxTemplates
{
    /// <summary>The club's place in the table sent it into the bracket.</summary>
    public const string Drawn = "inbox.playoff.drawn";

    /// <summary>The semi-final is in and the club has a final to play.</summary>
    public const string FinalSet = "inbox.playoff.final_set";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the message a club's place in the bracket produces.</summary>
    /// <param name="place">The club's final table place.</param>
    /// <param name="stage">The match the club plays.</param>
    /// <param name="opponentName">The opponent, or null for a final whose away side is the winner of the semi-final.</param>
    /// <param name="isHome">Whether the club hosts.</param>
    /// <param name="kickoffAt">When the match kicks off.</param>
    /// <param name="fixtureId">The match, which the message links to, or null for a final that is not scheduled yet.</param>
    public static InboxDraft DrawnInto(
        int place,
        PlayoffStage stage,
        string? opponentName,
        bool isHome,
        DateTimeOffset kickoffAt,
        Guid? fixtureId) =>
        Write(Drawn, new DrawnParameters(place, stage.ToCode(), opponentName, isHome, kickoffAt), fixtureId);

    /// <summary>Builds the message a club with a final to play produces.</summary>
    /// <param name="opponentName">The opponent.</param>
    /// <param name="isHome">Whether the club hosts.</param>
    /// <param name="kickoffAt">When the final kicks off.</param>
    /// <param name="fixtureId">The final, which the message links to.</param>
    public static InboxDraft FinalIsSet(string opponentName, bool isHome, DateTimeOffset kickoffAt, Guid fixtureId) =>
        Write(FinalSet, new FinalSetParameters(opponentName, isHome, kickoffAt), fixtureId);

    private static InboxDraft Write<TParameters>(string templateKey, TParameters parameters, Guid? fixtureId) =>
        new(InboxCategory.Table, templateKey, JsonSerializer.Serialize(parameters, Options), fixtureId);

    /// <summary>Reads a stored parameter document back.</summary>
    internal static TParameters Read<TParameters>(string parametersJson) =>
        JsonSerializer.Deserialize<TParameters>(parametersJson, Options)
            ?? throw new InvalidOperationException("A stored inbox message carries no parameters.");

    internal sealed record DrawnParameters(
        int Place,
        string Stage,
        string? OpponentName,
        bool IsHome,
        DateTimeOffset KickoffAt);

    internal sealed record FinalSetParameters(string OpponentName, bool IsHome, DateTimeOffset KickoffAt);
}
