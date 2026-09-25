using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Serialization;

/// <summary>
/// Writes a match's input and output in the one canonical form their hashes are taken over.
/// </summary>
/// <remarks>
/// <para>
/// The hashes are the engine's integrity spine: a stored result can be re-derived byte for byte, and a
/// defect is found by a golden hash failing rather than by a manager noticing (MAT-9, ADR-0004). That only
/// means something if the serialization is itself stable, so three rules apply throughout:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Collections are sorted into a fixed order before they are written.</b> Nothing iterates a hash set,
/// a dictionary, or a caller's list order. The squad is written in participant-identity order, the slots in
/// slot-number order, the events in sequence order, and the player lines by club and identity.
/// </description></item>
/// <item><description>
/// <b>Numbers are formatted invariantly.</b> Every integer goes through
/// <see cref="CultureInfo.InvariantCulture"/>, so a machine whose culture writes digits or signs
/// differently produces the same bytes.
/// </description></item>
/// <item><description>
/// <b>Every field is labelled.</b> Each value is written as <c>name=value</c> on its own line, so a field
/// cannot silently change position and two adjacent numbers cannot be read as one.
/// </description></item>
/// </list>
/// <para>
/// Content, input, and output are three different digests. The <b>content</b> hash covers the snapshot's
/// facts without the seed and is what the seed is derived from. The <b>input</b> hash covers the facts and
/// the seed and is what gets stored on the match. The <b>output</b> hash covers the result, including the
/// input hash, which is what binds a scoreline to the snapshot that produced it.
/// </para>
/// </remarks>
public static class CanonicalMatchSerializer
{
    /// <summary>Hashes the snapshot's facts, excluding the seed.</summary>
    /// <param name="input">The snapshot.</param>
    /// <returns>The lowercase hexadecimal content hash, to derive the seed from.</returns>
    public static string ContentHash(MatchInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return DigestOf(CanonicalText(input, includeSeed: false));
    }

    /// <summary>Hashes the complete snapshot, including the seed (MAT-9).</summary>
    /// <param name="input">The snapshot.</param>
    /// <returns>The lowercase hexadecimal input hash.</returns>
    public static string InputHash(MatchInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return DigestOf(CanonicalText(input, includeSeed: true));
    }

    /// <summary>Hashes a result, including the input hash it was produced from.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The lowercase hexadecimal output hash.</returns>
    public static string OutputHash(MatchResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return DigestOf(CanonicalResultText(result));
    }

    /// <summary>Renders the snapshot in its canonical form, for diagnosis rather than for hashing.</summary>
    /// <param name="input">The snapshot.</param>
    /// <param name="includeSeed">Whether to include the seed line.</param>
    public static string CanonicalText(MatchInputV1 input, bool includeSeed = true)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = new List<string>
        {
            "match-input-v1",
            Field("engineVersion", input.EngineVersion),
            Field("ruleSetVersion", input.RuleSetVersion),
            Field("fixtureId", input.FixtureId),
            Field("worldId", input.WorldId),
            Field("seasonId", input.SeasonId),
            Field("homeAdvantageBasisPoints", input.HomeAdvantageBasisPoints),
            Field("formulaConfigurationHash", input.FormulaConfigurationHash),
        };

        if (includeSeed)
        {
            lines.Add(Field("seed", input.Seed));
        }

        WriteSide(lines, "home", input.Home);
        WriteSide(lines, "away", input.Away);

