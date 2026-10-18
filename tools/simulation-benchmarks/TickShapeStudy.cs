using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Highlights;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Spatial;
using Tactical11Manager.MatchEngine.Tick;
using Rec = Tactical11Manager.SimulationBenchmarks.TickPlanStudy.Rec;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// The shape study (plan snoopy-toasting-hamster, Milestone 1): what a shape does from kick-off, and what a change of shape does in the middle
/// of a match, played with squads whose players are shaped by their position.
/// </summary>
/// <remarks>
/// <para>
/// It reuses the match-plan study's paired engine (<see cref="TickPlanStudy"/>): the baseline of a base is played once and kept, a cell plays the
/// same seeds with a rule on the home side, and the effect is read over the matches in which the rule fired. Because the engine is deterministic
/// and the plan runner draws nothing, a pair is identical until the rule fires.
/// </para>
/// <para>
/// A cell is written as text: <c>kick:442-433</c> (both sides start in their shapes, no baseline), <c>switch:442-433-v-352@60:natural</c>
/// (the home side starts in 4-4-2 against a 3-5-2 and moves to 4-3-3 at the first stoppage after minute 60; the placement is <c>natural</c>,
/// <c>bestfit</c> or <c>mismatch</c>, and <c>+wings</c> or <c>+centre</c> adds a pass focus), <c>control:...</c> (the same substitutions without the
/// change of shape), <c>noop:...</c> (a rule that changes nothing: it must reproduce the baseline exactly) and
/// <c>custom:...:positions|roles|focus</c> (the new shape's positions with the old roles, the old positions with the new roles, or the pass focus
/// alone). A change that needs a fresh player is read against a control that makes the same substitutions, so the switch is not credited with
/// fresh legs. <c>TS_CELLS</c> narrows the cells to a comma list of id prefixes. Every match is recorded, and the recording is reduced on the spot
/// to the zone counts of <see cref="Zones"/>.
/// </para>
/// </remarks>
internal static partial class TickShapeStudy
{
    private const int Entities = TickMatchRecording.Entities;
    private const int Half = Entities / 2;

    /// <summary>The minutes after a change the shots and goals are binned in, and how many bins are read.</summary>
    private const int BinMinutes = 5;
    private const int Bins = 6;

    /// <summary>The cells the smoke run plays: one pairing, every kind of cell.</summary>
    private const string SmokeCells =
        "kick:442-433,switch:442-433-v-352@60:natural,control:442-433-v-352@60,switch:442-433-v-352@60:bestfit,switch:442-433-v-352@60:mismatch,"
        + "switch:442-433-v-352@1:natural,custom:442-433-v-352@60:positions,custom:442-433-v-352@60:roles,custom:442-433-v-352@60:focus,noop:442-433-v-352@60";

    /// <summary>The shapes the points matrix covers unless <c>TS_SHAPES</c> says otherwise.</summary>
    private const string MatrixShapes = "442,433,4231,352,541";

    private static readonly Dictionary<string, FormationPreset> Shapes = new()
    {
        ["442"] = FormationPreset.FourFourTwo,
        ["433"] = FormationPreset.FourThreeThree,
        ["4231"] = FormationPreset.FourTwoThreeOne,
        ["352"] = FormationPreset.ThreeFiveTwo,
        ["541"] = FormationPreset.FiveFourOne,
        ["343"] = FormationPreset.ThreeFourThree,
        ["451"] = FormationPreset.FourFiveOne,
        ["4411"] = FormationPreset.FourFourOneOne,
        ["4141"] = FormationPreset.FourOneFourOne,
    };

