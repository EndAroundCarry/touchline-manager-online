using System.Globalization;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// The fair-squads probe (plan cheeky-dongarra, Milestone 4): plays the squads the world now seeds against each other on the tick engine, and
/// against the random squads it seeded before, and prints goals, shots and results per cell.
/// </summary>
/// <remarks>
/// Every side is a real club: the AI's own shape, instructions, match plan and eleven for its squad, turned into a snapshot by the game's own
/// snapshot builder, so the engine sees what a stored fixture would show it. The cells are the old tier 1 against itself (the baseline), the
/// template tier 1 and tier 2 against themselves, and the template tier 1 at home to the template tier 2. Report only; nothing is tuned from it.
/// </remarks>
internal static class TickFairProbe
{
    private const int ClubsPerTier = 16;

    private static readonly Guid WorldId = Guid.Parse("018f0000-0000-7000-8000-0000000000a2");
    private static readonly Guid SeasonId = Guid.Parse("018f0000-0000-7000-8000-0000000000a3");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Runs the probe.</summary>
    /// <param name="matches">How many matches a cell.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules, string variant = "")
    {
        if (variant == "tier3")
        {
            Tier3(matches, seed, rules);

            return;
        }

        if (variant == "diag")
        {
            Diagnose(matches, seed, rules);

            return;
        }

        Console.WriteLine($"== Fair squads on the tick engine, {matches:N0} matches a cell ==");

        var oldTier1 = Club.Build(seed, "old", 1, template: false);
        var oldTier2 = Club.Build(seed, "old", 2, template: false);
        var tier1 = Club.Build(seed, "tpl", 1, template: true);
        var tier2 = Club.Build(seed, "tpl", 2, template: true);

        Console.WriteLine($"  plan strength (best eleven, the fans' scale): old tier 1 {Mean(oldTier1)}, old tier 2 {Mean(oldTier2)}, template tier 1 {Mean(tier1)}, template tier 2 {Mean(tier2)}");
        Console.WriteLine($"  best-eleven spread inside a tier (max/min): old tier 1 {Spread(oldTier1):F3}, template tier 1 {Spread(tier1):F3}, template tier 2 {Spread(tier2):F3}");
        Console.WriteLine();

        Cell("old tier 1 v old tier 1 (baseline)", oldTier1, oldTier1, matches, seed, rules);
        Cell("old tier 1 v old tier 2", oldTier1, oldTier2, matches, seed + 1_000, rules);
        Cell("template tier 1 v tier 1", tier1, tier1, matches, seed + 2_000, rules);
        Cell("template tier 2 v tier 2", tier2, tier2, matches, seed + 3_000, rules);
        Cell("template tier 1 (home) v tier 2", tier1, tier2, matches, seed + 4_000, rules);
        Cell("template tier 2 (home) v tier 1", tier2, tier1, matches, seed + 5_000, rules);
    }

    // The attributes a lift touches, by group (AttributeName order).
    private static readonly int[] Physical = [18, 19, 20, 21, 22, 23];
    private static readonly int[] Defending = [5, 6, 7, 10, 12, 14];
    private static readonly int[] Attacking = [0, 1, 2, 3, 4, 8, 11, 13];

