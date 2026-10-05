using System.Globalization;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>
/// The off-ball probe (engine-v10): measures what the manager sees of how players take up positions and who
/// receives the ball, before and after the engine names receivers.
/// </summary>
/// <remarks>
/// <para>
/// Three readings, each printed as a small table. Receivers read the film's own "receive" marks, because that
/// is where a defender receiving in the attacking half shows up, whoever chose him. Positioning tiers play twin
/// strikers that differ only in Positioning and count their shots and goals. Pass credit shows how the
/// attempted passes divide by position family.
/// </para>
/// <para>
/// The probe uses only the engine's public surface, so it runs unchanged against every milestone and the
/// numbers can be compared with the v9 baseline.
/// </para>
/// </remarks>
internal static class OffBallProbe
{
    private const int HighPositioning = 18;
    private const int LowPositioning = 4;
    private const int TwinAbility = 13;
    private const int HalfwayX = 5_000;

    /// <summary>Runs the probe.</summary>
    /// <param name="matches">How many matches each reading plays.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules)
    {
        Console.WriteLine("== Off-ball probe ==");
        Receivers(Math.Min(matches, 1_000), seed, rules);
        PassCredit(Math.Min(matches, 2_000), seed, rules);
        PositioningTwins(matches, seed, rules);
    }

    private static void Receivers(int matches, ulong seed, EngineRulesV2 rules)
    {
        var options = new HighlightOptionsV1();
        var byFamily = new Dictionary<MatchPositionFamily, long>();
        var receptions = 0L;
        var pastHalfway = 0L;
        var defencePastHalfway = 0L;
        var defenceDeepPastHalfway = 0L;
        var passLegs = 0L;
        var soloLegs = 0L;
        var passages = 0L;
        var chainReceives = 0L;
        var chainDefenceReceives = 0L;
        var carries = 0L;
        var defenceCarries = 0L;
        var families = new Dictionary<Guid, MatchPositionFamily>();

        for (var index = 0; index < matches; index++)
        {
            var input = LaboratoryFixtures.EvenlyMatched(seed + (ulong)index);
            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(input, rules, null, recorder);
            var presentation = ReplayDirector.Analyse(input, result, recorder.Passages, options, null).Presentation;

            foreach (var slot in input.Home.Slots.Concat(input.Away.Slots))
            {
                families[slot.ParticipantId] = slot.Family;
            }

            // A leg the holder kept is recorded as a carry after the first waypoint; every other ground or lofted
            // leg is a pass. The touches say who took the ball: the chain's receivers, and the carrier the ground duel
            // draws, who is not part of the chain.
            foreach (var passage in recorder.Passages)
            {
                passages++;

                foreach (var touch in passage.Touches)
                {
                    if (touch.Action is not (PassageAction.Receive or PassageAction.Carry)
                        || !families.TryGetValue(touch.ParticipantId, out var family)
                        || AttackingX(touch.X, passage.Side) <= HalfwayX)
                    {
                        continue;
                    }

                    if (touch.Action == PassageAction.Receive)
                    {
                        chainReceives++;
                        chainDefenceReceives += family == MatchPositionFamily.Defence ? 1 : 0;
                    }
                    else
                    {
                        carries++;
                        defenceCarries += family == MatchPositionFamily.Defence ? 1 : 0;
                    }
                }

                foreach (var waypoint in passage.Waypoints.Skip(1))
                {
                    switch (waypoint.Kind)
                    {
                        case PassageWaypointKind.Carry:
                            soloLegs++;
                            break;

                        case PassageWaypointKind.Pass or PassageWaypointKind.Cross:
                            passLegs++;
                            break;

                        default:
                            break;
                    }
                }
            }

            foreach (var passage in presentation.Passages)
            {
                var players = passage.Entities.Where(entity => !entity.IsBall).ToDictionary(entity => entity.EntityId);

                foreach (var track in passage.Tracks)
                {
                    if (!players.TryGetValue(track.EntityId, out var player) || player.Family is null)
                    {
                        continue;
                    }

                    foreach (var frame in track.Keyframes)
                    {
                        if (frame.Action != "receive")
                        {
                            continue;
                        }

                        receptions++;
                        byFamily[player.Family.Value] = byFamily.GetValueOrDefault(player.Family.Value) + 1;

                        var attackingX = player.Side == MatchSide.Home ? frame.X : 10_000 - frame.X;

                        if (attackingX <= HalfwayX)
                        {
                            continue;
                        }

                        pastHalfway++;

                        if (player.Family == MatchPositionFamily.Defence)
                        {
                            defencePastHalfway++;

                            if (attackingX > 6_500)
                            {
                                defenceDeepPastHalfway++;
                            }
                        }
                    }
                }
            }
        }

        Console.WriteLine($"  Receivers in the film, {matches:N0} matches (each \"receive\" mark):");
        Console.WriteLine($"    receptions per match            {(double)receptions / matches,9:F1}");

        foreach (var family in byFamily.Keys.OrderBy(family => family))
        {
            Console.WriteLine($"    share by {family,-18}  {100.0 * byFamily[family] / Math.Max(1, receptions),8:F1}%");
        }

        Console.WriteLine($"    received past halfway           {(double)pastHalfway / matches,9:F1} per match");
        Console.WriteLine($"    ... by a Defence player         {Pct(defencePastHalfway, pastHalfway),8}%   target near zero");
        Console.WriteLine($"    ... by one beyond x 6,500       {Pct(defenceDeepPastHalfway, pastHalfway),8}%   target zero");
        Console.WriteLine($"    by the engine's touches past halfway: chain receivers by a Defence player {Pct(chainDefenceReceives, chainReceives),6}% of {(double)chainReceives / matches:F1} per match;");
        Console.WriteLine($"      ground-duel carriers (drawn by the play stream, not the chain) by a Defence player {Pct(defenceCarries, carries),6}% of {(double)carries / matches:F1} per match");
        Console.WriteLine($"    legs the holder kept            {Pct(soloLegs, soloLegs + passLegs),8}%   of the carried and passed legs after each start, {(double)soloLegs / Math.Max(1, passages):F2} per possession");
        Console.WriteLine();
    }

