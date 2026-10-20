using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// What the fans should expect from a match (plan partitioned-feather, Milestone 1): how a gap in team strength turns into win, draw and
/// loss on the tick engine, and whether the expectation the game would show is right on average.
/// </summary>
/// <remarks>
/// <para>
/// <c>curve</c> plays uniform sides (every attribute of every player the same value) in the board's 4-4-2 with a strength gap of -4 to +4
/// attribute points. It is a clean look at the engine, but its gaps come in whole points and the engine's skill curve flattens whole
/// numbers into steps, so it is not what the table is fitted to.
/// </para>
/// <para>
/// <c>real</c> takes the sides the dev world stored (two for each played fixture, so each club twice), pairs them across clubs with the
/// gaps spread evenly over half-point bins, plays them on the current engine and fits the curve to the gaps the fans will actually see
/// (<see cref="TeamStrength"/> of the eleven fielded). It then reads every match against the expectation <c>FanRuleSet.ExpectationFor</c>
/// gives, which is the plan's check that the mean surprise (points won minus points expected) is about zero overall and in every band of gap.
/// </para>
/// </remarks>
internal static class TickFansProbe
{
    private const string Directory = ".run-logs/fans";

    /// <summary>Runs the probe.</summary>
    /// <param name="matches">How many matches a cell (curve) or in all (real).</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="mode">"curve" (default) or "real".</param>
    /// <param name="path">The snapshot export for "real".</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules, string mode, string path)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var watch = Stopwatch.StartNew();
        var text = new StringBuilder();
        var name = mode.Length == 0 ? "curve" : mode.ToLowerInvariant();

        text.Append(name switch
        {
            "real" => Real(matches, seed, rules, path.Length == 0 ? Path.Combine(Directory, "snapshots.tsv") : path),
            "pool" => Pool(),
            "league" => League(matches, seed, rules, path.Length == 0 ? Path.Combine(Directory, "snapshots.tsv") : path),
            _ => Curve(matches, seed, rules),
        });
        watch.Stop();

        var report = text.ToString();