        return string.Join('\n', lines);
    }

    /// <summary>Renders a result in its canonical form, for diagnosis rather than for hashing.</summary>
    /// <param name="result">The result.</param>
    public static string CanonicalText(MatchResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return CanonicalResultText(result);
    }

    private static void WriteSide(List<string> lines, string label, MatchSideV1 side)
    {
        lines.Add($"side.{label}");
        lines.Add(Field($"{label}.clubId", side.ClubId));

        // Names are part of the frozen snapshot even though the simulation never reads them. A snapshot is
        // identified by its whole content, so leaving a field out would let two different snapshots agree.
        lines.Add(Field($"{label}.clubName", side.ClubName));
        lines.Add(Field($"{label}.mentality", (int)side.Instructions.Mentality));
        lines.Add(Field($"{label}.tempo", (int)side.Instructions.Tempo));
        lines.Add(Field($"{label}.passing", (int)side.Instructions.Passing));
        lines.Add(Field($"{label}.width", (int)side.Instructions.Width));
        lines.Add(Field($"{label}.pressing", (int)side.Instructions.Pressing));
        lines.Add(Field($"{label}.defensiveLine", (int)side.Instructions.DefensiveLine));
        lines.Add(Field($"{label}.tackling", (int)side.Instructions.Tackling));
        lines.Add(Field($"{label}.timeWasting", (int)side.Instructions.TimeWasting));

        foreach (var participant in side.Squad.OrderBy(participant => participant.ParticipantId))
        {
            var prefix = $"{label}.participant.{participant.ParticipantId:D}";

            lines.Add($"participant.{label}.{participant.ParticipantId:D}");
            lines.Add(Field($"{prefix}.playerId", participant.PlayerId));
            lines.Add(Field($"{prefix}.name", participant.DisplayName));
            lines.Add(Field($"{prefix}.shirtNumber", participant.ShirtNumber));
            lines.Add(Field($"{prefix}.position", (int)participant.Position));
            lines.Add(
                Field(
                    $"{prefix}.secondaryPositions",
                    string.Join(',', participant.SecondaryPositions.OrderBy(position => (int)position).Select(position => ((int)position).ToString(CultureInfo.InvariantCulture)))));

            var attributes = participant.Attributes.Values;

            for (var index = 0; index < attributes.Count; index++)
            {
                lines.Add(Field($"{prefix}.attribute.{(MatchAttributeName)index}", attributes[index]));
            }

            lines.Add(Field($"{prefix}.condition", participant.State.ConditionBasisPoints));
            lines.Add(Field($"{prefix}.fatigue", participant.State.FatigueBasisPoints));
            lines.Add(Field($"{prefix}.morale", participant.State.MoraleBasisPoints));
            lines.Add(Field($"{prefix}.sharpness", participant.State.SharpnessBasisPoints));
        }

        foreach (var slot in side.Slots.OrderBy(slot => slot.SlotNumber))
        {
            var prefix = $"{label}.slot.{slot.SlotNumber}";

            lines.Add($"slot.{label}.{slot.SlotNumber}");
            lines.Add(Field($"{prefix}.family", (int)slot.Family));
            lines.Add(Field($"{prefix}.role", (int)slot.Role));
            lines.Add(Field($"{prefix}.x", slot.X));
            lines.Add(Field($"{prefix}.y", slot.Y));
            lines.Add(Field($"{prefix}.participantId", slot.ParticipantId));
        }
    }

    private static string CanonicalResultText(MatchResultV1 result)
    {
        var lines = new List<string>
        {
            "match-result-v1",
            Field("engineVersion", result.EngineVersion),
            Field("ruleSetVersion", result.RuleSetVersion),
            Field("inputHash", result.InputHash),
            Field("homeGoals", result.HomeGoals),
            Field("awayGoals", result.AwayGoals),
            Field("totalMinutesPlayed", result.TotalMinutesPlayed),
        };

        WriteStatistics(lines, "home", result.Home);
        WriteStatistics(lines, "away", result.Away);

        foreach (var matchEvent in result.Events.OrderBy(matchEvent => matchEvent.Sequence))
        {
            var prefix = $"event.{matchEvent.Sequence}";

            lines.Add($"event.{matchEvent.Sequence}");
            lines.Add(Field($"{prefix}.minute", matchEvent.Minute));
            lines.Add(Field($"{prefix}.stoppageMinute", matchEvent.StoppageMinute));
            lines.Add(Field($"{prefix}.side", (int)matchEvent.Side));
            lines.Add(Field($"{prefix}.clubId", matchEvent.ClubId));
            lines.Add(Field($"{prefix}.type", (int)matchEvent.Type));
            lines.Add(Field($"{prefix}.participantId", matchEvent.ParticipantId));
            lines.Add(Field($"{prefix}.secondaryParticipantId", matchEvent.SecondaryParticipantId));
            lines.Add(Field($"{prefix}.zone", matchEvent.Zone is null ? null : (int)matchEvent.Zone.Value));
            lines.Add(Field($"{prefix}.qualityBasisPoints", matchEvent.QualityBasisPoints));
            lines.Add(Field($"{prefix}.absenceFixtures", matchEvent.AbsenceFixtures));
            lines.Add(Field($"{prefix}.substitutionReason", matchEvent.SubstitutionReason is null ? null : (int)matchEvent.SubstitutionReason.Value));
        }

        foreach (var line in result.PlayerLines.OrderBy(line => line.ClubId).ThenBy(line => line.ParticipantId))
        {
            var prefix = $"line.{line.ClubId:D}.{line.ParticipantId:D}";

            lines.Add($"line.{line.ClubId:D}.{line.ParticipantId:D}");
            lines.Add(Field($"{prefix}.side", (int)line.Side));
            lines.Add(Field($"{prefix}.started", line.Started));
            lines.Add(Field($"{prefix}.minutesPlayed", line.MinutesPlayed));
            lines.Add(Field($"{prefix}.goals", line.Goals));
            lines.Add(Field($"{prefix}.yellowCards", line.YellowCards));
            lines.Add(Field($"{prefix}.sentOff", line.SentOff));
            lines.Add(Field($"{prefix}.absenceFixtures", line.AbsenceFixtures));
        }

        return string.Join('\n', lines);
    }

    private static void WriteStatistics(List<string> lines, string label, MatchStatisticsV1 statistics)
    {
        var prefix = $"statistics.{label}";

        lines.Add($"statistics.{label}");
        lines.Add(Field($"{prefix}.possession", statistics.PossessionBasisPoints));
        lines.Add(Field($"{prefix}.goals", statistics.Goals));
        lines.Add(Field($"{prefix}.shots", statistics.Shots));
        lines.Add(Field($"{prefix}.shotsOnTarget", statistics.ShotsOnTarget));
        lines.Add(Field($"{prefix}.shotsOffTarget", statistics.ShotsOffTarget));
        lines.Add(Field($"{prefix}.shotsBlocked", statistics.ShotsBlocked));
        lines.Add(Field($"{prefix}.woodworkHits", statistics.WoodworkHits));
        lines.Add(Field($"{prefix}.saves", statistics.Saves));
        lines.Add(Field($"{prefix}.corners", statistics.Corners));
        lines.Add(Field($"{prefix}.offsides", statistics.Offsides));
        lines.Add(Field($"{prefix}.fouls", statistics.Fouls));
        lines.Add(Field($"{prefix}.yellowCards", statistics.YellowCards));
        lines.Add(Field($"{prefix}.redCards", statistics.RedCards));
        lines.Add(Field($"{prefix}.penaltiesAwarded", statistics.PenaltiesAwarded));
        lines.Add(Field($"{prefix}.penaltiesScored", statistics.PenaltiesScored));
        lines.Add(Field($"{prefix}.injuries", statistics.Injuries));
        lines.Add(Field($"{prefix}.substitutions", statistics.Substitutions));
    }

    private static string DigestOf(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    private static string Field(string name, string value) => $"{name}={value}";

    private static string Field(string name, int value) => $"{name}={value.ToString(CultureInfo.InvariantCulture)}";

    private static string Field(string name, ulong value) => $"{name}={value.ToString(CultureInfo.InvariantCulture)}";

    private static string Field(string name, bool value) => $"{name}={(value ? "true" : "false")}";

    private static string Field(string name, Guid value) => $"{name}={value:D}";

    private static string Field(string name, Guid? value) => $"{name}={value?.ToString("D") ?? "-"}";

    private static string Field(string name, int? value) => $"{name}={value?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
