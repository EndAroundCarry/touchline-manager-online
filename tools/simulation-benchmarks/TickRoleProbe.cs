using System.Globalization;
using System.Text;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Highlights;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Spatial;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// The tick role probe (plan at-the-moment-in-adaptive-muffin, Milestone 0): what each kind of slot does, per player per match.
/// </summary>
/// <remarks>
/// <para>
/// Equal sides stand in board formations and play on the tick engine. Every player is grouped by the formation he stands in and the
/// family and role of his slot, so a winger and a wide midfielder, or a full-back beside them, can be read side by side. It changes no
/// play: shots, goals and the player lines come from the result, and where a man stands comes from the continuous recording.
/// </para>
/// <para>
/// With the ball is the side that touched it last in open play; a ball in the air between two players still counts for the passer.
/// The own-X figures are metres from the man's own goal. Dead-ball stoppages are left out of every position figure, and a step longer
/// than fifteen metres in one frame (a placed player) is not counted as running.
/// </para>
/// </remarks>
internal static class TickRoleProbe
{
    private const int Entities = TickMatchRecording.Entities;
    private const int Half = Entities / 2;

    /// <summary>A step longer than this in one frame is a placement, not running, in pitch units (15 m).</summary>
    private const int TeleportUnits = 1_500;

    /// <summary>Pitch units to metres.</summary>
    private const double UnitsPerMetre = 100.0;

    private static readonly (string Name, FormationPreset Home, string HomeLabel, FormationPreset Away, string AwayLabel)[] Cells =
    [
        ("442v433", FormationPreset.FourFourTwo, "4-4-2", FormationPreset.FourThreeThree, "4-3-3"),
        ("442v442", FormationPreset.FourFourTwo, "4-4-2", FormationPreset.FourFourTwo, "4-4-2"),
        ("343v4231", FormationPreset.ThreeFourThree, "3-4-3", FormationPreset.FourTwoThreeOne, "4-2-3-1"),
    ];

