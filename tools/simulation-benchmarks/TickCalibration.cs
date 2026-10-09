using System.Globalization;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>
/// The tick engine's calibration runs (`tick-engine-v1`, Milestone 9): the plan's statistical bands, read over the board's formations
/// and over the snapshots the game has actually stored.
/// </summary>
/// <remarks>
/// The even 4-4-2 fixture the first tuning used is the one geometry the engine cannot go wrong on, so a figure measured there says
/// little. <c>tickmatrix</c> plays every formation against every other with uniform players, and <c>ticksnap</c> replays a
/// tab-separated export of stored snapshots (fixture id, home goals, away goals, snapshot document), with the stored sides' real
/// formations, instructions and attributes. A calibration is accepted only when both stay inside the bands.
/// </remarks>
internal static class TickCalibration
{
    private static readonly int Ability = int.TryParse(Environment.GetEnvironmentVariable("TICK_ABILITY"), out var ability) ? ability : 13;

    /// <summary>What one match came to.</summary>
    private readonly record struct Line(
        int Goals,
        int GoalsWithoutShot,
        int HomeGoals,
        int AwayGoals,
        int Shots,
        int HomeShots,
        int HomeOnTarget,
        int HomeSaved,
        int HomeBlocked,
        int OnTarget,
        int Saved,
        int Blocked,
        int OffTarget,
        int Woodwork,
        int Penalties,
        int Fouls,
        int Yellows,
        int Reds,
        int Corners,
        int Offsides,
        int Passes,
        int Completed);

    /// <summary>Plays every formation against every other, uniform players, and prints one row per pairing and the extremes.</summary>
    /// <param name="matchesPerPair">Matches per pairing.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Matrix(int matchesPerPair, ulong seed, EngineRulesV2 rules)
    {
        var presets = Environment.GetEnvironmentVariable("TICK_FORMS") is { Length: > 0 } list
            ? [.. list.Split(',').Select(entry => (FormationPreset)int.Parse(entry, CultureInfo.InvariantCulture))]
            : FormationPresets.All;
        var total = new List<Line>();
        var rows = new List<(string Name, double Goals, double Shots)>();
        var forGoals = new Dictionary<FormationPreset, List<double>>();
        var againstGoals = new Dictionary<FormationPreset, List<double>>();
        var forShots = new Dictionary<FormationPreset, List<double>>();

        Console.WriteLine($"== Tick matrix, {presets.Count}x{presets.Count} formations, {matchesPerPair} matches each ==");

        foreach (var home in presets)
        {
            foreach (var away in presets)
            {
                var lines = new Line[matchesPerPair];

                Parallel.For(0, matchesPerPair, index =>
                {
                    var input = LaboratoryFixtures.Build(
                        seed + (ulong)index,
                        Ability,
                        Ability,
                        Instructions(),
                        Instructions(),
                        rules);

                    input = Stand(input, home, away);
                    input = input with { Home = WithAttributes(input.Home, "TICK_HOME_ATTR"), Away = WithAttributes(input.Away, "TICK_AWAY_ATTR") };
                    lines[index] = Play(input, rules);
                });

                total.AddRange(lines);
                foreach (var (preset, scored, conceded, shots) in new[] { (home, lines.Average(l => l.HomeGoals), lines.Average(l => l.AwayGoals), lines.Average(l => l.HomeShots)), (away, lines.Average(l => l.AwayGoals), lines.Average(l => l.HomeGoals), lines.Average(l => l.Shots - l.HomeShots)) })
                {
                    forGoals.TryAdd(preset, []); againstGoals.TryAdd(preset, []); forShots.TryAdd(preset, []);
                    forGoals[preset].Add(scored); againstGoals[preset].Add(conceded); forShots[preset].Add(shots);
                }
                rows.Add(($"{home} v {away}", lines.Average(line => line.Goals), lines.Average(line => line.Shots)));
            }
        }

        foreach (var row in rows.OrderByDescending(row => row.Goals).Take(8))
        {
            Console.WriteLine($"  worst  {row.Name,-36} goals {row.Goals,6:F2}  shots {row.Shots,6:F1}");
        }

        foreach (var row in rows.OrderBy(row => row.Goals).Take(4))
        {
            Console.WriteLine($"  best   {row.Name,-36} goals {row.Goals,6:F2}  shots {row.Shots,6:F1}");
        }

        foreach (var preset in presets)
        {
            Console.WriteLine($"  {preset,-18} scores {forGoals[preset].Average(),5:F2}  concedes {againstGoals[preset].Average(),5:F2}  shots for {forShots[preset].Average(),5:F1}");
        }

        Console.WriteLine($"  pairings inside goals 2.2 - 3.4: {rows.Count(row => row.Goals is >= 2.2 and <= 3.4)} of {rows.Count}");
        Print("all pairings", total);
    }

