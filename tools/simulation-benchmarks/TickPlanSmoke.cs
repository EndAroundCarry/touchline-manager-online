using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Tick;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>
/// The match-plan smoke test (plan i-want-you-to-curious-globe, Milestone 2): one rule from each family is attached to the home side of the
/// equal 4-4-2 pairing and played against the same seeds without it, to show that every kind of rule fires, when, and that what it does reaches the
/// match. It is a check that the plumbing works, not the impact study (Milestone 7), so it reads no effect size.
/// </summary>
internal static class TickPlanSmoke
{
    /// <summary>The lab bench is a keeper and central midfielders, so every substitution here is a midfielder for a midfielder.</summary>
    private static MatchRuleSubstitutionV1 FreshMidfielder => new()
    {
        Off = MatchRuleOff.MostTiredInFamily,
        OffFamily = MatchPositionFamily.Midfield,
        On = MatchRuleOn.BestFitInFamily,
        OnFamily = MatchPositionFamily.Midfield,
    };

    private static MatchRuleSubstitutionV1 FreshForSubject => new()
    {
        Off = MatchRuleOff.Subject,
        On = MatchRuleOn.BestFitInFamily,
        OnFamily = MatchPositionFamily.Midfield,
    };

    private static readonly Case[] Cases =
    [
        new("protect a 2-goal lead from 60'", new MatchRuleV1
        {
            Ordinal = 1,
            FromMinute = 60,
            Score = MatchRuleScore.Leading,
            ScoreMargin = 2,
            Instructions = new MatchInstructionChangesV1 { MentalitySteps = -1, TimeWasting = MatchTimeWasting.Situational },
        }),
        new("chase from 60' when losing: 4-3-3, Attacking, high press, a fresh midfielder", new MatchRuleV1
        {
            Ordinal = 1,
            FromMinute = 60,
            Score = MatchRuleScore.Trailing,
            FormationCode = "4-3-3",
            Slots = SlotsOf(FormationPreset.FourThreeThree),
            Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking, Pressing = MatchPressing.HighPress },
            Substitutions = [FreshMidfielder],
        }),
        new("a fresh midfielder at 60'", new MatchRuleV1 { Ordinal = 1, FromMinute = 60, Substitutions = [FreshMidfielder] }),
        new("3-4-3 at 60'", new MatchRuleV1 { Ordinal = 1, FromMinute = 60, FormationCode = "3-4-3", Slots = SlotsOf(FormationPreset.ThreeFourThree) }),
        new("a booked player off after 45'", new MatchRuleV1 { Ordinal = 1, FromMinute = 45, Event = MatchRuleEvent.OwnBooked, Substitutions = [FreshForSubject] }),
        new("own red card: 4-4-1-1 and one step defensive", new MatchRuleV1
        {
            Ordinal = 1,
            Event = MatchRuleEvent.OwnSentOff,
            FormationCode = "4-4-1-1",
            Slots = SlotsOf(FormationPreset.FourFourOneOne),
            Instructions = new MatchInstructionChangesV1 { MentalitySteps = -1 },
        }),
        new("opponent red card: Attacking and wide", new MatchRuleV1
        {
            Ordinal = 1,
            Event = MatchRuleEvent.OpponentSentOff,
            Instructions = new MatchInstructionChangesV1 { Mentality = MatchMentality.Attacking, Width = MatchWidth.Wide },
        }),
        new("the lowest rated midfielder off at 60'", new MatchRuleV1
        {
            Ordinal = 1,
            FromMinute = 60,
            Event = MatchRuleEvent.PlayingBadly,
            EventFamily = MatchPositionFamily.Midfield,
            Substitutions = [FreshForSubject],
        }),
        new("a player under 65% condition off", new MatchRuleV1
        {
            Ordinal = 1,
            Event = MatchRuleEvent.Tired,
            EventThreshold = 6_500,
            Substitutions = [FreshForSubject],
        }),
        new("the opponent chasing the game (Attacking): one step defensive", new MatchRuleV1
        {
            Ordinal = 1,
            Event = MatchRuleEvent.OpponentMentalityAtLeast,
            EventThreshold = (int)MatchMentality.Attacking,
            Instructions = new MatchInstructionChangesV1 { MentalitySteps = -1 },
        }, OpponentAttacks: true),
    ];

    /// <summary>Runs the smoke test.</summary>
    /// <param name="matches">How many matches a case.</param>
    /// <param name="seed">The base seed.</param>
    /// <param name="rules">The rules in force.</param>
    public static void Run(int matches, ulong seed, EngineRulesV2 rules)
    {
        var report = new StringBuilder();
        var watch = Stopwatch.StartNew();

        report.AppendLine(CultureInfo.InvariantCulture, $"== Match plans smoke: one rule from each family on the home side of equal 16 against 16, 4-4-2 both; {matches} matches, seed {seed} ==");

        var plain = Play(null, opponentAttacks: false, matches, seed, rules);
        var plainChasing = Play(null, opponentAttacks: true, matches, seed, rules);

        report.AppendLine(CultureInfo.InvariantCulture, $"  no rule: home points {Points(plain):F2}, goals {Goals(plain):F2} a match");
        report.AppendLine("  rule                                                                       fired   first at  changes  tactical subs  home pts  goals");

        foreach (var entry in Cases)
        {
            var played = Play(entry.Rule, entry.OpponentAttacks, matches, seed, rules);
            var baseline = entry.OpponentAttacks ? plainChasing : plain;
            var fired = played.Where(match => match.FirstFiredMinute > 0).ToArray();

            report.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {entry.Name,-74} {fired.Length,4} {(fired.Length == 0 ? 0 : fired.Average(match => match.FirstFiredMinute)),8:F1}  {played.Sum(match => match.Changes),7}  {played.Sum(match => match.TacticalSubstitutions),13}  {Points(played),8:F2}  {Goals(played),5:F2}   (without the rule: points {Points(baseline):F2}, goals {Goals(baseline):F2})"));
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"  run time {watch.Elapsed.TotalSeconds:F0} s");

        Console.Write(report);

        var directory = Path.Combine(".run-logs", "plans");

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"smoke-{seed}.txt"), report.ToString());
        File.AppendAllText(
            Path.Combine(directory, "ledger.tsv"),
            string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}\ttickplans\tsmoke\t{matches}\t{seed}\t{watch.Elapsed.TotalSeconds:F0}\n"));
    }

    private static double Points(Played[] matches) => matches.Average(match => match.HomeGoals > match.AwayGoals ? 3.0 : (match.HomeGoals == match.AwayGoals ? 1.0 : 0.0));

    private static double Goals(Played[] matches) => matches.Average(match => match.HomeGoals + match.AwayGoals);

    private static Played[] Play(MatchRuleV1? rule, bool opponentAttacks, int matches, ulong seed, EngineRulesV2 rules)
    {
        var played = new Played[matches];

        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = 10 }, index =>
        {
            var input = TickPlanProbe.BuildNamed("equal-442", seed + (ulong)index, rules);

            if (rule is not null)
            {
                input = input with { Home = input.Home with { MatchPlan = new MatchPlanV1 { Rules = [rule] } } };
            }

            if (opponentAttacks)
            {
                input = input with { Away = input.Away with { Instructions = input.Away.Instructions with { Mentality = MatchMentality.Attacking } } };
            }

            var result = MatchSimulator.Simulate(input, rules, null, null, TickMatchEngine.Instance);
            var home = result.Events.Where(matchEvent => matchEvent.Side == MatchSide.Home).ToArray();
            var changes = home.Where(matchEvent => matchEvent.Type == EngineEventType.TacticalChange).ToArray();
            var substitutions = home.Where(matchEvent => matchEvent.Type == EngineEventType.Substitution && matchEvent.SubstitutionReason == MatchSubstitutionReason.Tactical).ToArray();
            var first = changes.Concat(substitutions).OrderBy(matchEvent => matchEvent.Sequence).FirstOrDefault();

            played[index] = new Played(result.HomeGoals, result.AwayGoals, first?.Minute ?? 0, changes.Length, substitutions.Length);
        });

        return played;
    }

    private static IReadOnlyList<MatchSlotV1> SlotsOf(FormationPreset preset) =>
    [
        .. FormationLayouts.DefaultSlots(preset).Select(entry => new MatchSlotV1
        {
            SlotNumber = entry.SlotNumber,
            Family = EngineVocabulary.Family(entry.PositionFamily),
            Role = EngineVocabulary.Role(entry.Role),
            X = entry.NormalizedX,
            Y = entry.NormalizedY,
            ParticipantId = Guid.Empty,
        }),
    ];

    private sealed record Case(string Name, MatchRuleV1 Rule, bool OpponentAttacks = false);

    private sealed record Played(int HomeGoals, int AwayGoals, int FirstFiredMinute, int Changes, int TacticalSubstitutions);
}
