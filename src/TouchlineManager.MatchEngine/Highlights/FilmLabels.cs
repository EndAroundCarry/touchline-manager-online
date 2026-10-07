using System.Globalization;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// The names, colours and codes a film is labelled with (`replay-v4`).
/// </summary>
internal static class FilmLabels
{
    /// <summary>Gets every participant's display name, by identity.</summary>
    /// <param name="input">The frozen snapshot.</param>
    public static Dictionary<Guid, string> Names(MatchInputV1 input)
    {
        var names = new Dictionary<Guid, string>();

        foreach (var side in new[] { input.Home, input.Away })
        {
            foreach (var participant in side.Squad)
            {
                names[participant.ParticipantId] = participant.DisplayName;
            }
        }

        return names;
    }

    /// <summary>Gets the two sides' primary colours.</summary>
    /// <param name="input">The frozen snapshot.</param>
    public static (string Home, string Away) Colours(MatchInputV1 input) =>
        (
            ClubPalette.Resolve(input.Home.ClubId, input.Home.PrimaryColour, input.Home.SecondaryColour).Primary,
            ClubPalette.Resolve(input.Away.ClubId, input.Away.PrimaryColour, input.Away.SecondaryColour).Primary);

    /// <summary>Gets the abbreviated position a slot is labelled with.</summary>
    /// <param name="slot">The slot.</param>
    public static string PositionCode(MatchSlotV1 slot) => slot.Family switch
    {
        MatchPositionFamily.Goalkeeper => "GK",
        MatchPositionFamily.Defence => "DF",
        MatchPositionFamily.Midfield => "MF",
        MatchPositionFamily.Attack => "FW",
        _ => string.Empty,
    };

    /// <summary>Gets whether an event type is a shot.</summary>
    /// <param name="type">The type.</param>
    public static bool IsShot(EngineEventType type) => type is
        EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed
        or EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget
        or EngineEventType.Woodwork or EngineEventType.FreeKickShot;

    /// <summary>Gets the outcome code a passage is keyed by.</summary>
    /// <param name="type">The event type.</param>
    public static string OutcomeCode(EngineEventType type) => type switch
    {
        EngineEventType.Goal => "goal",
        EngineEventType.PenaltyGoal => "penalty_goal",
        EngineEventType.PenaltyMissed => "penalty_missed",
        EngineEventType.FreeKickShot => "free_kick_shot",
        EngineEventType.Woodwork => "woodwork",
        EngineEventType.ShotSaved => "saved",
        EngineEventType.ShotBlocked => "blocked",
        EngineEventType.ShotOffTarget => "off_target",
        _ => "play",
    };

    /// <summary>Writes the sentence that narrates a passage for readers who cannot see the Canvas.</summary>
    /// <param name="matchEvent">The passage's principal event, or null.</param>
    /// <param name="names">The participants' names.</param>
    /// <param name="minute">The minute the passage is labelled with.</param>
    /// <param name="stoppage">The stoppage minute.</param>
    public static string Narration(EngineEventV1? matchEvent, Dictionary<Guid, string> names, int minute, int stoppage)
    {
        if (matchEvent is null)
        {
            return $"Play continues, {ClockLabel(minute, stoppage)}.";
        }

        var player = matchEvent.ParticipantId is Guid id && names.TryGetValue(id, out var name)
            ? name
            : "the attacker";

        var outcome = matchEvent.Type switch
        {
            EngineEventType.Goal => "Goal",
            EngineEventType.PenaltyGoal => "Penalty scored",
            EngineEventType.PenaltyMissed => "Penalty missed",
            EngineEventType.FreeKickShot => "Free kick struck",
            EngineEventType.Woodwork => "Shot against the woodwork",
            EngineEventType.ShotSaved => "Shot saved",
            EngineEventType.ShotBlocked => "Shot blocked",
            EngineEventType.ShotOffTarget => "Shot off target",
            _ => "Chance",
        };

        return $"{outcome} — {player}, {ClockLabel(matchEvent.Minute, matchEvent.StoppageMinute)}.";
    }

    /// <summary>Writes a minute as the scoreboard does: <c>67</c>, or <c>90+3</c>.</summary>
    /// <param name="minute">The minute.</param>
    /// <param name="stoppage">The stoppage minute.</param>
    public static string ClockLabel(int minute, int stoppage) =>
        stoppage > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minute}+{stoppage}")
            : minute.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the minute and stoppage minute the engine would stamp at a match second, on a half's own clock.
    /// </summary>
    /// <param name="second">The match second.</param>
    /// <param name="period">The half.</param>
    /// <param name="rules">The rules in force.</param>
    public static (int Minute, int Stoppage) ClockOf(int second, int period, EngineRulesV2 rules)
    {
        var played = Math.Max(0, second) / Math.Max(1, rules.SecondsPerMinute);
        var regulation = period == 1 ? rules.HalfTimeMinute : rules.RegulationMinutes;

        return played < regulation
            ? (played + 1, 0)
            : (regulation, played + 1 - regulation);
    }
}