    /// <summary>Replays stored snapshots on the current engine and prints what they came to.</summary>
    /// <param name="path">The tab-separated export.</param>
    /// <param name="limit">The most snapshots to play.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Snapshots(string path, int limit, EngineRulesV2 rules)
    {
        var inputs = new List<MatchInputV1>();

        foreach (var row in File.ReadLines(path).Take(limit))
        {
            var parts = row.Split('\t', 4);
            var content = MatchSnapshotDocument.Read(parts[3]);
            var input = content.Input;

            if (Environment.GetEnvironmentVariable("TICK_UNIFORM") is { Length: > 0 } uniform)
            {
                var value = int.Parse(uniform, CultureInfo.InvariantCulture);

                input = input with
                {
                    Home = input.Home with { Squad = [.. input.Home.Squad.Select(player => player with { Attributes = PlayerAttributesV1.Uniform(value) })] },
                    Away = input.Away with { Squad = [.. input.Away.Squad.Select(player => player with { Attributes = PlayerAttributesV1.Uniform(value) })] },
                };
            }

            if (Environment.GetEnvironmentVariable("TICK_FLAT") is { Length: > 0 } flat)
            {
                var names = flat.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<MatchAttributeName>).ToArray();

                PlayerAttributesV1 Flatten(PlayerAttributesV1 attributes)
                {
                    var values = attributes.Values.ToArray();

                    foreach (var name in names)
                    {
                        values[(int)name] = 13;
                    }

                    return PlayerAttributesV1.From(values);
                }

                input = input with
                {
                    Home = input.Home with { Squad = [.. input.Home.Squad.Select(player => player.IsGoalkeeper && Environment.GetEnvironmentVariable("TICK_FLATGK") is null ? player : player with { Attributes = Flatten(player.Attributes) })] },
                    Away = input.Away with { Squad = [.. input.Away.Squad.Select(player => player.IsGoalkeeper && Environment.GetEnvironmentVariable("TICK_FLATGK") is null ? player : player with { Attributes = Flatten(player.Attributes) })] },
                };
            }

            if (Environment.GetEnvironmentVariable("TICK_FORM") is { Length: > 0 } form)
            {
                var preset = (FormationPreset)int.Parse(form, CultureInfo.InvariantCulture);
                input = Stand(input, preset, preset);
            }

            input = input with
            {
                EngineVersion = EngineVersions.EngineLabel,
                RuleSetVersion = EngineVersions.RuleSetLabel,
                FormulaConfigurationHash = EngineConfiguration.HashOf(rules),
            };

            inputs.Add(input);
        }

        if (Environment.GetEnvironmentVariable("TICK_ONLY") is { Length: > 0 } only)
        {
            inputs = [inputs[int.Parse(only, CultureInfo.InvariantCulture)]];
        }

        Console.WriteLine($"== Stored snapshots, {inputs.Count} matches ==");

        if (Environment.GetEnvironmentVariable("TICK_TRACE") is { Length: > 0 } trace && inputs.Count > 0)
        {
            var traced = MatchSimulator.Simulate(inputs[int.Parse(trace, CultureInfo.InvariantCulture)], rules);

            foreach (var entry in traced.Events.Take(400))
            {
                Console.WriteLine($"  {entry.Minute,3}' {entry.Side,-5} {entry.Type,-16} {entry.Zone} q={entry.QualityBasisPoints}");
            }
        }

        var lines = new Line[inputs.Count];

        Parallel.For(0, inputs.Count, index => lines[index] = Play(inputs[index], rules));