    private static void PassCredit(int matches, ulong seed, EngineRulesV2 rules)
    {
        var attempted = new Dictionary<MatchPositionFamily, long>();
        var completed = new Dictionary<MatchPositionFamily, long>();

        for (var index = 0; index < matches; index++)
        {
            var input = LaboratoryFixtures.EvenlyMatched(seed + (ulong)index);
            var result = MatchSimulator.Simulate(input, rules);
            var families = FamiliesOf(input);

            foreach (var line in result.PlayerLines)
            {
                if (!families.TryGetValue(line.ParticipantId, out var family))
                {
                    continue;
                }

                attempted[family] = attempted.GetValueOrDefault(family) + line.PassesAttempted;
                completed[family] = completed.GetValueOrDefault(family) + line.PassesCompleted;
            }
        }

        var total = attempted.Values.Sum();
        Console.WriteLine($"  Pass credit by position family, {matches:N0} matches (the starters' lines):");

        foreach (var family in attempted.Keys.OrderBy(family => family))
        {
            Console.WriteLine(
                $"    {family,-18} {Pct(attempted[family], total),6}% of passes, completion {Pct(completed[family], attempted[family]),5}%");
        }

        Console.WriteLine($"    passes attempted per match      {(double)total / matches,9:F1}");
        Console.WriteLine();
    }

    private static void PositioningTwins(int matches, ulong seed, EngineRulesV2 rules)
    {
        var high = new Tally();
        var low = new Tally();
        var homeGoals = 0L;
        var awayGoals = 0L;

        for (var index = 0; index < matches; index++)
        {
            var input = Twins(LaboratoryFixtures.EvenlyMatched(seed + (ulong)index), out var highId, out var lowId);
            var result = MatchSimulator.Simulate(input, rules);

            homeGoals += result.HomeGoals;
            awayGoals += result.AwayGoals;

            foreach (var matchEvent in result.Events)
            {
                if (matchEvent.ParticipantId is not Guid who || (who != highId && who != lowId))
                {
                    continue;
                }

                var tally = who == highId ? high : low;

                switch (matchEvent.Type)
                {
                    case EngineEventType.Goal:
                        tally.Shots++;
                        tally.Goals++;
                        break;

                    case EngineEventType.ShotSaved or EngineEventType.ShotBlocked
                        or EngineEventType.ShotOffTarget or EngineEventType.Woodwork:
                        tally.Shots++;
                        break;

                    default:
                        break;
                }
            }
        }

        Console.WriteLine(
            $"  Twin strikers, {matches:N0} matches (home slots 10 and 11, Positioning {HighPositioning} against {LowPositioning}, everything else {TwinAbility}):");
        Console.WriteLine($"    {"",-22} {"shots",8} {"goals",8}   per match");
        Console.WriteLine($"    {"Positioning " + HighPositioning,-22} {high.Shots,8} {high.Goals,8}   {(double)high.Shots / matches:F3} shots, {(double)high.Goals / matches:F3} goals");
        Console.WriteLine($"    {"Positioning " + LowPositioning,-22} {low.Shots,8} {low.Goals,8}   {(double)low.Shots / matches:F3} shots, {(double)low.Goals / matches:F3} goals");
        Console.WriteLine($"    goals per match, both sides      {((double)homeGoals + awayGoals) / matches:F3}");
        Console.WriteLine();
    }

    private static int AttackingX(int x, MatchSide side) => side == MatchSide.Home ? x : 10_000 - x;

    private static MatchInputV1 Twins(MatchInputV1 input, out Guid highId, out Guid lowId)
    {
        var strikers = input.Home.Slots.Where(slot => slot.SlotNumber is 10 or 11).OrderBy(slot => slot.SlotNumber).ToArray();

        highId = strikers[0].ParticipantId;
        lowId = strikers[1].ParticipantId;

        var high = highId;
        var low = lowId;
        var squad = input.Home.Squad
            .Select(participant => participant.ParticipantId == high
                ? participant with { Attributes = WithPositioning(HighPositioning) }
                : participant.ParticipantId == low
                    ? participant with { Attributes = WithPositioning(LowPositioning) }
                    : participant)
            .ToList();

        return input with { Home = input.Home with { Squad = squad } };
    }

    private static PlayerAttributesV1 WithPositioning(int positioning)
    {
        var values = Enumerable.Repeat(TwinAbility, MatchAttributeNames.Count).ToArray();

        values[(int)MatchAttributeName.Positioning] = positioning;

        return PlayerAttributesV1.From(values);
    }

    private static Dictionary<Guid, MatchPositionFamily> FamiliesOf(MatchInputV1 input) =>
        input.Home.Slots.Concat(input.Away.Slots).ToDictionary(slot => slot.ParticipantId, slot => slot.Family);

    private static string Pct(long part, long whole) =>
        (whole == 0 ? 0 : 100.0 * part / whole).ToString("F2", CultureInfo.InvariantCulture);

    private sealed class Tally
    {
        public long Shots { get; set; }

        public long Goals { get; set; }
    }
}