    private static readonly Regex Grammar = new(
        @"^(?<kind>kick|switch|control|noop|custom|oop):(?<a>\d+)-(?<b>\d+)(?:-v-(?<o>\d+))?(?:@(?<m>\d+))?(?::(?<rest>[a-z+]+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, ShapedSquad> Squads = [];

    private enum Kind
    {
        Kickoff,
        Switch,
        Control,
        NoOp,
        Custom,
        Misplaced,
    }

    private enum Placement
    {
        Natural,
        BestFit,
        Mismatch,
    }

    /// <summary>The kick-off misplacement of an <c>oop</c> cell: which two pairs of the eleven change places.</summary>
    private enum Swap
    {
        Both,
        Striker,
        Wing,
    }

    private enum Ablation
    {
        Positions,
        Roles,
        Focus,
    }

    /// <summary>Runs the study.</summary>
    /// <param name="matches">How many matches a cell.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="spec">The cells as a comma list of cell texts, "squads" to print the squads and their moves, or empty for the smoke cells.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules, string spec)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (spec.Equals("squads", StringComparison.OrdinalIgnoreCase))
        {
            PrintSquads();

            return;
        }

        var directory = Path.Combine(".run-logs", "plans", "shape");

        Directory.CreateDirectory(directory);

        var matrixShapes = (Environment.GetEnvironmentVariable("TS_SHAPES") ?? MatrixShapes).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (spec.Equals("matrix", StringComparison.OrdinalIgnoreCase))
        {
            var report = Matrix.Read(directory, seed, matrixShapes);

            Console.Write(report);
            File.WriteAllText(Path.Combine(directory, $"matrix-{seed}.txt"), report);

            return;
        }

        if (spec.Equals("h3", StringComparison.OrdinalIgnoreCase))
        {
            var report = H3.Read(directory, seed, matrixShapes);

            Console.Write(report);
            File.WriteAllText(Path.Combine(directory, $"h3-{seed}.txt"), report);

            return;
        }

        if (spec.Equals("grid", StringComparison.OrdinalIgnoreCase))
        {
            // Every ordered pairing of the shapes: the home side's shape first.
            spec = string.Join(',', matrixShapes.SelectMany(home => matrixShapes.Select(away => $"kick:{home}-{away}")));
        }

        var filter = (Environment.GetEnvironmentVariable("TS_CELLS") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cells = (spec.Length == 0 ? SmokeCells : spec)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Parse)
            .Where(cell => filter.Length == 0 || filter.Any(prefix => cell.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var summary = Path.Combine(directory, $"cells-{seed}.tsv");
        var kickoffs = Path.Combine(directory, $"kickoff-{seed}.tsv");
        var zonesFile = Path.Combine(directory, $"zones-{seed}.tsv");
        var total = 0.0;

        if (!File.Exists(summary))
        {
            File.WriteAllText(summary, TickPlanStudy.Header());
        }

        if (!File.Exists(kickoffs))
        {
            File.WriteAllText(kickoffs, "cell\thome\taway\tseed\tmatches\tpts\tse_pts\twin\tdraw\tlose\thome_goals\taway_goals\thome_shots\taway_shots\thome_crosses\taway_crosses\tspent_s\n");
        }

        if (!File.Exists(zonesFile))
        {
            File.WriteAllText(zonesFile, "cell\tarm\tmatches\t" + string.Join('\t', Enumerable.Range(0, Zones.Length).Select(index => $"z{index}")) + "\n");
        }

        Console.WriteLine($"== tickshape: {cells.Length} cell(s), {matches} matches each, seed {seed} ==");

        foreach (var cell in cells)
        {
            var watch = Stopwatch.StartNew();
            var shared = 0.0;
            var file = Regex.Replace(cell.Id, "[^a-z0-9+-]+", "_");

            if (cell.Kind == Kind.Kickoff)
            {
                var home = SquadOf(1, cell.From, []);
                var away = SquadOf(2, cell.To, []);
                var recs = Play(index => LaboratoryFixtures.BuildShaped(seed + (ulong)index, home, away, rules), null, matches, rules);

                watch.Stop();

                var own = watch.Elapsed.TotalSeconds;
                var line = AnalyseKickoff(cell, recs, seed, own, out var text);

                Console.Write(text);
                File.AppendAllText(kickoffs, line);
                File.AppendAllText(zonesFile, ZoneRow(cell.Id, "kickoff", recs));
                File.WriteAllText(Path.Combine(directory, $"{file}-{seed}.txt"), text);
                TickPlanStudy.AppendLedger("shape", cell.Id, matches, seed, own);
                total += own;

                continue;
            }

            var homeSquad = SquadOf(1, cell.From, [cell.To]);
            var awaySquad = SquadOf(2, cell.Opponent, []);
            var move = homeSquad.Moves[cell.To];
            Func<int, MatchInputV1> build = index => LaboratoryFixtures.BuildShaped(seed + (ulong)index, homeSquad, awaySquad, rules);

            var baseline = TickPlanStudy.Cached(
                Path.Combine(directory, $"base-{cell.Base}-{seed}-{matches}.json"),
                "shape",
                $"baseline-{cell.Base}",
                matches,
                seed,
                () => Play(build, null, matches, rules),
                out var baselineSeconds);

            shared += baselineSeconds;

            Rec[] Arm(string name, MatchRuleV1 rule)
            {
                var recs = TickPlanStudy.Cached(
                    Path.Combine(directory, $"arm-{cell.Base}-{name}-{seed}-{matches}.json"),
                    "shape",
                    $"{name}-{cell.Base}",
                    matches,
                    seed,
                    () => Play(build, [rule], matches, rules),
                    out var seconds);

                shared += seconds;

                return recs;
            }

            var control = new MatchRuleV1 { Ordinal = 1, FromMinute = cell.Minute, Substitutions = [.. move.Substitutions] };
            var needsControl = cell.Kind is Kind.Switch or Kind.Custom && cell.Ablation != Ablation.Focus && cell.Placement != Placement.BestFit && move.Substitutions.Count > 0;
            var without = needsControl ? Arm($"sub{cell.Minute}", control) : baseline;

            // The cell's own arm is kept too, so that it can later be read paired against the plain baseline or against another cell of its base.
            var cellSeconds = 0.0;
            var with = cell.Kind switch
            {
                Kind.Control => Arm($"sub{cell.Minute}", control),
                Kind.Misplaced => Played(TickPlanStudy.Cached(
                    Path.Combine(directory, $"cell-{file}-{seed}-{matches}.json"),
                    "shape",
                    cell.Id,
                    matches,
                    seed,
                    () => Play(index => Misplaced(build(index), cell.Swap), null, matches, rules),
                    out cellSeconds)),
                _ => TickPlanStudy.Cached(
                    Path.Combine(directory, $"cell-{file}-{seed}-{matches}.json"),
                    "shape",
                    cell.Id,
                    matches,
                    seed,
                    () => Play(build, [RuleOf(cell, move)], matches, rules),
                    out cellSeconds),
            };

            // A match in which the rule could not make all its substitutions (the assistant had already used a bench player the move needed)
            // is not the move that was asked for: it is left out of the effect. The reason is in the state at the change, which both arms share.
            var expected = SubstitutionsExpected(cell, move);

            foreach (var rec in with.Where(rec => expected > 0 && rec.FireSeq > 0 && rec.RuleSubstitutions != expected))
            {
                rec.Excluded = true;
            }

            var spent = cellSeconds + shared;
            var label = new TickPlanStudy.CellLabel(cell.Id, "shape", cell.Base, cell.Plain, $"{cell.From.ToCode()} against {cell.Opponent.ToCode()}, position-shaped squads of 16, home against away", false, false);
            var row = TickPlanStudy.Analyse(label, without, with, seed, matches, spent, out var report);
            var extras = Extras(expected, without, with);
            var included = Enumerable.Range(0, matches).Where(index => with[index].FireSeq > 0 && !with[index].Excluded).ToArray();

            // A cell read against its control is read against the plain baseline as well: the control's substitutes play out of position in the
            // old shape, so the control alone overstates what the change gains.
            if (!ReferenceEquals(without, baseline))
            {
                var baseLabel = label with { Id = cell.Id + "|vs-base", Plain = cell.Plain + ", against the plain baseline" };

                row += TickPlanStudy.Analyse(baseLabel, baseline, with, seed, matches, 0, out var baseReport);
                report += baseReport;
            }

            Console.Write(report);
            Console.Write(extras);
            File.AppendAllText(summary, row);
            File.AppendAllText(zonesFile, ZoneRow(cell.Id, "with", included.Select(index => with[index])) + ZoneRow(cell.Id, "without", included.Select(index => without[index])));
            File.WriteAllText(Path.Combine(directory, $"{file}-{seed}.txt"), report + extras);
            total += spent;
        }

        Console.WriteLine($"  run time this call {total:F0} s over {cells.Length} cell(s) (a baseline or a control is counted in the first cell that played it; the ledger counts it once)");
    }

    // ---------------------------------------------------------------- cells

    private sealed record ShapeCell(
        string Id,
        Kind Kind,
        FormationPreset From,
        FormationPreset To,
        FormationPreset Opponent,
        int Minute,
        Placement Placement,
        Ablation Ablation,
        MatchInstructionChangesV1? Changes,
        Swap Swap = Swap.Both)
    {
        /// <summary>Gets the base: the squads the cell is played on, which fix its baseline.</summary>
        public string Base => Kind == Kind.Kickoff
            ? $"{From.ToCode()}-v-{To.ToCode()}"
            : $"{From.ToCode()}-to-{To.ToCode()}-v-{Opponent.ToCode()}";

        /// <summary>Gets the cell in plain words.</summary>
        public string Plain
        {
            get
            {
                var when = Minute <= 1 ? "the first stoppage" : $"{Minute}'";
                var focus = Changes?.PassFocus is { } passFocus ? $" and pass focus {passFocus}" : string.Empty;
                var move = $"{From.ToCode()} to {To.ToCode()} at {when}";

                return Kind switch
                {
                    Kind.Kickoff => $"{From.ToCode()} against {To.ToCode()} from kick-off",
                    Kind.Switch => Placement switch
                    {
                        Placement.Natural => $"{move} with every player in his natural slot{focus}",
                        Placement.BestFit => $"{move} left to the engine's own fit, no substitutions{focus}",
                        _ => $"{move} with the right players deliberately misplaced{focus}",
                    },
                    Kind.Control => $"control: the substitutions of {move}, without the change of shape",
                    Kind.NoOp => $"control: a rule at {when} that changes nothing",
                    Kind.Misplaced => $"{From.ToCode()} from kick-off with {Swap switch { Swap.Striker => "a striker at centre back", Swap.Wing => "a centre back on the wing", _ => "a striker at centre back and a centre back on the wing" }}",
                    _ => Ablation switch
                    {
                        Ablation.Positions => $"{move}: the new shape's positions with the old roles",
                        Ablation.Roles => $"{move}: the old positions with the new roles",
                        _ => $"{move}: the pass focus alone{focus}",
                    },
                };
            }
        }
    }

    private static ShapeCell Parse(string text)
    {
        var match = Grammar.Match(text.ToLowerInvariant());

        if (!match.Success)
        {
            throw new ArgumentException($"'{text}' is not a cell. Try kick:442-433, switch:442-433-v-352@60:natural, control:442-433-v-352@60, noop:442-433-v-352@60 or custom:442-433-v-352@60:positions.");
        }

        FormationPreset Shape(string code) => Shapes.TryGetValue(code, out var preset) ? preset : throw new ArgumentException($"No shape '{code}'. Known: {string.Join(", ", Shapes.Keys)}.");

        var kind = match.Groups["kind"].Value switch
        {
            "kick" => Kind.Kickoff,
            "switch" => Kind.Switch,
            "control" => Kind.Control,
            "noop" => Kind.NoOp,
            "oop" => Kind.Misplaced,
            _ => Kind.Custom,
        };
        var tokens = match.Groups["rest"].Value.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var placement = tokens.Contains("bestfit") ? Placement.BestFit : (tokens.Contains("mismatch") ? Placement.Mismatch : Placement.Natural);
        var ablation = tokens.Contains("roles") ? Ablation.Roles : (tokens.Contains("focus") ? Ablation.Focus : Ablation.Positions);
        MatchInstructionChangesV1? changes = tokens.Contains("wings")
            ? new MatchInstructionChangesV1 { PassFocus = MatchPassFocus.Wings }
            : (tokens.Contains("centre") ? new MatchInstructionChangesV1 { PassFocus = MatchPassFocus.Centre } : null);

        if (kind == Kind.Custom && ablation == Ablation.Focus)
        {
            changes ??= new MatchInstructionChangesV1 { PassFocus = MatchPassFocus.Wings };
        }

        if (kind != Kind.Kickoff && !match.Groups["o"].Success)
        {
            throw new ArgumentException($"'{text}' needs an opponent: -v-352.");
        }

        return new ShapeCell(
            text.ToLowerInvariant(),
            kind,
            Shape(match.Groups["a"].Value),
            Shape(match.Groups["b"].Value),
            match.Groups["o"].Success ? Shape(match.Groups["o"].Value) : Shape(match.Groups["b"].Value),
            match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 60,
            placement,
            ablation,
            changes,
            tokens.Contains("striker") ? Swap.Striker : (tokens.Contains("wing") ? Swap.Wing : Swap.Both));
    }

    private static ShapedSquad SquadOf(int clubSeed, FormationPreset start, FormationPreset[] targets)
    {
        var key = $"{clubSeed}:{start}:{string.Join(',', targets)}";

        if (!Squads.TryGetValue(key, out var squad))
        {
            Squads[key] = squad = LaboratoryFixtures.Shaped(clubSeed, start, targets);
        }

        return squad;
    }

    /// <summary>
    /// The home side's eleven with two of its pairs changed places at kick-off: slot 10 (a striker) with slot 3 (a centre back), and slot 4 (the other
    /// centre back) with slot 6 (the right wide midfielder), in the 4-4-2 the misplacement cells start in.
    /// </summary>
    private static MatchInputV1 Misplaced(MatchInputV1 input, Swap swap)
    {
        var pairs = swap switch
        {
            Swap.Striker => new[] { (10, 3) },
            Swap.Wing => new[] { (4, 6) },
            _ => new[] { (10, 3), (4, 6) },
        };
        var slots = input.Home.Slots.ToDictionary(slot => slot.SlotNumber);

        foreach (var (first, second) in pairs)
        {
            slots[first] = slots[first] with { ParticipantId = input.Home.Slots.Single(slot => slot.SlotNumber == second).ParticipantId };
            slots[second] = slots[second] with { ParticipantId = input.Home.Slots.Single(slot => slot.SlotNumber == first).ParticipantId };
        }

        return input with { Home = input.Home with { Slots = [.. input.Home.Slots.Select(slot => slots[slot.SlotNumber])] } };
    }

    /// <summary>A cell that changes the eleven at kick-off has no rule to fire: every match counts, from minute 0.</summary>
    private static Rec[] Played(Rec[] recs)
    {
        foreach (var rec in recs)
        {
            rec.FireSeq = 1;
            rec.FireMinute = 0;
        }

        return recs;
    }

    /// <summary>How many substitutions a cell's rule is meant to make: the move's, unless the cell names none.</summary>
    private static int SubstitutionsExpected(ShapeCell cell, ShapedMove move) => cell.Kind switch
    {
        Kind.Control => move.Substitutions.Count,
        Kind.Switch when cell.Placement != Placement.BestFit => move.Substitutions.Count,
        Kind.Custom when cell.Ablation != Ablation.Focus => move.Substitutions.Count,
        _ => 0,
    };

    /// <summary>The rule the home side carries for a cell.</summary>
    private static MatchRuleV1 RuleOf(ShapeCell cell, ShapedMove move)
    {
        var rule = new MatchRuleV1 { Ordinal = 1, FromMinute = cell.Minute };

        switch (cell.Kind)
        {
            case Kind.NoOp:
                return rule;

            case Kind.Control:
                return rule with { Substitutions = [.. move.Substitutions] };

            case Kind.Switch:
                return rule with
                {
                    FormationCode = cell.To.ToCode(),
                    Slots = TickPlanSmoke.SlotsOf(cell.To),
                    Overrides = cell.Placement switch
                    {
                        Placement.Natural => move.Natural,
                        Placement.Mismatch => move.Mismatch,
                        _ => [],
                    },
                    Substitutions = cell.Placement == Placement.BestFit ? [] : [.. move.Substitutions],
                    Instructions = cell.Changes,
                };

            default:
                return cell.Ablation == Ablation.Focus
                    ? rule with { Instructions = cell.Changes }
                    : rule with
                    {
                        FormationCode = cell.To.ToCode(),
                        Slots = CustomSlots(cell, move),
                        Overrides = move.Natural,
                        Substitutions = [.. move.Substitutions],
                        Instructions = cell.Changes,
                    };
        }
    }

    /// <summary>
    /// The new shape's slots with half of each swapped for the old shape's: the new positions carrying the roles the players had (the player who
    /// takes new slot n brings the role of the seat he stood in), or the old positions carrying the new roles.
    /// </summary>
    private static IReadOnlyList<MatchSlotV1> CustomSlots(ShapeCell cell, ShapedMove move)
    {
        var start = FormationLayouts.DefaultSlots(cell.From).ToDictionary(slot => slot.SlotNumber);
        var seatOfSlot = move.Natural.ToDictionary(placement => placement.SlotNumber, placement => move.SeatOf[placement.ParticipantId]);

        return
        [
            .. FormationLayouts.DefaultSlots(cell.To).Select(target =>
            {
                var old = target.SlotNumber == 1 ? start[1] : start[seatOfSlot[target.SlotNumber]];
                var (family, role, x, y) = cell.Ablation == Ablation.Positions
                    ? (old.PositionFamily, old.Role, target.NormalizedX, target.NormalizedY)
                    : (target.PositionFamily, target.Role, old.NormalizedX, old.NormalizedY);

                return new MatchSlotV1
                {
                    SlotNumber = target.SlotNumber,
                    Family = EngineVocabulary.Family(family),
                    Role = EngineVocabulary.Role(role),
                    X = x,
                    Y = y,
                    ParticipantId = Guid.Empty,
                };
            }),
        ];
    }

    private static void PrintSquads()
    {
        var starts = new[] { "442", "433", "4231", "352", "541", "343", "451", "4141" };

        foreach (var code in starts)
        {
            var start = Shapes[code];

            foreach (var line in SquadOf(1, start, []).Lines)
            {
                Console.WriteLine(line);
            }

            foreach (var other in starts.Where(other => other != code))
            {
                try
                {
                    Console.WriteLine("   " + SquadOf(1, start, [Shapes[other]]).Moves[Shapes[other]].Description);
                }
                catch (InvalidOperationException problem)
                {
                    Console.WriteLine($"   {start.ToCode()} to {Shapes[other].ToCode()}: FAILED, {problem.Message}");
                }
            }

            Console.WriteLine();
        }
    }

    // ---------------------------------------------------------------- playing

    /// <summary>Plays the matches of a pairing with the rule on the home side, every match recorded and reduced to its figures and zone counts.</summary>
    private static Rec[] Play(Func<int, MatchInputV1> build, MatchRuleV1[]? rule, int matches, EngineRulesV2 rules)
    {
        var recs = new Rec[matches];

        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            var input = build(index);

            if (rule is not null)
            {
                input = input with { Home = input.Home with { MatchPlan = new MatchPlanV1 { Rules = rule } } };
            }

            var recorder = new MatchPassageRecorder();
            var result = MatchSimulator.Simulate(input, rules, null, recorder, TickMatchEngine.Instance);
            var rec = TickPlanStudy.Read(input, result, null);

            if (recorder.Tick is { } recording)
            {
                rec.Zones = Zones.Reduce(recording);
            }

            recs[index] = rec;
        });

        return recs;
    }