        if (Environment.GetEnvironmentVariable("TICK_VERBOSE") is not null)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var h = inputs[i].Home.Instructions;
                var a = inputs[i].Away.Instructions;
                Console.WriteLine($"  #{i,2} goals {lines[i].HomeGoals}-{lines[i].AwayGoals} shots {lines[i].Shots,3} reds {lines[i].Reds} abil {TeamAbility(inputs[i].Home):F1}/{TeamAbility(inputs[i].Away):F1} {Shape(inputs[i].Home)} v {Shape(inputs[i].Away)} | H men{(int)h.Mentality} tmp{(int)h.Tempo} prs{(int)h.Pressing} line{(int)h.DefensiveLine} tkl{(int)h.Tackling} pass{(int)h.Passing} | A men{(int)a.Mentality} tmp{(int)a.Tempo} prs{(int)a.Pressing} line{(int)a.DefensiveLine} tkl{(int)a.Tackling} pass{(int)a.Passing}");
            }
        }

        Print("stored snapshots", lines);

        if (Environment.GetEnvironmentVariable("TICK_FILM") is not null)
        {
            var seconds = new double[inputs.Count];
            var passages = new double[inputs.Count];
            var kilobytes = new double[inputs.Count];

            Parallel.For(0, inputs.Count, index =>
            {
                var metrics = new PlayerLiveMetricsRecorder();
                var recorder = new MatchPassageRecorder();
                var result = MatchSimulator.Simulate(inputs[index], rules, metrics, recorder);
                var film = ReplayDirector.Build(inputs[index], result, recorder, liveMetrics: metrics.Metrics);

                var bare = film.Passages.Count(passage => passage.Tracks.Count != passage.Entities.Count);
                if (bare > 0)
                {
                    Console.WriteLine($"  match {index}: {bare} passage(s) whose tracks do not match their entities: " + string.Join(", ", film.Passages.Where(passage => passage.Tracks.Count != passage.Entities.Count).Select(passage => $"tracks {passage.Tracks.Count} entities {passage.Entities.Count} ms {passage.DurationMilliseconds} {passage.OutcomeCode}")));
                }

                seconds[index] = film.TotalPlaybackMilliseconds / 1_000.0;
                passages[index] = film.Passages.Count;
                kilobytes[index] = film.EstimatedPayloadBytes / 1_024.0;
            });

            Console.WriteLine($"  film seconds {seconds.Average(),7:F0} (max {seconds.Max():F0}, min {seconds.Min():F0})   passages {passages.Average(),5:F1} (max {passages.Max():F0})   payload KB {kilobytes.Average(),6:F0} (max {kilobytes.Max():F0}); over 75 passages {passages.Count(value => value > 75)}, over 750 KB {kilobytes.Count(value => value > 750)} of {inputs.Count}");
        }
    }

    /// <summary>Reads TICK_INSTR ("Mentality=4,Tempo=2") into the instructions both sides play under; empty is the defaults.</summary>
    private static MatchInstructionsV1 Instructions()
    {
        object boxed = new MatchInstructionsV1();

        if (Environment.GetEnvironmentVariable("TICK_INSTR") is { Length: > 0 } text)
        {
            foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var property = typeof(MatchInstructionsV1).GetProperty(parts[0])
                    ?? throw new ArgumentException($"No instruction named {parts[0]}.");
                var value = int.Parse(parts[1], CultureInfo.InvariantCulture);

                property.SetValue(boxed, property.PropertyType == typeof(bool) ? value != 0 : Enum.ToObject(property.PropertyType, value));
            }
        }

        return (MatchInstructionsV1)boxed;
    }

    /// <summary>Reads an attribute override ("16" for all, or "13,Pace=8,Finishing=18") from an environment variable and applies it to a side.</summary>
    private static MatchSideV1 WithAttributes(MatchSideV1 side, string variable)
    {
        if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } text)
        {
            return side;
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var values = Enumerable.Repeat(int.Parse(parts[0], CultureInfo.InvariantCulture), 28).ToArray();

        foreach (var pair in parts.Skip(1))
        {
            var kv = pair.Split('=', 2);

            values[(int)Enum.Parse<MatchAttributeName>(kv[0])] = int.Parse(kv[1], CultureInfo.InvariantCulture);
        }

        var attributes = PlayerAttributesV1.From(values);

        return side with { Squad = [.. side.Squad.Select(player => player with { Attributes = attributes })] };
    }

    private static double TeamAbility(MatchSideV1 side)
    {
        var starters = side.Slots.Where(slot => slot.Family != MatchPositionFamily.Goalkeeper).Select(slot => slot.ParticipantId).ToHashSet();

        return side.Squad.Where(player => starters.Contains(player.ParticipantId)).Average(player => player.Attributes.Values.Take(24).Average());
    }

    private static string Shape(MatchSideV1 side) =>
        $"{side.Slots.Count(slot => slot.Family == MatchPositionFamily.Defence)}-{side.Slots.Count(slot => slot.Family == MatchPositionFamily.Midfield)}-{side.Slots.Count(slot => slot.Family == MatchPositionFamily.Attack)}";

    /// <summary>Stands both sides in the board's shape for a formation preset.</summary>
    /// <param name="input">The snapshot.</param>
    /// <param name="home">The home side's formation.</param>
    /// <param name="away">The away side's formation.</param>
    internal static MatchInputV1 Stand(MatchInputV1 input, FormationPreset home, FormationPreset away) =>
        input with { Home = Stand(input.Home, home), Away = Stand(input.Away, away) };

    private static MatchSideV1 Stand(MatchSideV1 side, FormationPreset preset)
    {
        var layout = FormationLayouts.DefaultSlots(preset);

        return side with
        {
            Slots =
            [
                .. side.Slots.Select(slot =>
                {
                    var entry = layout.Single(candidate => candidate.SlotNumber == slot.SlotNumber);

                    return slot with
                    {
                        Family = EngineVocabulary.Family(entry.PositionFamily),
                        Role = EngineVocabulary.Role(entry.Role),
                        X = entry.NormalizedX,
                        Y = entry.NormalizedY,
                    };
                }),
            ],
        };
    }

    private static Line Play(MatchInputV1 input, EngineRulesV2 rules)
    {
        var result = MatchSimulator.Simulate(input, rules);

        int Count(EngineEventType type) => result.Events.Count(entry => entry.Type == type);

        return new Line(
            result.HomeGoals + result.AwayGoals,
            result.Events.Count(entry => entry.Type == EngineEventType.Goal && entry.X is null),
            result.HomeGoals,
            result.AwayGoals,
            result.Home.Shots + result.Away.Shots,
            result.Home.Shots,
            result.Home.ShotsOnTarget,
            result.Events.Count(entry => entry.Type == EngineEventType.ShotSaved && entry.Side == MatchSide.Home),
            result.Events.Count(entry => entry.Type == EngineEventType.ShotBlocked && entry.Side == MatchSide.Home),
            result.Home.ShotsOnTarget + result.Away.ShotsOnTarget,
            Count(EngineEventType.ShotSaved),
            Count(EngineEventType.ShotBlocked),
            Count(EngineEventType.ShotOffTarget),
            Count(EngineEventType.Woodwork),
            Count(EngineEventType.PenaltyAwarded),
            result.Home.Fouls + result.Away.Fouls,
            result.Home.YellowCards + result.Away.YellowCards,
            result.Home.RedCards + result.Away.RedCards,
            result.Home.Corners + result.Away.Corners,
            result.Home.Offsides + result.Away.Offsides,
            result.PlayerLines.Sum(line => line.PassesAttempted),
            result.PlayerLines.Sum(line => line.PassesCompleted));
    }

    private static void Print(string label, IReadOnlyCollection<Line> lines)
    {
        var count = Math.Max(1, lines.Count);
        var shots = Math.Max(1, lines.Sum(line => line.Shots));
        var passes = Math.Max(1, lines.Sum(line => line.Passes));
        var home = lines.Count(line => line.HomeGoals > line.AwayGoals);
        var draw = lines.Count(line => line.HomeGoals == line.AwayGoals);
        var away = lines.Count - home - draw;
        var goals = lines.Select(line => (double)line.Goals).OrderBy(value => value).ToArray();

        double Average(Func<Line, int> pick) => lines.Sum(pick) / (double)count;

        Console.WriteLine($"-- {label} ({lines.Count} matches)");
        Console.WriteLine($"  goals            {Average(line => line.Goals),8:F2}   2.60 - 2.90   (median {goals[goals.Length / 2]:F0}, max {goals[^1]:F0})");
        Console.WriteLine($"  shots            {Average(line => line.Shots),8:F1}   22 - 28");
        Console.WriteLine($"  goals without a shot {Average(line => line.GoalsWithoutShot),6:F2}");
        Console.WriteLine($"  home on target/saved/blocked {Average(line => line.HomeOnTarget),6:F2} {Average(line => line.HomeSaved),6:F2} {Average(line => line.HomeBlocked),6:F2}   away {Average(line => line.OnTarget - line.HomeOnTarget),6:F2} {Average(line => line.Saved - line.HomeSaved),6:F2} {Average(line => line.Blocked - line.HomeBlocked),6:F2}");
        Console.WriteLine($"  home/away goals  {Average(line => line.HomeGoals),8:F2} {Average(line => line.AwayGoals),8:F2}   shots {Average(line => line.HomeShots),6:F1} {Average(line => line.Shots - line.HomeShots),6:F1}");
        Console.WriteLine($"  on target        {100.0 * lines.Sum(line => line.OnTarget) / shots,7:F1}%   32% - 38%");
        Console.WriteLine($"  saved/blk/off/wd {Average(line => line.Saved),5:F1} {Average(line => line.Blocked),5:F1} {Average(line => line.OffTarget),5:F1} {Average(line => line.Woodwork),5:F1}");
        Console.WriteLine($"  pass completion  {100.0 * lines.Sum(line => line.Completed) / passes,7:F1}%   75% - 85%   ({Average(line => line.Passes):F0} attempted)");
        Console.WriteLine($"  yellow cards     {Average(line => line.Yellows),8:F2}   3.0 - 4.5   (reds {Average(line => line.Reds):F2})");
        Console.WriteLine($"  fouls/pens/corn/off {Average(line => line.Fouls),5:F1} {Average(line => line.Penalties),5:F2} {Average(line => line.Corners),5:F1} {Average(line => line.Offsides),5:F1}");
        Console.WriteLine($"  home/draw/away   {100.0 * home / count,5:F1}% {100.0 * draw / count,5:F1}% {100.0 * away / count,5:F1}%   42-48 / 22-26 / 28-34");
        Console.WriteLine();
    }
}