        Console.Write(report);
        File.WriteAllText(Path.Combine(Directory, $"{name}-{seed}.txt"), report);
        File.AppendAllText(
            Path.Combine(Directory, "ledger.tsv"),
            string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}\ttickfans\t{name}\t{matches}\t{seed}\t{watch.Elapsed.TotalSeconds:F0}\n"));
        Console.WriteLine($"  run time this call {watch.Elapsed.TotalSeconds:F0} s; every call is appended to {Path.Combine(Directory, "ledger.tsv")}");
    }

    private static string Curve(int matches, ulong seed, EngineRulesV2 rules)
    {
        var text = new StringBuilder();
        var weak = int.TryParse(Environment.GetEnvironmentVariable("TF_BASE"), CultureInfo.InvariantCulture, out var baseAbility) ? baseAbility : 14;
        var gaps = (Environment.GetEnvironmentVariable("TF_GAPS") ?? "-4,-3,-2,-1,0,1,2,3,4")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
        var played = new List<Played>();

        text.Append(CultureInfo.InvariantCulture, $"== tickfans curve: uniform sides in the board's 4-4-2, the weaker side at {weak}, {matches} matches a cell ==\n");
        text.Append("  gap = home strength minus away strength in attribute points (negative: the away side is stronger)\n");

        foreach (var gap in gaps)
        {
            var home = gap >= 0 ? weak + gap : weak;
            var away = gap >= 0 ? weak : weak - gap;
            var results = new (int Home, int Away)[matches];

            Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
            {
                var built = LaboratoryFixtures.Build(seed + (ulong)index, home, away, new MatchInstructionsV1(), new MatchInstructionsV1(), rules);
                var input = TickCalibration.Stand(built, FormationPreset.FourFourTwo, FormationPreset.FourFourTwo);
                var result = MatchSimulator.Simulate(input, rules, null, null, TickMatchEngine.Instance);

                results[index] = (result.HomeGoals, result.AwayGoals);
            });

            played.AddRange(results.Select(r => new Played(gap * 100, gap * 100, r.Home, r.Away)));
            text.Append(Cell(gap, results));
        }

        text.Append(FitPlayed("uniform sides", [.. played], entry => entry.GapCentis, -5, 5));
        text.Append('\n');

        return text.ToString();
    }

    private static string Cell(int gap, (int Home, int Away)[] results)
    {
        var win = results.Count(r => r.Home > r.Away);
        var draw = results.Count(r => r.Home == r.Away);
        var loss = results.Length - win - draw;

        return string.Create(CultureInfo.InvariantCulture, $"  gap {gap,+2:+0;-0;0}  win {100.0 * win / results.Length,5:F1}%  draw {100.0 * draw / results.Length,5:F1}%  loss {100.0 * loss / results.Length,5:F1}%   points {((3.0 * win) + draw) / results.Length,5:F2}   goals {results.Average(r => r.Home),4:F2} - {results.Average(r => r.Away),4:F2}\n");
    }

    private static string Real(int matches, ulong seed, EngineRulesV2 rules, string path)
    {
        var text = new StringBuilder();
        var inputs = new List<(MatchInputV1 Input, int HomeGoals, int AwayGoals)>();

        foreach (var row in File.ReadLines(path))
        {
            var parts = row.Split('\t', 4);

            inputs.Add((Relabel(MatchSnapshotDocument.Read(parts[3]).Input, rules), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture)));
        }

        var sides = inputs.SelectMany(entry => new[] { entry.Input.Home, entry.Input.Away }).ToList();
        var strengths = sides.Select(StrengthOf).ToArray();
        var random = new Random(unchecked((int)seed));

        // Every ordered pairing of two different clubs, in bins of half an attribute point; the same number is drawn from each bin so that
        // the gaps the curve is fitted on are spread evenly and not bunched around zero (the world rarely holds a gap beyond three points).
        var bins = new Dictionary<int, List<(int Home, int Away)>>();

        for (var home = 0; home < sides.Count; home++)
        {
            for (var away = 0; away < sides.Count; away++)
            {
                if (sides[home].ClubId == sides[away].ClubId)
                {
                    continue;
                }

                var bin = (int)Math.Floor((strengths[home] - strengths[away]) / 50.0);

                if (!bins.TryGetValue(bin, out var list))
                {
                    list = [];
                    bins[bin] = list;
                }

                list.Add((home, away));
            }
        }

        var width = int.TryParse(Environment.GetEnvironmentVariable("TF_BINS"), CultureInfo.InvariantCulture, out var limit) ? limit : 7;
        var chosen = bins.Where(pair => pair.Key >= -width && pair.Key < width).OrderBy(pair => pair.Key).ToList();
        var pairs = new List<(int Home, int Away)>();

        for (var index = 0; index < matches; index++)
        {
            var list = chosen[index % chosen.Count].Value;

            pairs.Add(list[random.Next(list.Count)]);
        }

        var played = new Played[matches];

        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            var input = inputs[0].Input with { Home = sides[pairs[index].Home], Away = sides[pairs[index].Away], Seed = seed + (ulong)index };
            var result = MatchSimulator.Simulate(input, rules, null, null, TickMatchEngine.Instance);

            played[index] = new Played(StrengthOf(input.Home) - StrengthOf(input.Away), MeanOf28(input.Home) - MeanOf28(input.Away), result.HomeGoals, result.AwayGoals);
        });

        var stored = inputs.Select(entry => new Played(StrengthOf(entry.Input.Home) - StrengthOf(entry.Input.Away), MeanOf28(entry.Input.Home) - MeanOf28(entry.Input.Away), entry.HomeGoals, entry.AwayGoals)).ToArray();

        text.Append(CultureInfo.InvariantCulture, $"== tickfans real: {matches} pairings of the {sides.Count} stored sides, drawn evenly from bins of half a point, and the {stored.Length} stored fixtures ==\n");
        text.Append(CultureInfo.InvariantCulture, $"  strength of the stored sides: {Describe(strengths)}\n");
        File.WriteAllLines(
            Path.Combine(Directory, $"real-{seed}.tsv"),
            played.Select(entry => string.Create(CultureInfo.InvariantCulture, $"pair\t{entry.GapCentis}\t{entry.Gap28Centis}\t{entry.HomeGoals}\t{entry.AwayGoals}"))
                .Concat(stored.Select(entry => string.Create(CultureInfo.InvariantCulture, $"stored\t{entry.GapCentis}\t{entry.Gap28Centis}\t{entry.HomeGoals}\t{entry.AwayGoals}"))));
        text.Append(FitPlayed("strength = outfield 24 attributes, keeper 4 (the plan's)", played, entry => entry.GapCentis, -4, 4));
        text.Append(FitPlayed("strength = mean of all 28 attributes (what the AI policy ranks by)", played, entry => entry.Gap28Centis, -4, 4));
        text.Append(Check("random pairings (the curve was fitted to pairings like these; a different seed gives fresh matches)", played));
        text.Append(Check("the stored fixtures, as the dev world played them", stored));

        return text.ToString();
    }

    /// <summary>
    /// Plays one division of the dev world through a double round robin (306 matches, the squads as stored) and reads each match against the
    /// expectation the fans would have held at that round, with form built from the season so far: squad only, the plan's form weight
    /// (<see cref="FanRuleSet.FormHalfWeightGames"/>) and a slower one.
    /// </summary>
    private static string League(int matches, ulong seed, EngineRulesV2 rules, string path)
    {
        var text = new StringBuilder();
        var inputs = File.ReadLines(path).Select(row => Relabel(MatchSnapshotDocument.Read(row.Split('\t', 4)[3]).Input, rules)).ToList();
        var sideOf = new Dictionary<Guid, MatchSideV1>();

        foreach (var side in inputs.SelectMany(input => new[] { input.Home, input.Away }))
        {
            sideOf.TryAdd(side.ClubId, side);
        }

        var divisions = File.ReadLines(Path.Combine(Directory, "divisions.tsv"))
            .Select(row => row.Split('\t'))
            .GroupBy(columns => columns[2])
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        var chosen = divisions[int.TryParse(Environment.GetEnvironmentVariable("TF_DIVISION"), CultureInfo.InvariantCulture, out var division) ? division : 0];
        var clubs = chosen.Select(columns => Guid.Parse(columns[0])).Where(sideOf.ContainsKey).ToList();
        var count = clubs.Count;
        var fixtures = new List<(int Round, int Home, int Away)>();

        // Circle method: club 0 stays, the others rotate; the second half of the season turns every fixture round.
        var ring = Enumerable.Range(0, count).ToList();

        for (var round = 0; round < count - 1; round++)
        {
            for (var pair = 0; pair < count / 2; pair++)
            {
                var one = ring[pair];
                var other = ring[count - 1 - pair];
                var flip = (round + pair) % 2 == 0;

                fixtures.Add((round, flip ? one : other, flip ? other : one));
                fixtures.Add((round + count - 1, flip ? other : one, flip ? one : other));
            }

            ring.Insert(1, ring[^1]);
            ring.RemoveAt(ring.Count - 1);
        }

        fixtures = [.. fixtures.OrderBy(fixture => fixture.Round).Take(Math.Max(1, matches))];

        var results = new (int Home, int Away)[fixtures.Count];

        Parallel.For(0, fixtures.Count, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            var input = inputs[0] with { Home = sideOf[clubs[fixtures[index].Home]], Away = sideOf[clubs[fixtures[index].Away]], Seed = seed + (ulong)index };
            var result = MatchSimulator.Simulate(input, rules, null, null, TickMatchEngine.Instance);

            results[index] = (result.HomeGoals, result.AwayGoals);
        });

        var strength = clubs.Select(club => StrengthOf(sideOf[club])).ToArray();
        var variants = new (string Name, int HalfGames)[] { ("squad only", 0), ("form weight games/(games+10), the plan's", FanRuleSet.FormHalfWeightGames), ("form weight games/(games+30)", 30) };
        var points = new int[count];
        var games = new int[count];
        var tallies = variants.Select(_ => new List<(int Round, double HomeError, double AwayError, double HomeSurprise, double AwaySurprise)>()).ToArray();
        var labels = new int[Enum.GetValues<FanExpectationLabel>().Length];

        foreach (var roundGroup in fixtures.Select((fixture, index) => (fixture, index)).GroupBy(entry => entry.fixture.Round).OrderBy(group => group.Key))
        {
            foreach (var (fixture, index) in roundGroup)
            {
                var homeForm = new FanForm(games[fixture.Home], points[fixture.Home]);
                var awayForm = new FanForm(games[fixture.Away], points[fixture.Away]);
                var homePoints = results[index].Home > results[index].Away ? 3 : results[index].Home == results[index].Away ? 1 : 0;
                var awayPoints = results[index].Home < results[index].Away ? 3 : results[index].Home == results[index].Away ? 1 : 0;

                for (var variant = 0; variant < variants.Length; variant++)
                {
                    var half = variants[variant].HalfGames;
                    var squadGap = strength[fixture.Home] - strength[fixture.Away];
                    var played = Math.Min(homeForm.Games, awayForm.Games);
                    var weightBp = half == 0 ? 0 : (int)((long)played * 10_000 / (played + half));
                    var formGap = (homeForm.PointsPerGameCentis - awayForm.PointsPerGameCentis) * 100 / FanRuleSet.FormPointsPerAttributePointCentis;
                    var gap = (int)((((long)(10_000 - weightBp) * squadGap) + ((long)weightBp * formGap)) / 10_000);
                    var home = FanRuleSet.ExpectationForGap(gap, true);
                    var away = FanRuleSet.ExpectationForGap(gap, false);

                    if (variant == 1)
                    {
                        labels[(int)home.Label]++;
                        labels[(int)away.Label]++;
                    }

                    tallies[variant].Add((
                        roundGroup.Key,
                        Math.Pow(homePoints - (home.ExpectedPointsCentis / 100.0), 2),
                        Math.Pow(awayPoints - (away.ExpectedPointsCentis / 100.0), 2),
                        homePoints - (home.ExpectedPointsCentis / 100.0),
                        awayPoints - (away.ExpectedPointsCentis / 100.0)));
                }
            }

            foreach (var (fixture, index) in roundGroup)
            {
                var homePoints = results[index].Home > results[index].Away ? 3 : results[index].Home == results[index].Away ? 1 : 0;
                var awayPoints = results[index].Home < results[index].Away ? 3 : results[index].Home == results[index].Away ? 1 : 0;

                games[fixture.Home]++;
                games[fixture.Away]++;
                points[fixture.Home] += homePoints;
                points[fixture.Away] += awayPoints;
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"== tickfans league: {chosen.First()[2]}, {count} clubs, {fixtures.Count} matches of a double round robin, squads as stored ==\n");
        text.Append(CultureInfo.InvariantCulture, $"  strength of the {count} clubs: {Describe(strength)}\n");
        text.Append("  the fans' expectation against the points taken (mean surprise; root-mean-square error of the expected points, both sides of every match; lower is better):\n");

        for (var variant = 0; variant < variants.Length; variant++)
        {
            string Window(int from, int to)
            {
                var rows = tallies[variant].Where(row => row.Round >= from && row.Round < to).ToArray();

                return rows.Length == 0
                    ? "-"
                    : string.Create(CultureInfo.InvariantCulture, $"{Math.Sqrt(rows.Average(row => (row.HomeError + row.AwayError) / 2)):F3}");
            }

            var all = tallies[variant];

            text.Append(CultureInfo.InvariantCulture, $"    {variants[variant].Name,-44} surprise {all.Average(row => (row.HomeSurprise + row.AwaySurprise) / 2),+6:+0.000;-0.000}   error {Window(0, 99)}   by rounds 1-10 {Window(0, 10)}, 11-20 {Window(10, 20)}, 21-34 {Window(20, 34)}\n");
        }

        text.Append("  labels given over the season, both sides of every match (plan's weight): ");
        text.Append(string.Join(", ", Enum.GetValues<FanExpectationLabel>().OrderByDescending(label => label).Select(label => $"{FanExpectationLabels.Phrase(label)} {labels[(int)label]}")));
        text.Append('\n');

        return text.ToString();
    }

    /// <summary>
    /// Reads played matches against the expectation the game would have shown: the mean surprise (points won minus points expected) overall,
    /// by band of strength gap and by label, for the home side and for the away side.
    /// </summary>
    private static string Check(string title, Played[] played)
    {
        var text = new StringBuilder();
        var rows = played.Select(entry =>
        {
            var home = FanRuleSet.ExpectationForGap(entry.GapCentis, true);
            var away = FanRuleSet.ExpectationForGap(entry.GapCentis, false);
            var homePoints = entry.HomeGoals > entry.AwayGoals ? 3 : entry.HomeGoals == entry.AwayGoals ? 1 : 0;
            var awayPoints = entry.HomeGoals < entry.AwayGoals ? 3 : entry.HomeGoals == entry.AwayGoals ? 1 : 0;

            return (Entry: entry, Home: home, Away: away, HomeSurprise: homePoints - (home.ExpectedPointsCentis / 100.0), AwaySurprise: awayPoints - (away.ExpectedPointsCentis / 100.0));
        }).ToArray();

        double Standard(IEnumerable<double> values)
        {
            var list = values.ToArray();
            var mean = list.Average();

            return Math.Sqrt(list.Sum(value => (value - mean) * (value - mean)) / Math.Max(1, list.Length - 1)) / Math.Sqrt(list.Length);
        }

        text.Append(CultureInfo.InvariantCulture, $"  -- the expectation FanRuleSet.ExpectationFor gives, against {title}: {rows.Length} matches --\n");
        text.Append(CultureInfo.InvariantCulture, $"     mean surprise, home side {rows.Average(row => row.HomeSurprise),+6:+0.000;-0.000} (+-{Standard(rows.Select(row => row.HomeSurprise)):F3}), away side {rows.Average(row => row.AwaySurprise),+6:+0.000;-0.000} (+-{Standard(rows.Select(row => row.AwaySurprise)):F3}); plan: |mean| under 0.1\n");
        text.Append(CultureInfo.InvariantCulture, $"     expected points, home side {rows.Average(row => row.Home.ExpectedPointsCentis) / 100.0:F2}, away side {rows.Average(row => row.Away.ExpectedPointsCentis) / 100.0:F2}; actual {rows.Average(row => row.HomeSurprise + (row.Home.ExpectedPointsCentis / 100.0)):F2} and {rows.Average(row => row.AwaySurprise + (row.Away.ExpectedPointsCentis / 100.0)):F2}\n");
        text.Append("     by band of strength gap, home side (matches: mean surprise, goals scored home - away):\n");

        foreach (var band in rows.GroupBy(row => (int)Math.Floor(row.Entry.GapCentis / 100.0)).OrderBy(group => group.Key))
        {
            var members = band.ToArray();

            text.Append(CultureInfo.InvariantCulture, $"       {band.Key,+3:+0;-0;0}..{band.Key + 1,+3:+0;-0;0} ({members.Length,3}): {members.Average(row => row.HomeSurprise),+6:+0.000;-0.000} (+-{Standard(members.Select(row => row.HomeSurprise)):F2})   goals scored {members.Average(row => row.Entry.HomeGoals),4:F2} - {members.Average(row => row.Entry.AwayGoals),4:F2}\n");
        }

        text.Append("     by label, either side (sides: mean surprise, share winning / drawing / losing):\n");

        var sides = rows.SelectMany(row => new[]
        {
            (Label: row.Home.Label, Surprise: row.HomeSurprise, Points: row.Entry.HomeGoals > row.Entry.AwayGoals ? 3 : row.Entry.HomeGoals == row.Entry.AwayGoals ? 1 : 0, Expected: row.Home),
            (Label: row.Away.Label, Surprise: row.AwaySurprise, Points: row.Entry.HomeGoals < row.Entry.AwayGoals ? 3 : row.Entry.HomeGoals == row.Entry.AwayGoals ? 1 : 0, Expected: row.Away),
        }).ToArray();

        foreach (var label in sides.GroupBy(side => side.Label).OrderByDescending(group => group.Key))
        {
            var members = label.ToArray();

            text.Append(CultureInfo.InvariantCulture, $"       {FanExpectationLabels.Phrase(label.Key),-20} ({members.Length,4}): {members.Average(side => side.Surprise),+6:+0.000;-0.000}   expected {members.Average(side => side.Expected.WinBp) / 100.0,5:F1} / {members.Average(side => side.Expected.DrawBp) / 100.0,5:F1} / {members.Average(side => side.Expected.LossBp) / 100.0,5:F1} %, got {100.0 * members.Count(side => side.Points == 3) / members.Length,5:F1} / {100.0 * members.Count(side => side.Points == 1) / members.Length,5:F1} / {100.0 * members.Count(side => side.Points == 0) / members.Length,5:F1} %\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Pools every <c>real-*.tsv</c> a past run wrote, fits the curve once to all of them and prints the table <c>FanRuleSet</c> holds, as C#.
    /// </summary>
    private static string Pool()
    {
        var text = new StringBuilder();
        var played = new List<Played>();
        var files = System.IO.Directory.GetFiles(Directory, "real-*.tsv").Order().ToList();

        foreach (var file in files)
        {
            foreach (var row in File.ReadLines(file).Select(line => line.Split('\t')).Where(columns => columns[0] == "pair"))
            {
                played.Add(new Played(
                    int.Parse(row[1], CultureInfo.InvariantCulture),
                    int.Parse(row[2], CultureInfo.InvariantCulture),
                    int.Parse(row[3], CultureInfo.InvariantCulture),
                    int.Parse(row[4], CultureInfo.InvariantCulture)));
            }
        }

        var all = played.ToArray();

        text.Append(CultureInfo.InvariantCulture, $"== tickfans pool: {files.Count} runs, {all.Length} matches ({string.Join(", ", files.Select(Path.GetFileName))}) ==\n");
        text.Append(FitPlayed("pooled, plan strength", all, entry => entry.GapCentis, -4, 4));

        double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

        double LogLoss(double[] p) => p[0] <= 0 || p[2] <= 0
            ? double.MaxValue
            : -all.Sum(entry =>
            {
                var latent = p[0] * ((entry.GapCentis / 100.0) + p[1]);
                var win = Sigmoid(latent - p[2]);
                var loss = Sigmoid(-latent - p[2]);

                return Math.Log(entry.HomeGoals > entry.AwayGoals ? win : entry.HomeGoals == entry.AwayGoals ? Math.Max(1e-9, 1 - win - loss) : loss);
            });

        var fit = Minimize(LogLoss, [0.3, 1.0, 0.6], [0.1, 0.2, 0.1]);

        double[] GoalFit(Func<Played, int> pick, int sign) => Minimize(
            p => all.Sum(entry =>
            {
                var lambda = Math.Exp(p[0] + (sign * p[1] * entry.GapCentis / 100.0));

                return lambda - (pick(entry) * Math.Log(lambda));
            }),
            [0.3, 0.1],
            [0.1, 0.05]);

        var homeGoals = GoalFit(entry => entry.HomeGoals, 1);
        var awayGoals = GoalFit(entry => entry.AwayGoals, -1);
        var rows = Enumerable.Range(0, 25).Select(row => -600 + (row * 50)).ToArray();

        (int Win, int Draw) At(int gapCentis)
        {
            var latent = fit[0] * ((gapCentis / 100.0) + fit[1]);
            var win = Sigmoid(latent - fit[2]);
            var loss = Sigmoid(-latent - fit[2]);

            return ((int)Math.Round(10_000 * win), (int)Math.Round(10_000 * (1 - win - loss)));
        }

        text.Append(CultureInfo.InvariantCulture, $"  pooled ordered logit: beta {fit[0]:F4}, home advantage {fit[1]:F3} points, draw band +-{fit[2]:F4}\n");
        text.Append(CultureInfo.InvariantCulture, $"  home goals exp({homeGoals[0]:F4} + {homeGoals[1]:F4} gap), away goals exp({awayGoals[0]:F4} - {awayGoals[1]:F4} gap)\n");
        text.Append(CultureInfo.InvariantCulture, $"  HomeWinBp = [{string.Join(", ", rows.Select(row => At(row).Win))}]\n");
        text.Append(CultureInfo.InvariantCulture, $"  DrawBp = [{string.Join(", ", rows.Select(row => At(row).Draw))}]\n");
        text.Append(CultureInfo.InvariantCulture, $"  HomeGoalsCentis = [{string.Join(", ", rows.Select(row => (int)Math.Round(100 * Math.Exp(homeGoals[0] + (homeGoals[1] * row / 100.0)))))}]\n");
        text.Append(CultureInfo.InvariantCulture, $"  AwayGoalsCentis = [{string.Join(", ", rows.Select(row => (int)Math.Round(100 * Math.Exp(awayGoals[0] - (awayGoals[1] * row / 100.0)))))}]\n");

        // The points one attribute point is worth to a side around an even game, averaged over the two venues: what a form gap is converted by.
        double Points(int gapCentis, bool home)
        {
            var (win, draw) = At(home ? gapCentis : -gapCentis);
            var loss = 10_000 - win - draw;

            return home ? ((3.0 * win) + draw) / 10_000 : ((3.0 * loss) + draw) / 10_000;
        }

        var slope = ((Points(100, true) - Points(-100, true)) + (Points(100, false) - Points(-100, false))) / 4.0;

        text.Append(CultureInfo.InvariantCulture, $"  points per attribute point around an even game (mean of home and away, +-1 point): {slope:F3}\n");

        return text.ToString();
    }

    /// <summary>Fits the ordered logit and the two goal means to played matches and prints them beside what was observed.</summary>
    private static string FitPlayed(string title, Played[] played, Func<Played, int> gapOf, int from, int to)
    {
        var text = new StringBuilder();

        double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

        double LogLoss(double[] p)
        {
            var (beta, h, c) = (p[0], p[1], p[2]);

            if (beta <= 0 || c <= 0)
            {
                return double.MaxValue;
            }

            var total = 0.0;

            foreach (var entry in played)
            {
                var latent = beta * ((gapOf(entry) / 100.0) + h);
                var win = Sigmoid(latent - c);
                var loss = Sigmoid(-latent - c);
                var draw = Math.Max(1e-9, 1.0 - win - loss);

                total -= Math.Log(entry.HomeGoals > entry.AwayGoals ? win : entry.HomeGoals == entry.AwayGoals ? draw : loss);
            }

            return total;
        }

        var fit = Minimize(LogLoss, [0.4, 0.5, 0.5], [0.1, 0.2, 0.1]);

        text.Append(CultureInfo.InvariantCulture, $"  -- {title}, {played.Length} matches --\n");
        text.Append(CultureInfo.InvariantCulture, $"     ordered-logit fit: beta {fit[0]:F4} per point, home advantage worth {fit[1]:F3} points, draw band +-{fit[2]:F4}; log-loss per match {LogLoss(fit) / played.Length:F4}\n");

        foreach (var (label, pick, sign) in new (string, Func<Played, int>, int)[] { ("home goals", entry => entry.HomeGoals, 1), ("away goals", entry => entry.AwayGoals, -1) })
        {
            double Deviance(double[] p) => played.Sum(entry =>
            {
                var lambda = Math.Exp(p[0] + (sign * p[1] * gapOf(entry) / 100.0));

                return lambda - (pick(entry) * Math.Log(lambda));
            });

            var goals = Minimize(Deviance, [0.3, 0.1], [0.1, 0.05]);

            text.Append(CultureInfo.InvariantCulture, $"     {label}: mean = exp({goals[0]:F4} {(sign > 0 ? "+" : "-")} {goals[1]:F4} * gap) -> {Math.Exp(goals[0]):F3} at gap 0\n");
        }

        text.Append("     fitted table (gap in points: home win / draw / loss %, home points):\n");

        for (var gap = (double)from; gap <= to; gap += 0.5)
        {
            var latent = fit[0] * (gap + fit[1]);
            var win = Sigmoid(latent - fit[2]);
            var loss = Sigmoid(-latent - fit[2]);

            text.Append(CultureInfo.InvariantCulture, $"       {gap,5:+0.0;-0.0;0.0}: {100 * win,5:F1} / {100 * (1 - win - loss),5:F1} / {100 * loss,5:F1}   {(3 * win) + (1 - win - loss),5:F2}\n");
        }

        text.Append("     observed by band of one point (matches: win / draw / loss %, home points, goals):\n");

        foreach (var band in played.GroupBy(entry => (int)Math.Floor(gapOf(entry) / 100.0)).OrderBy(group => group.Key))
        {
            var count = band.Count();
            var wins = band.Count(entry => entry.HomeGoals > entry.AwayGoals);
            var draws = band.Count(entry => entry.HomeGoals == entry.AwayGoals);

            text.Append(CultureInfo.InvariantCulture, $"       {band.Key,+3:+0;-0;0}..{band.Key + 1,+3:+0;-0;0} ({count,3}): {100.0 * wins / count,5:F1} / {100.0 * draws / count,5:F1} / {100.0 * (count - wins - draws) / count,5:F1}   {((3.0 * wins) + draws) / count,5:F2}   goals {band.Average(entry => entry.HomeGoals),4:F2} - {band.Average(entry => entry.AwayGoals),4:F2}\n");
        }

        return text.ToString();
    }

    /// <summary>A small Nelder-Mead simplex: enough for two or three parameters of a smooth likelihood.</summary>
    private static double[] Minimize(Func<double[], double> f, double[] start, double[] step)
    {
        var n = start.Length;
        var simplex = new double[n + 1][];

        simplex[0] = (double[])start.Clone();

        for (var i = 0; i < n; i++)
        {
            simplex[i + 1] = (double[])start.Clone();
            simplex[i + 1][i] += step[i];
        }

        var values = simplex.Select(f).ToArray();

        for (var iteration = 0; iteration < 4_000; iteration++)
        {
            var order = Enumerable.Range(0, n + 1).OrderBy(i => values[i]).ToArray();

            simplex = [.. order.Select(i => simplex[i])];
            values = [.. order.Select(i => values[i])];

            if (Math.Abs(values[n] - values[0]) < 1e-10)
            {
                break;
            }

            var centroid = new double[n];

            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                {
                    centroid[j] += simplex[i][j] / n;
                }
            }

            double[] Along(double t) => [.. Enumerable.Range(0, n).Select(j => centroid[j] + (t * (simplex[n][j] - centroid[j])))];

            var reflected = Along(-1);
            var reflectedValue = f(reflected);

            if (reflectedValue < values[0])
            {
                var expanded = Along(-2);
                var expandedValue = f(expanded);

                (simplex[n], values[n]) = expandedValue < reflectedValue ? (expanded, expandedValue) : (reflected, reflectedValue);
            }
            else if (reflectedValue < values[n - 1])
            {
                (simplex[n], values[n]) = (reflected, reflectedValue);
            }
            else
            {
                var contracted = Along(0.5);
                var contractedValue = f(contracted);

                if (contractedValue < values[n])
                {
                    (simplex[n], values[n]) = (contracted, contractedValue);
                }
                else
                {
                    for (var i = 1; i <= n; i++)
                    {
                        for (var j = 0; j < n; j++)
                        {
                            simplex[i][j] = simplex[0][j] + (0.5 * (simplex[i][j] - simplex[0][j]));
                        }

                        values[i] = f(simplex[i]);
                    }
                }
            }
        }

        return simplex[Array.IndexOf(values, values.Min())];
    }

    private static string Describe(int[] values)
    {
        var sorted = values.Order().ToArray();

        return $"min {sorted[0] / 100.0:F2}, p10 {sorted[sorted.Length / 10] / 100.0:F2}, median {sorted[sorted.Length / 2] / 100.0:F2}, p90 {sorted[sorted.Length * 9 / 10] / 100.0:F2}, max {sorted[^1] / 100.0:F2}";
    }

    /// <summary>The strength of the eleven a side fielded.</summary>
    private static int StrengthOf(MatchSideV1 side)
    {
        var byId = side.Squad.ToDictionary(player => player.ParticipantId);

        return TeamStrength.OfEleven(side.Slots.Select(slot => TeamStrength.AbilityOf(byId[slot.ParticipantId].Attributes.Values, slot.Family == MatchPositionFamily.Goalkeeper)));
    }

    /// <summary>The mean of all twenty-eight attributes of the eleven a side fielded (the yardstick the AI policy ranks by).</summary>
    private static int MeanOf28(MatchSideV1 side)
    {
        var byId = side.Squad.ToDictionary(player => player.ParticipantId);

        return (int)Math.Round(100.0 * side.Slots.Average(slot => byId[slot.ParticipantId].Attributes.Values.Average()));
    }

    private static MatchInputV1 Relabel(MatchInputV1 input, EngineRulesV2 rules) => input with
    {
        EngineVersion = EngineVersions.EngineLabel,
        RuleSetVersion = EngineVersions.RuleSetLabel,
        FormulaConfigurationHash = EngineConfiguration.HashOf(rules),
    };

    private readonly record struct Played(int GapCentis, int Gap28Centis, int HomeGoals, int AwayGoals);
}