    // ---------------------------------------------------------------- reading

    private static string AnalyseKickoff(ShapeCell cell, Rec[] recs, ulong seed, double spent, out string text)
    {
        var n = recs.Length;
        var report = new StringBuilder();
        var points = recs.Select(rec => rec.HomeGoals > rec.AwayGoals ? 3.0 : (rec.HomeGoals == rec.AwayGoals ? 1.0 : 0.0)).ToArray();
        var mean = points.Average();
        var se = n > 1 ? Math.Sqrt(points.Sum(value => (value - mean) * (value - mean)) / (n - 1) / n) : 0;

        double Rate(Func<Rec, bool> test) => 100.0 * recs.Count(test) / n;

        var win = Rate(rec => rec.HomeGoals > rec.AwayGoals);
        var draw = Rate(rec => rec.HomeGoals == rec.AwayGoals);
        var lose = Rate(rec => rec.HomeGoals < rec.AwayGoals);

        report.Append(CultureInfo.InvariantCulture, $"== {cell.Id}: {cell.Plain}; position-shaped squads of 16, home against away; seed {seed}, {spent:F0} s ==\n");
        report.Append(CultureInfo.InvariantCulture, $"   home points {mean:F2} ±{1.96 * se:F2}   win / draw / lose % {win:F0}/{draw:F0}/{lose:F0}\n");
        report.Append(CultureInfo.InvariantCulture, $"   goals {recs.Average(rec => rec.HomeGoals):F2} - {recs.Average(rec => rec.AwayGoals):F2}   shots {recs.Average(rec => rec.HomeShots):F1} - {recs.Average(rec => rec.AwayShots):F1}   crosses {recs.Average(rec => rec.HomeCrosses):F1} - {recs.Average(rec => rec.AwayCrosses):F1}\n");
        report.Append(ZoneText(Sum(recs), n, 0, "home"));
        report.Append(ZoneText(Sum(recs), n, 1, "away"));
        report.Append('\n');
        text = report.ToString();

        string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        return string.Join(
            '\t',
            cell.Id,
            cell.From.ToCode(),
            cell.To.ToCode(),
            seed,
            n,
            F(mean),
            F(se),
            F(win),
            F(draw),
            F(lose),
            F(recs.Average(rec => rec.HomeGoals)),
            F(recs.Average(rec => rec.AwayGoals)),
            F(recs.Average(rec => rec.HomeShots)),
            F(recs.Average(rec => rec.AwayShots)),
            F(recs.Average(rec => rec.HomeCrosses)),
            F(recs.Average(rec => rec.AwayCrosses)),
            F(spent)) + "\n";
    }