    /// <summary>Runs the probe.</summary>
    /// <param name="matches">How many matches a cell.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="only">The cell to play (442v433, 442v442, 343v4231), or empty for all three.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules, string only)
    {
        Directory.CreateDirectory(".run-logs");

        var tsv = new StringBuilder("cell\tgroup\tplayers\tshots\tgoals\tcrosses\tcrosses_completed\ttackles\ttackles_won\tpasses\tdribbles\town_x_with\town_x_without\tshare_own_half_without\tkm\n");
        var tag = only.Length == 0 ? "all" : only;

        foreach (var cell in Cells.Where(cell => only.Length == 0 || cell.Name.StartsWith(only, StringComparison.OrdinalIgnoreCase)))
        {
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            var gate = new object();
            double goals = 0;
            double shots = 0;
            double homePoints = 0;

            Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
            {
                var input = TickCalibration.Stand(
                    LaboratoryFixtures.EvenlyMatched(seed + (ulong)index, rules),
                    cell.Home,
                    cell.Away);
                var recorder = new MatchPassageRecorder();
                var result = MatchSimulator.Simulate(input, rules, new PlayerLiveMetricsRecorder(), recorder, TickMatchEngine.Instance);

                if (recorder.Tick is not { } recording)
                {
                    return;
                }

                var local = new Dictionary<string, Group>(StringComparer.Ordinal);

                Read(input, result, recording, cell.HomeLabel, cell.AwayLabel, local);

                lock (gate)
                {
                    foreach (var (key, group) in local)
                    {
                        if (!groups.TryGetValue(key, out var total))
                        {
                            groups[key] = total = new Group();
                        }

                        total.Add(group);
                    }

                    goals += result.HomeGoals + result.AwayGoals;
                    shots += result.Home.Shots + result.Away.Shots;
                    homePoints += result.HomeGoals > result.AwayGoals ? 3 : (result.HomeGoals == result.AwayGoals ? 1 : 0);
                }
            });

            Console.WriteLine($"== tickroles {cell.Name}: {cell.HomeLabel} at home to {cell.AwayLabel}, {matches} matches, seed {seed}; goals {goals / matches:F2}, shots {shots / matches:F1}, home points {homePoints / matches:F2} ==");
            Console.WriteLine($"  {"group",-30} {"n",3} {"shots",6} {"fk",6} {"open",6} {"goals",6} {"cross",6} {"done",6} {"tackle",6} {"won",6} {"pass",6} {"drib",6} {"xWith",6} {"xWout",6} {"ownHf%",6} {"km",6}   (per player per match; x in m from own goal)");

            foreach (var (key, group) in groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var players = Math.Max(1, group.Players);
                var perSlot = (double)group.Players / matches;

                double Per(long value) => (double)value / players;

                var with = group.FramesWith == 0 ? 0 : group.OwnXWith / (double)group.FramesWith / UnitsPerMetre;
                var without = group.FramesWithout == 0 ? 0 : group.OwnXWithout / (double)group.FramesWithout / UnitsPerMetre;
                var ownHalf = group.FramesWithout == 0 ? 0 : 100.0 * group.OutOfPossessionOwnHalf / group.FramesWithout;
                var km = group.DistanceUnits / UnitsPerMetre / 1_000.0 / players;

                Console.WriteLine($"  {key,-30} {perSlot,3:F0} {Per(group.Shots),6:F2} {Per(group.FreeKickShots),6:F2} {Per(group.Shots - group.FreeKickShots),6:F2} {Per(group.Goals),6:F2} {Per(group.CrossesAttempted),6:F2} {Per(group.CrossesCompleted),6:F2} {Per(group.TacklesAttempted),6:F2} {Per(group.TacklesWon),6:F2} {Per(group.Passes),6:F1} {Per(group.Dribbles),6:F2} {with,6:F1} {without,6:F1} {ownHalf,6:F1} {km,6:F2}");
                tsv.Append(CultureInfo.InvariantCulture, $"{cell.Name}\t{key}\t{perSlot:F0}\t{Per(group.Shots):F3}\t{Per(group.Goals):F3}\t{Per(group.CrossesAttempted):F3}\t{Per(group.CrossesCompleted):F3}\t{Per(group.TacklesAttempted):F3}\t{Per(group.TacklesWon):F3}\t{Per(group.Passes):F2}\t{Per(group.Dribbles):F3}\t{with:F2}\t{without:F2}\t{ownHalf:F2}\t{km:F3}\n");
            }

            Console.WriteLine();
        }

        var path = $".run-logs/roles-{tag}-{seed}.tsv";