    /// <summary>
    /// Tier 2 against itself with one thing changed at a time: the plans tier 1's clubs play, or a lift of two points to one group of
    /// attributes. Each cell is the template tier 2 unless its label says otherwise.
    /// </summary>
    private static void Diagnose(int matches, ulong seed, EngineRulesV2 rules)
    {
        Console.WriteLine($"== Why tier 2 scores: {matches:N0} matches a cell ==");

        static Func<PlayerPosition, int[], int[]> Lift(int[] group, bool keepers, bool outfield, int by = 2) => (position, values) =>
        {
            var isKeeper = position == PlayerPosition.Goalkeeper;

            if ((isKeeper && !keepers) || (!isKeeper && !outfield))
            {
                return values;
            }

            var lifted = (int[])values.Clone();

            foreach (var index in group)
            {
                lifted[index] = Math.Min(20, lifted[index] + by);
            }

            return lifted;
        };

        // Sets one attribute of every goalkeeper to at least (raise) or at most (cap) a value.
        static Func<PlayerPosition, int[], int[]> Keeper(int attribute, int value, bool raise) => (position, values) =>
        {
            if (position != PlayerPosition.Goalkeeper)
            {
                return values;
            }

            var set = (int[])values.Clone();

            set[attribute] = raise ? Math.Max(set[attribute], value) : Math.Min(set[attribute], value);

            return set;
        };

        var all = Enumerable.Range(0, 28).ToArray();
        var cells = new List<(string Label, Club[] Clubs, ulong Seed, bool PerClub)>
        {
            ("tier 1 v tier 1", Club.Build(seed, "tpl", 1, template: true), seed + 2_000, true),
            ("tier 2 v tier 2", Club.Build(seed, "tpl", 2, template: true), seed + 3_000, true),
            ("tier 2 squads, tier 1 plans", Club.Build(seed, "tpl", 2, template: true, planTier: 1), seed + 3_000, false),
            ("tier 1 squads, tier 2 plans", Club.Build(seed, "tpl", 1, template: true, planTier: 2), seed + 2_000, false),
            ("tier 2, keepers +2 (all)", Club.Build(seed, "tpl", 2, template: true, edit: Lift(all, keepers: true, outfield: false)), seed + 3_000, false),
            ("tier 2, outfield +2 (all)", Club.Build(seed, "tpl", 2, template: true, edit: Lift(all, keepers: false, outfield: true)), seed + 3_000, false),
            ("tier 2, outfield physical +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift(Physical, keepers: false, outfield: true)), seed + 3_000, false),
            ("tier 2, outfield defending +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift(Defending, keepers: false, outfield: true)), seed + 3_000, false),
            ("tier 2, outfield attacking +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift(Attacking, keepers: false, outfield: true)), seed + 3_000, false),
            ("tier 2, keeper Reflexes +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift([25], keepers: true, outfield: false)), seed + 3_000, false),
            ("tier 2, keeper Handling +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift([24], keepers: true, outfield: false)), seed + 3_000, false),
            ("tier 2, keeper Agility+Pace+Accel +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift([18, 19, 22], keepers: true, outfield: false)), seed + 3_000, false),
            ("tier 2, keeper Positioning+Anticip +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift([12, 14], keepers: true, outfield: false)), seed + 3_000, false),
            ("tier 2, keeper OneOnOnes +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift([26], keepers: true, outfield: false)), seed + 3_000, false),
            ("tier 2, keeper Aerial+Jumping +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift([23, 27], keepers: true, outfield: false)), seed + 3_000, false),
            ("tier 1, keeper Reflexes at most 18", Club.Build(seed, "tpl", 1, template: true, edit: Keeper(25, 18, raise: false)), seed + 2_000, false),
            ("tier 2, keeper Reflexes at least 19", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true)), seed + 3_000, false),
            ("tier 2 plans, keeper Reflexes at least 19", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planTier: 1), seed + 3_000, false),
            ("tier 2 R19, all Balanced", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { Mentality = Mentality.Balanced } }), seed + 3_000, false),
            ("tier 2 R19, no counter", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { CounterAttack = false } }), seed + 3_000, false),
            ("tier 2 R19, neutral instructions", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = TeamInstructionSet.Neutral }), seed + 3_000, false),
            ("tier 2 R19 neutral Tempo", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { Tempo = TeamInstructionSet.Neutral.Tempo } }), seed + 3_000, false),
            ("tier 2 R19 neutral Passing", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { Passing = TeamInstructionSet.Neutral.Passing } }), seed + 3_000, false),
            ("tier 2 R19 neutral Width", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { Width = TeamInstructionSet.Neutral.Width } }), seed + 3_000, false),
            ("tier 2 R19 neutral Pressing", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { Pressing = TeamInstructionSet.Neutral.Pressing } }), seed + 3_000, false),
            ("tier 2 R19 neutral DefensiveLine", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { DefensiveLine = TeamInstructionSet.Neutral.DefensiveLine } }), seed + 3_000, false),
            ("tier 2 R19 neutral Tackling", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { Tackling = TeamInstructionSet.Neutral.Tackling } }), seed + 3_000, false),
            ("tier 2 R19 neutral TimeWasting", Club.Build(seed, "tpl", 2, template: true, edit: Keeper(25, 19, raise: true), planEdit: plan => plan with { Instructions = plan.Instructions with { TimeWasting = TeamInstructionSet.Neutral.TimeWasting } }), seed + 3_000, false),
            ("tier 2, keeper the rest +2", Club.Build(seed, "tpl", 2, template: true, edit: Lift([.. all.Except([25, 24, 18, 19, 22, 12, 14, 26, 23, 27])], keepers: true, outfield: false)), seed + 3_000, false),
        };

        foreach (var (label, clubs) in new[] { ("tier 1", cells[0].Clubs), ("tier 2", cells[1].Clubs) })
        {
            // The keeper each club starts: the AI's pick for the goalkeeping slot.
            var keepers = clubs
                .Select(club => club.Source.Players.Single(player => player.PlayerId == club.Source.Selection.Single(entry => entry.SlotNumber == 1).PlayerId))
                .ToList();

            string Of(int attribute) => keepers.Average(player => player.Attributes[attribute]).ToString("F1", CultureInfo.InvariantCulture);

            Console.WriteLine(
                $"  {label} starting keepers with Reflexes 19 or 20: {keepers.Count(player => player.Attributes[25] >= 19)} of {keepers.Count}; Reflexes by keeper: {string.Join(' ', keepers.Select(player => player.Attributes[25]))}");
            Console.WriteLine(
                $"  {label} starting keepers (mean of 16): rating {keepers.Average(player => player.Attributes.Average()).ToString("F1", CultureInfo.InvariantCulture)}  Reflexes {Of(25)}  Handling {Of(24)}  OneOnOnes {Of(26)}  Aerial {Of(27)}  Jumping {Of(23)}  Agility {Of(22)}  Pace {Of(18)}  Acceleration {Of(19)}  Positioning {Of(12)}  Anticipation {Of(14)}  Passing {Of(1)}");
        }

        var only = Environment.GetEnvironmentVariable("FAIR_CELLS");

        foreach (var cell in cells)
        {
            if (only is { Length: > 0 } && !only.Split('|').Any(prefix => cell.Label.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            Cell(cell.Label, cell.Clubs, cell.Clubs, matches, cell.Seed, rules, cell.PerClub);
        }
    }

    /// <summary>Tier 3 (rated 7-9) against itself and against tier 2, as seeded and with the keepers' Reflexes at 19 or more.</summary>
    private static void Tier3(int matches, ulong seed, EngineRulesV2 rules)
    {
        Console.WriteLine($"== Tier 3 and tier 4: {matches:N0} matches a cell ==");

        static Func<PlayerPosition, int[], int[]> Reflexes19() => (position, values) =>
        {
            if (position != PlayerPosition.Goalkeeper)
            {
                return values;
            }

            var set = (int[])values.Clone();

            set[25] = Math.Max(set[25], 19);

            return set;
        };

        var tier2 = Club.Build(seed, "tpl", 2, template: true);
        var tier3 = Club.Build(seed, "tpl", 3, template: true);
        var tier4 = Club.Build(seed, "tpl", 4, template: true);
        var tier3Keepers = Club.Build(seed, "tpl", 3, template: true, edit: Reflexes19());
        var tier4Keepers = Club.Build(seed, "tpl", 4, template: true, edit: Reflexes19());

        Console.WriteLine($"  plan strength: tier 2 {Mean(tier2)}, tier 3 {Mean(tier3)}, tier 4 {Mean(tier4)}; best-eleven spread tier 3 {Spread(tier3):F3}, tier 4 {Spread(tier4):F3}");

        foreach (var (label, clubs) in new[] { ("tier 3", tier3), ("tier 4", tier4) })
        {
            var keepers = clubs
                .Select(club => club.Source.Players.Single(player => player.PlayerId == club.Source.Selection.Single(entry => entry.SlotNumber == 1).PlayerId))
                .ToList();

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {label} starting keepers: rating {keepers.Average(player => player.Attributes.Average()):F1}, Reflexes {keepers.Average(player => player.Attributes[25]):F1}, {keepers.Count(player => player.Attributes[25] >= 19)} of 16 at Reflexes 19+"));
        }

        Cell("tier 3 v tier 3", tier3, tier3, matches, seed + 6_000, rules);
        Cell("tier 3 v tier 3, keepers Reflexes 19+", tier3Keepers, tier3Keepers, matches, seed + 6_000, rules);
        Cell("tier 2 (home) v tier 3", tier2, tier3, matches, seed + 7_000, rules);
        Cell("tier 4 v tier 4", tier4, tier4, matches, seed + 8_000, rules);
        Cell("tier 4 v tier 4, keepers Reflexes 19+", tier4Keepers, tier4Keepers, matches, seed + 8_000, rules);
    }

    private static int Mean(Club[] clubs) => (int)Math.Round(clubs.Average(club => club.Strength));

    private static double Spread(Club[] clubs) => (double)clubs.Max(club => club.Strength) / clubs.Min(club => club.Strength);

    private static void Cell(string label, Club[] homes, Club[] aways, int matches, ulong seed, EngineRulesV2 rules, bool perClub = false)
    {
        var played = new MatchResultV1[matches];
        var gaps = new int[matches];
        var pairs = new (int Home, int Away)[matches];

        // The matches are independent and the engine is pure, so they play side by side.
        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 12 }, index =>
        {
            var home = homes[index % ClubsPerTier];
            var away = aways[(index + 1 + ((index / ClubsPerTier) % (ClubsPerTier - 1))) % ClubsPerTier];

            if (ReferenceEquals(homes, aways) && home.Source.ClubId == away.Source.ClubId)
            {
                away = aways[(index + 2) % ClubsPerTier];
            }

            var built = MatchSnapshotBuilder.Build(
                new FixtureSidesSnapshot(Guid.CreateVersion7(), SeasonId, WorldId, home.Source, away.Source, []),
                rules);
            var input = built.Input with { Seed = seed + (ulong)index };

            played[index] = MatchSimulator.Simulate(input, rules, null, null, TickMatchEngine.Instance);
            gaps[index] = home.Strength - away.Strength;
            pairs[index] = (Array.IndexOf(homes, home), Array.IndexOf(aways, away));
        });

        var goals = played.Sum(result => (double)(result.HomeGoals + result.AwayGoals));
        var homeGoals = played.Sum(result => (double)result.HomeGoals);
        var shots = played.Sum(result => (double)(result.Home.Shots + result.Away.Shots));
        var onTarget = played.Sum(result => (double)(result.Home.ShotsOnTarget + result.Away.ShotsOnTarget));
        var corners = played.Sum(result => (double)(result.Home.Corners + result.Away.Corners));
        var fouls = played.Sum(result => (double)(result.Home.Fouls + result.Away.Fouls));
        var yellows = played.Sum(result => (double)(result.Home.YellowCards + result.Away.YellowCards));
        var homeWins = played.Count(result => result.HomeGoals > result.AwayGoals);
        var draws = played.Count(result => result.HomeGoals == result.AwayGoals);
        var high = played.Count(result => result.HomeGoals + result.AwayGoals >= 7);
        var gap = gaps.Sum(value => (double)value);

        string Pct(int count) => (100.0 * count / matches).ToString("F0", CultureInfo.InvariantCulture);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {label,-36} goals {goals / matches,4:F2} (home {homeGoals / matches:F2})  shots {shots / matches,5:F1}  on target {onTarget / matches,4:F1}  corners {corners / matches,4:F1}  fouls {fouls / matches,4:F1}  yellows {yellows / matches,3:F1}  7+ goals {Pct(high),2}%  home/draw/away {Pct(homeWins)}/{Pct(draws)}/{Pct(matches - homeWins - draws)}%  mean strength gap {gap / matches:F1}"));

        if (!perClub)
        {
            return;
        }

        for (var club = 0; club < ClubsPerTier; club++)
        {
            var scored = 0.0;
            var conceded = 0.0;
            var count = 0;

            for (var index = 0; index < matches; index++)
            {
                if (pairs[index].Home == club)
                {
                    scored += played[index].HomeGoals;
                    conceded += played[index].AwayGoals;
                    count++;
                }

                if (pairs[index].Away == club)
                {
                    scored += played[index].AwayGoals;
                    conceded += played[index].HomeGoals;
                    count++;
                }
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"      club {club + 1,2} {homes[club].Shape,-34} strength {homes[club].Strength}  scored {scored / count:F2}  conceded {conceded / count:F2}  ({count} matches)"));
        }
    }

    /// <summary>One club of a tier: its squad as the AI would field it.</summary>
    private sealed class Club
    {
        public required ClubSideSource Source { get; init; }

        public required int Strength { get; init; }

        public required string Shape { get; init; }

        public static Club[] Build(ulong seed, string kind, int tier, bool template, Func<PlayerPosition, int[], int[]>? edit = null, int? planTier = null, Func<AiClubPlan, AiClubPlan>? planEdit = null)
        {
            var clubs = new Club[ClubsPerTier];

            for (var index = 0; index < ClubsPerTier; index++)
            {
                var clubId = Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"018f0000-0000-7000-8000-{tier * 1_000 + index + (template ? 500 : 0):D12}"));
                var request = new SquadGenerationRequest(
                    string.Create(CultureInfo.InvariantCulture, $"fair-{seed}"),
                    "england",
                    "XX",
                    WorldId,
                    clubId,
                    ((tier - 1) * ClubsPerTier) + index,
                    tier,
                    SeasonId,
                    1,
                    2030,
                    Now);
                var squad = template ? PlayerGenerator.GenerateTemplateSquad(request) : PlayerGenerator.GenerateSquad(request);
                var values = squad.ToDictionary(
                    member => member.Player.Id,
                    member => (IReadOnlyList<int>)(edit is null ? [.. member.Attributes.ToSet().Values] : edit(member.Player.PrimaryPosition, [.. member.Attributes.ToSet().Values])));
                var planId = planTier is { } other
                    ? Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"018f0000-0000-7000-8000-{(other * 1_000) + index + (template ? 500 : 0):D12}"))
                    : clubId;
                var players = squad
                    .Select(member => new SnapshotPlayerRow(
                        member.Player.Id,
                        member.Player.FullName,
                        member.Player.ShortName,
                        member.Player.PrimaryPosition,
                        member.Player.SecondaryPositions,
                        values[member.Player.Id],
                        member.State.ConditionBp,
                        member.State.FatigueBp,
                        member.State.MoraleBp,
                        member.State.MatchSharpnessBp,
                        IsAvailable: true,
                        member.State.ExperienceBp))
                    .ToList();
                var pool = squad
                    .Select(member => new AiSquadPlayer(
                        member.Player.Id,
                        member.Player.PrimaryPosition,
                        member.Player.SecondaryPositions,
                        values[member.Player.Id],
                        member.State.ConditionBp,
                        IsAvailable: true))
                    .ToList();
                var decided = AiClubPolicy.Decide(planId, pool);
                var plan = planEdit is null ? decided : planEdit(decided);

                clubs[index] = new Club
                {
                    Strength = TeamStrength.OfSquad(pool),
                    Shape = string.Create(CultureInfo.InvariantCulture, $"{plan.Formation} {plan.Instructions.Mentality}{(plan.Instructions.CounterAttack ? " counter" : string.Empty)}"),
                    Source = new ClubSideSource(
                        clubId,
                        string.Create(CultureInfo.InvariantCulture, $"{kind} T{tier}-{index + 1:00}"),
                        plan.Instructions,
                        [.. plan.Slots.Select(slot => new SnapshotSlotRow(slot.SlotNumber, slot.PositionFamily, slot.Role, slot.NormalizedX, slot.NormalizedY))],
                        [.. plan.Slots.Where(slot => slot.AssignedPlayerId is not null).Select(slot => new SnapshotSelectionRow(slot.SlotNumber, slot.AssignedPlayerId!.Value, null))],
                        players,
                        MatchInstructions: plan.MatchPlan.Instructions),
                };
            }

            return clubs;
        }
    }
}