    /// <summary>What a paired cell reports beyond the match-plan study's table: the plumbing checks, the bins after the change, and the zones.</summary>
    private static string Extras(int substitutionsExpected, Rec[] without, Rec[] with)
    {
        var report = new StringBuilder();
        var fired = Enumerable.Range(0, with.Length).Where(index => with[index].FireSeq > 0 && !with[index].Excluded).ToArray();
        var rest = Enumerable.Range(0, with.Length).Where(index => with[index].FireSeq == 0).ToArray();

        bool Same(int index) =>
            with[index].HomeGoals == without[index].HomeGoals
            && with[index].AwayGoals == without[index].AwayGoals
            && with[index].HomeShots == without[index].HomeShots
            && with[index].AwayShots == without[index].AwayShots
            && with[index].HomeCrosses == without[index].HomeCrosses
            && with[index].AwayCrosses == without[index].AwayCrosses
            && with[index].Zones.AsSpan().SequenceEqual(without[index].Zones);

        report.Append(CultureInfo.InvariantCulture, $"   checks: the rule fired in {fired.Length} of {with.Length}; the {rest.Length} matches it did not fire in are identical to their arm in {rest.Count(Same)}");

        if (substitutionsExpected > 0)
        {
            report.Append(CultureInfo.InvariantCulture, $"; {with.Count(rec => rec.Excluded)} fired matches left out because the rule could not make all {substitutionsExpected} substitutions");
        }

        report.Append('\n');

        if (fired.Length > 0)
        {
            report.Append("   after the change, per 5 minutes, with minus without (95%):\n");
            report.Append("     minutes after   home shots       away shots       home goals       away goals\n");

            for (var bin = 0; bin < Bins; bin++)
            {
                // The minutes of the bin are counted from the minute the rule fired in the played match: the two arms are identical until then.
                string Bin(Func<Rec, List<int>> minutesOf)
                {
                    int InBin(Rec rec, Rec played) => minutesOf(rec).Count(minute => minute > played.FireMinute + (bin * BinMinutes) && minute <= played.FireMinute + ((bin + 1) * BinMinutes));

                    var values = fired.Select(index => (double)(InBin(with[index], with[index]) - InBin(without[index], with[index]))).ToArray();
                    var mean = values.Average();
                    var se = values.Length > 1 ? Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1) / values.Length) : 0;

                    return $"{mean.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)} ±{(1.96 * se).ToString("F2", CultureInfo.InvariantCulture)}";
                }

                report.Append(CultureInfo.InvariantCulture, $"     {bin * BinMinutes,3}-{(bin + 1) * BinMinutes,3}      {Bin(rec => rec.HomeShotMinutes),-15}  {Bin(rec => rec.AwayShotMinutes),-15}  {Bin(rec => rec.HomeGoalMinutes),-15}  {Bin(rec => rec.AwayGoalMinutes),-15}\n");
            }