        File.WriteAllText(path, tsv.ToString());
        Console.WriteLine($"  wrote {path}");
    }

    private static void Read(MatchInputV1 input, MatchResultV1 result, TickMatchRecording recording, string homeLabel, string awayLabel, Dictionary<string, Group> groups)
    {
        var labels = new string[Entities];
        var entityOf = new Dictionary<Guid, int>();

        foreach (var (side, sideInput, shape) in new[] { (0, input.Home, homeLabel), (1, input.Away, awayLabel) })
        {
            foreach (var slot in sideInput.Slots)
            {
                var entity = (side * Half) + slot.SlotNumber - 1;

                labels[entity] = $"{shape} {slot.Family}/{slot.Role}";
                entityOf[slot.ParticipantId] = entity;
            }
        }

        // A substitute takes over his team-mate's slot, so his figures belong to the slot's kind.
        foreach (var stamp in recording.Rosters.Where(stamp => stamp.Occupant != Guid.Empty))
        {
            entityOf[stamp.Occupant] = stamp.Entity;
        }

        var local = new Group?[Entities];

        Group Of(int entity)
        {
            if (local[entity] is { } found)
            {
                return found;
            }

            if (!groups.TryGetValue(labels[entity], out var group))
            {
                groups[labels[entity]] = group = new Group();
            }

            return local[entity] = group;
        }

        // Every slot counts as one player of its kind in each match, whether or not a substitute played part of it.
        for (var entity = 0; entity < Entities; entity++)
        {
            Of(entity).Players++;
        }

        foreach (var matchEvent in result.Events)
        {
            if (matchEvent.ParticipantId is not { } participant || !entityOf.TryGetValue(participant, out var entity))
            {
                continue;
            }

            if (matchEvent.Type is EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget or EngineEventType.Woodwork)
            {
                Of(entity).Shots++;
            }

            if (matchEvent.Type == EngineEventType.Goal)
            {
                Of(entity).Goals++;
            }

            if (matchEvent.Type == EngineEventType.FreeKickShot)
            {
                Of(entity).FreeKickShots++;
            }
        }

        foreach (var line in result.PlayerLines)
        {
            if (!entityOf.TryGetValue(line.ParticipantId, out var entity))
            {
                continue;
            }

            var group = Of(entity);

            group.CrossesAttempted += line.CrossesAttempted;
            group.CrossesCompleted += line.CrossesCompleted;
            group.TacklesAttempted += line.TacklesAttempted;
            group.TacklesWon += line.TacklesWon;
            group.Passes += line.PassesAttempted;
            group.Dribbles += line.DribblesAttempted;
        }

        var possession = -1;

        for (var frame = 0; frame < recording.FrameCount; frame++)
        {
            var controller = recording.Controller(frame);

            if (controller >= 0)
            {
                possession = controller / Half;
            }

            if (recording.State(frame) != TickPlayState.OpenPlay || possession < 0)
            {
                continue;
            }

            var placed = recording.Flags(frame) != TickFrameFlags.None;
            var stepped = frame > 0 && !placed && recording.State(frame - 1) == TickPlayState.OpenPlay;

            for (var entity = 0; entity < Entities; entity++)
            {
                var x = recording.PlayerX(frame, entity);

                if (x == TickMatchRecording.Absent)
                {
                    continue;
                }

                var group = Of(entity);
                var side = entity / Half;
                var ownX = side == 0 ? x : SpatialPitch.PitchLength - x;

                if (side == possession)
                {
                    group.OwnXWith += ownX;
                    group.FramesWith++;
                }
                else
                {
                    group.OwnXWithout += ownX;
                    group.FramesWithout++;

                    if (ownX < SpatialPitch.PitchLength / 2)
                    {
                        group.OutOfPossessionOwnHalf++;
                    }
                }

                if (stepped && recording.PlayerX(frame - 1, entity) != TickMatchRecording.Absent)
                {
                    var dx = (long)x - recording.PlayerX(frame - 1, entity);
                    var dy = (long)recording.PlayerY(frame, entity) - recording.PlayerY(frame - 1, entity);
                    var step = (long)Math.Sqrt((dx * dx) + (dy * dy));

                    if (step <= TeleportUnits)
                    {
                        group.DistanceUnits += step;
                    }
                }
            }
        }
    }

    /// <summary>The totals of one kind of slot: every man who stood in one over every match.</summary>
    private sealed class Group
    {
        public long Players { get; set; }

        public long Shots { get; set; }

        public long FreeKickShots { get; set; }

        public long Goals { get; set; }

        public long CrossesAttempted { get; set; }

        public long CrossesCompleted { get; set; }

        public long TacklesAttempted { get; set; }

        public long TacklesWon { get; set; }

        public long Passes { get; set; }

        public long Dribbles { get; set; }

        public long OwnXWith { get; set; }

        public long FramesWith { get; set; }

        public long OwnXWithout { get; set; }

        public long FramesWithout { get; set; }

        public long OutOfPossessionOwnHalf { get; set; }

        public long DistanceUnits { get; set; }

        public void Add(Group other)
        {
            Players += other.Players;
            Shots += other.Shots;
            FreeKickShots += other.FreeKickShots;
            Goals += other.Goals;
            CrossesAttempted += other.CrossesAttempted;
            CrossesCompleted += other.CrossesCompleted;
            TacklesAttempted += other.TacklesAttempted;
            TacklesWon += other.TacklesWon;
            Passes += other.Passes;
            Dribbles += other.Dribbles;
            OwnXWith += other.OwnXWith;
            FramesWith += other.FramesWith;
            OwnXWithout += other.OwnXWithout;
            FramesWithout += other.FramesWithout;
            OutOfPossessionOwnHalf += other.OutOfPossessionOwnHalf;
            DistanceUnits += other.DistanceUnits;
        }
    }
}
