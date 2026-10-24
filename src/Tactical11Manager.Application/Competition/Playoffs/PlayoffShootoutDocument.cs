using System.Text.Json;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Shootout;

namespace Tactical11Manager.Application.Competition.Playoffs;

/// <summary>One kick of a stored shoot-out.</summary>
/// <param name="Number">The kick's place in the shoot-out, from 1.</param>
/// <param name="Round">The round, from 1: each side kicks once a round.</param>
/// <param name="Side">The side that took it, <c>home</c> or <c>away</c>.</param>
/// <param name="TakerId">The player who took it.</param>
/// <param name="GoalkeeperId">The goalkeeper who faced it, or null when the other side had none on the pitch.</param>
/// <param name="Outcome">How it ended: <c>scored</c>, <c>saved</c> or <c>wide</c>.</param>
public sealed record StoredShootoutKick(
    int Number,
    int Round,
    string Side,
    Guid TakerId,
    Guid? GoalkeeperId,
    string Outcome);

/// <summary>A stored shoot-out: the rules that produced it, who kicked first, and every kick.</summary>
/// <param name="Version">The shoot-out rules' version.</param>
/// <param name="FirstSide">The side that kicked first, <c>home</c> or <c>away</c>.</param>
/// <param name="Kicks">Every kick, in the order it was taken.</param>
public sealed record StoredShootout(string Version, string FirstSide, IReadOnlyList<StoredShootoutKick> Kicks);

/// <summary>
/// Writes and reads the document a play-off tie keeps its shoot-out in (`PR-6`).
/// </summary>
/// <remarks>
/// The engine's own result is not stored as it is: the tie keeps a small, versioned document of plain codes, so a later change to the
/// engine's enums cannot change what an old shoot-out reads as (`JSN-5`).
/// </remarks>
public static class PlayoffShootoutDocument
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Writes a shoot-out as the document the tie stores.</summary>
    /// <param name="result">The engine's shoot-out.</param>
    public static string Write(ShootoutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return JsonSerializer.Serialize(
            new StoredShootout(
                result.Version,
                SideCode(result.FirstSide),
                [
                    .. result.Kicks.Select(kick => new StoredShootoutKick(
                        kick.Number,
                        kick.Round,
                        SideCode(kick.Side),
                        kick.TakerId,
                        kick.GoalkeeperId,
                        kick.Outcome switch
                        {
                            ShootoutKickOutcome.Scored => "scored",
                            ShootoutKickOutcome.Saved => "saved",
                            _ => "wide",
                        })),
                ]),
            Options);
    }

    /// <summary>Reads a stored shoot-out back.</summary>
    /// <param name="json">The stored document.</param>
    public static StoredShootout Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<StoredShootout>(json, Options)
            ?? throw new InvalidOperationException("A stored shoot-out carries no kicks.");
    }

    private static string SideCode(MatchSide side) => side == MatchSide.Home ? "home" : "away";
}