            report.Append(ZoneText(Sum(fired.Select(index => with[index])), fired.Length, 0, "with, the matches that fired, home"));
            report.Append(ZoneText(Sum(fired.Select(index => without[index])), fired.Length, 0, "without, the same matches, home"));
        }

        report.Append('\n');

        return report.ToString();
    }

    // ---------------------------------------------------------------- the points matrix

    /// <summary>
    /// H1: the points a home side takes from each ordered pairing of shapes, read as <c>points(A home, B away) = home + strength(A) - strength(B) +
    /// matchup(A, B) + home bonus of the pairing</c>. A strength is half the difference between a shape's mean as home side and as away side; the
    /// matchup is what is left of the pairing's two-way difference once strengths are taken out, and the home bonus is what the two orders share.
    /// Each term is a linear sum of the cells' means, so its interval comes from the cells' own standard errors.
    /// </summary>
    private static class Matrix
    {
        /// <summary>A term counts as small when it is under this many points and its interval stays inside <see cref="IntervalBound"/>.</summary>
        private const double SmallTerm = 0.10;
        private const double IntervalBound = 0.15;

        public static string Read(string directory, ulong seed, string[] keys)
        {
            var file = Path.Combine(directory, $"kickoff-{seed}.tsv");
            var zoneFile = Path.Combine(directory, $"zones-{seed}.tsv");

            if (!File.Exists(file))
            {
                return $"No {file}: play the grid first (tickshape <matches> {seed} grid).\n";
            }

            var rows = new Dictionary<string, string[]>();

            foreach (var line in File.ReadLines(file).Skip(1))
            {
                var fields = line.Split('\t');

                rows[fields[0]] = fields;
            }

            var zoneRows = new Dictionary<string, long[]>();

            if (File.Exists(zoneFile))
            {
                foreach (var line in File.ReadLines(zoneFile).Skip(1))
                {
                    var fields = line.Split('\t');

                    if (fields[1] == "kickoff")
                    {
                        zoneRows[fields[0]] = [.. fields.Skip(3).Select(value => long.Parse(value, CultureInfo.InvariantCulture))];
                    }
                }
            }

            var k = keys.Length;
            var points = new double[k, k];
            var error = new double[k, k];
            var matches = new int[k, k];
            var values = new double[8][,];

            for (var index = 0; index < values.Length; index++)
            {
                values[index] = new double[k, k];
            }

            for (var a = 0; a < k; a++)
            {
                for (var b = 0; b < k; b++)
                {
                    if (!rows.TryGetValue($"kick:{keys[a]}-{keys[b]}", out var row))
                    {
                        return $"The grid is not complete: kick:{keys[a]}-{keys[b]} is missing from {file}.\n";
                    }

                    double At(int column) => double.Parse(row[column], CultureInfo.InvariantCulture);

                    matches[a, b] = (int)At(4);
                    points[a, b] = At(5);
                    error[a, b] = At(6);

                    // home goals, away goals, home shots, away shots, home crosses, away crosses
                    for (var index = 0; index < 6; index++)
                    {
                        values[index][a, b] = At(10 + index);
                    }
                }
            }

            string Plain(string key) => Shapes[key].ToCode();

            var text = new StringBuilder();
            var all = Enumerable.Range(0, k).SelectMany(a => Enumerable.Range(0, k).Select(b => points[a, b])).Average();
            var cells = matches.Cast<int>().Sum();

            text.Append(CultureInfo.InvariantCulture, $"== tickshape matrix, seed {seed}: {k} shapes, {k * k} ordered pairings, {cells} matches ({matches.Cast<int>().Min()}-{matches.Cast<int>().Max()} a pairing); position-shaped squads of 16, uniform-16 keeper ==\n\n");
            text.Append("Home points per ordered pairing (home side down, away side across), ±95%:\n");
            text.Append(Row("home \\ away", keys.Select(Plain), 12));

            for (var a = 0; a < k; a++)
            {
                text.Append(Row(Plain(keys[a]), Enumerable.Range(0, k).Select(b => $"{points[a, b]:F2} ±{1.96 * error[a, b]:F2}"), 12));
            }

            text.Append(CultureInfo.InvariantCulture, $"\nMean home points over all pairings: {all:F3}.\n\n");

            // Strengths: s(a) = 1/(2k) * sum over b != a of (P(a,b) - P(b,a)).
            double[,] StrengthCoefficients(int a)
            {
                var coefficient = new double[k, k];

                for (var b = 0; b < k; b++)
                {
                    if (b != a)
                    {
                        coefficient[a, b] += 0.5 / k;
                        coefficient[b, a] -= 0.5 / k;
                    }
                }

                return coefficient;
            }

            (double Value, double Half) Evaluate(double[,] coefficient)
            {
                double value = 0;
                double variance = 0;

                for (var a = 0; a < k; a++)
                {
                    for (var b = 0; b < k; b++)
                    {
                        value += coefficient[a, b] * points[a, b];
                        variance += coefficient[a, b] * coefficient[a, b] * error[a, b] * error[a, b];
                    }
                }

                return (value, 1.96 * Math.Sqrt(variance));
            }

            double[,] Combine(params (double Weight, double[,] Coefficient)[] parts)
            {
                var sum = new double[k, k];

                foreach (var (weight, coefficient) in parts)
                {
                    for (var a = 0; a < k; a++)
                    {
                        for (var b = 0; b < k; b++)
                        {
                            sum[a, b] += weight * coefficient[a, b];
                        }
                    }
                }

                return sum;
            }

            string Flag((double Value, double Half) term) => Math.Abs(term.Value) < SmallTerm && Math.Abs(term.Value) + term.Half < IntervalBound ? "small" : "NOT small";

            var small = true;
            var strengths = new (double Value, double Half)[k];

            text.Append("Strength: points a shape adds as the stronger of two sides, against the average shape (zero-sum), ±95%:\n");

            for (var a = 0; a < k; a++)
            {
                strengths[a] = Evaluate(StrengthCoefficients(a));
                small &= Flag(strengths[a]) == "small";
                text.Append(CultureInfo.InvariantCulture, $"  {Plain(keys[a]),-8} {strengths[a].Value,+6:F3} ±{strengths[a].Half:F3}   {Flag(strengths[a])}\n");
            }

            // Matchup: mu(a,b) = (P(a,b) - P(b,a)) / 2 - (s(a) - s(b)), the part of a pairing a strength does not explain; mu(b,a) = -mu(a,b).
            text.Append("\nMatchup: points shape A takes from shape B beyond the strengths (A minus B; a negative value is B's), ±95%:\n");

            var matchup = new List<(string Pair, (double Value, double Half) Term)>();

            for (var a = 0; a < k; a++)
            {
                for (var b = a + 1; b < k; b++)
                {
                    var difference = new double[k, k];

                    difference[a, b] = 0.5;
                    difference[b, a] = -0.5;

                    var term = Evaluate(Combine((1, difference), (-1, StrengthCoefficients(a)), (1, StrengthCoefficients(b))));

                    matchup.Add(($"{Plain(keys[a])} against {Plain(keys[b])}", term));
                    small &= Flag(term) == "small";
                    text.Append(CultureInfo.InvariantCulture, $"  {Plain(keys[a])} against {Plain(keys[b]),-6} {term.Value,+6:F3} ±{term.Half:F3}   {Flag(term)}\n");
                }
            }

            // Home bonus of a pairing: (P(a,b) + P(b,a)) / 2 - mean of all; a == b is the mirror match.
            text.Append("\nHome bonus of the pairing beyond the average (what both orders share; a mirror match on the diagonal), ±95%:\n");

            var bonus = new List<(string Pair, (double Value, double Half) Term)>();

            for (var a = 0; a < k; a++)
            {
                for (var b = a; b < k; b++)
                {
                    var share = new double[k, k];
                    var mean = new double[k, k];

                    for (var c = 0; c < k; c++)
                    {
                        for (var d = 0; d < k; d++)
                        {
                            mean[c, d] = 1.0 / (k * k);
                        }
                    }

                    share[a, b] += 0.5;
                    share[b, a] += 0.5;

                    var term = Evaluate(Combine((1, share), (-1, mean)));

                    bonus.Add(($"{Plain(keys[a])} and {Plain(keys[b])}", term));
                    text.Append(CultureInfo.InvariantCulture, $"  {Plain(keys[a])} and {Plain(keys[b]),-6} {term.Value,+6:F3} ±{term.Half:F3}\n");
                }
            }

            var largest = matchup.OrderByDescending(entry => Math.Abs(entry.Term.Value)).First();
            var clear = matchup.Count(entry => Math.Abs(entry.Term.Value) > entry.Term.Half);

            text.Append(CultureInfo.InvariantCulture, $"\nH1 (shapes barely differ from kick-off, every strength and matchup term under {SmallTerm:F2} points with its interval inside ±{IntervalBound:F2}): {(small ? "CONFIRMED" : "NOT CONFIRMED")}.\n");
            text.Append(CultureInfo.InvariantCulture, $"  Largest strength {strengths.Max(term => Math.Abs(term.Value)):F3}; largest matchup {Math.Abs(largest.Term.Value):F3} ({largest.Pair}); {clear} of {matchup.Count} matchups have an interval clear of zero.\n\n");

            // Goals, shots and crosses of a shape as a side, over every opponent and both home and away.
            text.Append("Per match, a shape's side against all the others (home and away alike): goals for / against, shots for / against, crosses for / against:\n");

            for (var a = 0; a < k; a++)
            {
                double Mean(int forHome, int forAway) =>
                    (Enumerable.Range(0, k).Average(b => values[forHome][a, b]) + Enumerable.Range(0, k).Average(b => values[forAway][b, a])) / 2;

                text.Append(CultureInfo.InvariantCulture, $"  {Plain(keys[a]),-8} goals {Mean(0, 1),5:F2} / {Mean(1, 0),5:F2}   shots {Mean(2, 3),5:F1} / {Mean(3, 2),5:F1}   crosses {Mean(4, 5),5:F1} / {Mean(5, 4),5:F1}\n");
            }

            text.Append('\n');

            if (zoneRows.Count == 0)
            {
                return text.ToString();
            }

            // Zones (H3): each shape's own actions, over all pairings, in the shape's own frame; then everyone pooled.
            text.Append("Zones, per match, each shape in its own frame over every opponent (home and away pooled). Read the surplus per third, never pooled over thirds:\n\n");

            var pooled = new long[Zones.Length];
            var pooledMatches = 0L;

            for (var a = 0; a < k; a++)
            {
                var sums = new long[Zones.Length];
                var played = 0L;

                for (var b = 0; b < k; b++)
                {
                    if (zoneRows.TryGetValue($"kick:{keys[a]}-{keys[b]}", out var home))
                    {
                        Zones.AddSide(sums, home, 0);
                        played += matches[a, b];
                    }

                    if (zoneRows.TryGetValue($"kick:{keys[b]}-{keys[a]}", out var away))
                    {
                        Zones.AddSide(sums, away, 1);
                        played += matches[b, a];
                    }
                }

                Zones.AddSide(pooled, sums, 0);
                pooledMatches += played;
                text.Append(ZoneText(sums, (int)played, 0, $"{Plain(keys[a])}, {played} matches of that side"));
                text.Append('\n');
            }

            text.Append(ZoneText(pooled, (int)pooledMatches, 0, $"all shapes pooled, {pooledMatches} side-matches"));

            return text.ToString();
        }

        private static string Row(string head, IEnumerable<string> cells, int width) =>
            head.PadRight(12) + string.Concat(cells.Select(cell => cell.PadLeft(width))) + "\n";
    }

    // ---------------------------------------------------------------- zones

    private static long[] Sum(IEnumerable<Rec> recs)
    {
        var sums = new long[Zones.Length];

        foreach (var rec in recs.Where(rec => rec.Zones.Length == Zones.Length))
        {
            for (var index = 0; index < sums.Length; index++)
            {
                sums[index] += rec.Zones[index];
            }
        }

        return sums;
    }

    private static string ZoneRow(string id, string arm, IEnumerable<Rec> recs)
    {
        var list = recs.ToArray();

        return $"{id}\t{arm}\t{list.Length}\t{string.Join('\t', Sum(list))}\n";
    }

    private static string ZoneText(long[] sums, int matches, int side, string label)
    {
        if (matches == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        var names = new[] { "shots", "crosses", "passes tried", "passes completed" };

        text.Append(CultureInfo.InvariantCulture, $"   zones, {label} (own frame; per lane 0-2 across, thirds 0-2 from own goal), per match:\n");

        for (var kind = 0; kind < names.Length; kind++)
        {
            text.Append(CultureInfo.InvariantCulture, $"     {names[kind],-17}");

            for (var lane = 0; lane < Zones.Lanes; lane++)
            {
                text.Append(" [");

                for (var third = 0; third < Zones.Thirds; third++)
                {
                    text.Append(CultureInfo.InvariantCulture, $" {(double)sums[Zones.ActionAt(side, kind, lane, third)] / matches,6:F2}");
                }

                text.Append(" ]");
            }

            text.Append('\n');
        }

        foreach (var third in new[] { 2, 1 })
        {
            long All(int bucket, int field)
            {
                long total = 0;

                for (var lane = 0; lane < Zones.Lanes; lane++)
                {
                    total += sums[Zones.SurplusIndex(side, lane, third, bucket) + field];
                }

                return total;
            }

            var samples = Enumerable.Range(0, Zones.Buckets).Sum(bucket => All(bucket, 0));

            text.Append(CultureInfo.InvariantCulture, $"     ball in the {(third == 2 ? "attacking" : "middle")} third, side in possession; attackers minus defenders in the ball's lane and third:\n");
            text.Append("       surplus             ");

            for (var bucket = 0; bucket < Zones.Buckets; bucket++)
            {
                text.Append((bucket - 4).ToString("+0;-0;0", CultureInfo.InvariantCulture).PadLeft(6));
            }

            text.Append('\n');
            text.Append("       share of samples %  ");

            for (var bucket = 0; bucket < Zones.Buckets; bucket++)
            {
                text.Append(CultureInfo.InvariantCulture, $"{(samples == 0 ? 0 : 100.0 * All(bucket, 0) / samples),6:F1}");
            }

            text.Append('\n');
            text.Append("       shot within 10 s %  ");

            for (var bucket = 0; bucket < Zones.Buckets; bucket++)
            {
                text.Append(CultureInfo.InvariantCulture, $"{(All(bucket, 0) == 0 ? 0 : 100.0 * All(bucket, 1) / All(bucket, 0)),6:F1}");
            }

            text.Append('\n');
            text.Append("       kept 10 s later %   ");

            for (var bucket = 0; bucket < Zones.Buckets; bucket++)
            {
                text.Append(CultureInfo.InvariantCulture, $"{(All(bucket, 2) == 0 ? 0 : 100.0 * All(bucket, 3) / All(bucket, 2)),6:F1}");
            }

            text.Append('\n');
        }

        long Flank(int field) => sums[Zones.FlankAt + (side * Zones.FlankFields) + field];

        text.Append(CultureInfo.InvariantCulture, $"     flank crosses {(double)Flank(0) / matches:F2} per match; with no opposing player in the lane {(Flank(0) == 0 ? 0 : 100.0 * Flank(1) / Flank(0)):F0} %; shot within 10 s: all {(Flank(0) == 0 ? 0 : 100.0 * Flank(2) / Flank(0)):F1} %, uncovered {(Flank(1) == 0 ? 0 : 100.0 * Flank(3) / Flank(1)):F1} %\n");

        return text.ToString();
    }

    /// <summary>
    /// The counts one recorded match is reduced to, so the recording can be dropped: where shots, crosses and passes happen, how many more
    /// attackers than defenders stand around the ball and what comes of it, and crosses from a flank the opponent has left open.
    /// </summary>
    /// <remarks>
    /// All positions are in the acting side's own frame (it attacks towards the far end, its own left hand lane 0), so the two sides can be
    /// added together. A zone is one of three lanes across and three thirds along the pitch. Nothing here reads a player's role or family, because
    /// a reshape changes the seat's family and the positions are what a shape changes.
    /// </remarks>
    private static class Zones
    {
        public const int Lanes = 3;
        public const int Thirds = 3;
        public const int Buckets = 9;
        public const int SurplusFields = 4;
        public const int FlankFields = 4;

        /// <summary>The kinds of action counted: a shot, a cross, a pass tried and a pass completed.</summary>
        public const int Kinds = 4;

        public const int SurplusAt = 2 * Kinds * Lanes * Thirds;
        public const int FlankAt = SurplusAt + (2 * Lanes * Thirds * Buckets * SurplusFields);
        public const int Length = FlankAt + (2 * FlankFields);

        /// <summary>How often the ball's surroundings are sampled, in frames (10 a second).</summary>
        private const int SampleEvery = 5;

        /// <summary>How long after a sample a shot counts as coming of it, in frames (10 seconds).</summary>
        private const int Horizon = 100;

        /// <summary>How long after the horizon a controller is looked for, to judge whether the ball was kept, in frames.</summary>
        private const int KeptWindow = 30;

        /// <summary>How long after a pass the receiver is looked for, in frames.</summary>
        private const int CompletionWindow = 60;

        /// <summary>How far along the pitch from a cross an opposing player still marks its lane, in pitch units (30 m).</summary>
        private const int FlankReach = 3_000;

        /// <summary>Where a bucket's four figures start: samples, shot within the horizon, samples that can be judged for possession, possession kept.</summary>
        public static int SurplusIndex(int side, int lane, int third, int bucket) =>
            SurplusAt + (((((((side * Lanes) + lane) * Thirds) + third) * Buckets) + bucket) * SurplusFields);

        /// <summary>Adds one side's counts of a match sum to the first side's place in another, so shapes can be pooled over home and away.</summary>
        public static void AddSide(long[] target, long[] source, int side)
        {
            void Add(int from, int to, int size)
            {
                for (var index = 0; index < size; index++)
                {
                    target[to + index] += source[from + index];
                }
            }

            Add(ActionAt(side, 0, 0, 0), ActionAt(0, 0, 0, 0), Kinds * Lanes * Thirds);
            Add(SurplusIndex(side, 0, 0, 0), SurplusIndex(0, 0, 0, 0), Lanes * Thirds * Buckets * SurplusFields);
            Add(FlankAt + (side * FlankFields), FlankAt, FlankFields);
        }

        public static int ActionAt(int side, int kind, int lane, int third) => ((((side * Kinds) + kind) * Lanes) + lane) * Thirds + third;

        public static int[] Reduce(TickMatchRecording recording)
        {
            var counts = new int[Length];
            var frames = recording.FrameCount;
            var shots = new[] { new List<int>(), new List<int>() };

            // The actions are in frame order, so each side's shot frames are sorted.
            foreach (var stamp in recording.Actions)
            {
                if (stamp.Action == PassageAction.Shot && stamp.Entity is >= 0 and < Entities && stamp.Frame < frames)
                {
                    shots[stamp.Entity / Half].Add(stamp.Frame);
                }
            }

            foreach (var stamp in recording.Actions)
            {
                if (stamp.Entity is < 0 or >= Entities || stamp.Frame >= frames)
                {
                    continue;
                }

                var kind = stamp.Action switch
                {
                    PassageAction.Shot => 0,
                    PassageAction.Cross => 1,
                    PassageAction.Pass => 2,
                    _ => -1,
                };
                var x = recording.PlayerX(stamp.Frame, stamp.Entity);

                if (kind < 0 || x == TickMatchRecording.Absent)
                {
                    continue;
                }

                var y = recording.PlayerY(stamp.Frame, stamp.Entity);
                var side = stamp.Entity / Half;
                var (lane, third) = ZoneOf(side, x, y);

                counts[ActionAt(side, kind, lane, third)]++;

                if (kind == 2 && Completed(recording, stamp, side))
                {
                    counts[ActionAt(side, 3, lane, third)]++;
                }

                if (kind == 1 && lane != 1)
                {
                    var open = !OpponentInLane(recording, stamp.Frame, side, lane, x);
                    var shot = HasShot(shots[side], stamp.Frame, stamp.Frame + Horizon);
                    var at = FlankAt + (side * FlankFields);

                    counts[at]++;
                    counts[at + 1] += open ? 1 : 0;
                    counts[at + 2] += shot ? 1 : 0;
                    counts[at + 3] += open && shot ? 1 : 0;
                }
            }

            for (var frame = 0; frame < frames; frame += SampleEvery)
            {
                var controller = recording.Controller(frame);

                if (controller < 0 || recording.State(frame) != TickPlayState.OpenPlay)
                {
                    continue;
                }

                var side = controller / Half;
                var ball = ZoneOf(side, recording.BallX(frame), recording.BallY(frame));
                var surplus = 0;

                for (var entity = 0; entity < Entities; entity++)
                {
                    var x = recording.PlayerX(frame, entity);

                    if (x != TickMatchRecording.Absent && ZoneOf(side, x, recording.PlayerY(frame, entity)) == ball)
                    {
                        surplus += entity / Half == side ? 1 : -1;
                    }
                }

                var at = SurplusIndex(side, ball.Lane, ball.Third, Math.Clamp(surplus, -4, 4) + 4);

                counts[at]++;
                counts[at + 1] += HasShot(shots[side], frame, frame + Horizon) ? 1 : 0;

                if (frame + Horizon < frames)
                {
                    counts[at + 2]++;
                    counts[at + 3] += KeptAfter(recording, frame + Horizon, side) ? 1 : 0;
                }
            }

            return counts;
        }

        /// <summary>The lane (across) and third (along) of a point, in a side's own frame.</summary>
        private static (int Lane, int Third) ZoneOf(int side, int x, int y)
        {
            var ownX = OwnX(side, x);
            var ownY = side == 0 ? y : SpatialPitch.PitchWidth - y;

            return (Math.Clamp(ownY * Lanes / SpatialPitch.PitchWidth, 0, Lanes - 1), Math.Clamp(ownX * Thirds / SpatialPitch.PitchLength, 0, Thirds - 1));
        }

        private static int OwnX(int side, int x) => side == 0 ? x : SpatialPitch.PitchLength - x;

        /// <summary>Whether an opposing outfield player stands in the lane within thirty metres along the pitch of the crosser.</summary>
        private static bool OpponentInLane(TickMatchRecording recording, int frame, int side, int lane, int crosserX)
        {
            for (var entity = 0; entity < Entities; entity++)
            {
                var x = recording.PlayerX(frame, entity);

                // The opposing keeper is slot 1 of his side, entity 0 or 11.
                if (entity / Half == side || entity % Half == 0 || x == TickMatchRecording.Absent)
                {
                    continue;
                }

                if (ZoneOf(side, x, recording.PlayerY(frame, entity)).Lane == lane && Math.Abs(OwnX(side, x) - OwnX(side, crosserX)) <= FlankReach)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Completed(TickMatchRecording recording, TickActionStamp stamp, int side)
        {
            for (var frame = stamp.Frame + 1; frame < Math.Min(recording.FrameCount, stamp.Frame + CompletionWindow); frame++)
            {
                var controller = recording.Controller(frame);

                if (controller >= 0 && controller != stamp.Entity)
                {
                    return controller / Half == side;
                }
            }

            return false;
        }

        private static bool KeptAfter(TickMatchRecording recording, int from, int side)
        {
            for (var frame = from; frame <= Math.Min(recording.FrameCount - 1, from + KeptWindow); frame++)
            {
                var controller = recording.Controller(frame);

                if (controller >= 0)
                {
                    return controller / Half == side;
                }
            }

            return false;
        }

        /// <summary>Whether a side shot in the frames after this one, up to and including the end frame.</summary>
        private static bool HasShot(List<int> shotFrames, int frame, int end)
        {
            var at = shotFrames.BinarySearch(frame + 1);

            if (at < 0)
            {
                at = ~at;
            }

            return at < shotFrames.Count && shotFrames[at] <= end;
        }
    }
}
