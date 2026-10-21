using System.Globalization;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.Squad.Training;

namespace Tactical11Manager.SimulationBenchmarks;

// Follows cohorts of players through daily training, alone: no club, no market, no contracts (plan radiant-globe, training-v4). It answers whether
// a youth reaches the limit his potential sets by the age the generator assumes (TRN-18: a 17-year-old starts seven points under it), whether
// veterans decline as before, and whether a generated adult drifts. ECON_PROBE=1 runs it instead of a world; ECON_PROBE_POT=18-20 narrows the
// youths to a potential range, to show gems developing.
internal static class EconTrainingProbe
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Run(int players, int seasons, int trainingPercent, int potentialLow, int potentialHigh)
    {
        var inv = CultureInfo.InvariantCulture;
        var range = potentialLow > 0 ? $"potential {potentialLow}-{potentialHigh}" : "potential as the generator draws it";

        Console.WriteLine(string.Create(inv, $"econ training probe ({DailyProgression.Version}): {players} youths of 17-18 ({range}), development at {trainingPercent}% of the rules'"));
        Console.WriteLine();

        var trained = Youths(players, seasons, trainingPercent, potentialLow, potentialHigh);

        Adults("veterans: generated players aged 28, seven seasons", 28, 28, 7, trainingPercent);

        var generated = Adults("drift: generated adults aged 24-29, three seasons", 24, 29, 3, trainingPercent);

        Console.WriteLine("## Twenties per player, by position: a trained youth at 24-25 against a generated adult of 24-29");
        Console.WriteLine("position | trained youths | 20s | generated adults | 20s | mean potential (trained / generated)");

        foreach (var position in Enum.GetValues<PlayerPosition>())
        {
            var mine = trained.Where(player => player.Position == position).ToList();
            var theirs = generated.Where(player => player.Position == position).ToList();

            if (mine.Count == 0 || theirs.Count == 0)
            {
                continue;
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{position,-20} | {mine.Count,14} | {mine.Average(Twenties),4:0.00} | {theirs.Count,16} | {theirs.Average(Twenties),4:0.00} | {mine.Average(player => (double)player.Potential):0.0} / {theirs.Average(player => (double)player.Potential):0.0}"));
        }

        Console.WriteLine();
    }

    // ---- youths -----------------------------------------------------------------------------------------------------------------------

    private static List<SimPlayer> Youths(int players, int seasons, int trainingPercent, int potentialLow, int potentialHigh)
    {
        var positions = Enum.GetValues<PlayerPosition>();
        var cohort = new List<SimPlayer>();
        var ordinal = 0;

        for (var index = 0; index < players; index++)
        {
            var position = positions[index % positions.Length];

            // With a potential range the draw is repeated until it fits, so the cohort keeps its mix of positions.
            for (var attempt = 0; attempt < 400; attempt++)
            {
                var member = PlayerGenerator.GenerateYouth(
                    "econ-probe", "england", "XX", Guid.Empty, EconSim.MakeGuid(1, index), tier: 1, Guid.Empty, 1, EconSim.GameYearBase, position, ordinal++, Now);

                if (potentialLow == 0 || member.Player.Potential >= potentialLow && member.Player.Potential <= potentialHigh)
                {
                    cohort.Add(Make(member, index));

                    break;
                }
            }
        }

        var snapshot = new List<SimPlayer>();
        var completeAt = new Dictionary<int, int>();
        var gapAt = new Dictionary<(int Id, int Age), double>();

        Console.WriteLine("## Youths: average against the limit, by year");
        Console.WriteLine("year | mean age | skill (28) | own-position skill | developing attributes now / limit | complete | 20s per player | 20s per winger | attributes over the limit");

        Train(cohort, seasons, trainingPercent, (year, gameYear) =>
        {
            foreach (var player in cohort)
            {
                var age = player.AgeIn(gameYear);

                if (!completeAt.ContainsKey(player.Id) && Complete(player))
                {
                    completeAt[player.Id] = age;
                }

                if (age is 22 or 24)
                {
                    gapAt[(player.Id, age)] = Formula(player) - Developing(player);
                }
            }

            if (year == 7)
            {
                snapshot = [.. cohort.Select(Copy)];
            }

            var wingers = cohort.Where(player => player.Position is PlayerPosition.RightWinger or PlayerPosition.LeftWinger).ToList();

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{year,4} | {cohort.Average(player => player.AgeIn(gameYear)),8:0.0} | {cohort.Average(player => (double)player.Ability),10:0.0} | {cohort.Average(player => player.OwnSkill),18:0.0} | {cohort.Average(Developing),14:0.0} / {cohort.Average(Formula),4:0.0} | {cohort.Average(player => Complete(player) ? 1.0 : 0.0),8:P0} | {cohort.Average(Twenties),14:0.00} | {(wingers.Count == 0 ? double.NaN : wingers.Average(Twenties)),14:0.00} | {cohort.Average(OverLimit),25:0.00}"));
        });

        Console.WriteLine();
        Console.WriteLine("## Youths: when each is complete (sum of the developing attributes at the formula's sum), by learning aptitude");
        Console.WriteLine("aptitude third | median age complete | complete by 22 | by 24 | by 26 | never in the run | still short at 22 | at 24 (points per attribute)");

        var ordered = cohort.OrderBy(player => player.Aptitude).ToList();
        var third = Math.Max(1, ordered.Count / 3);
        var groups = new (string Label, List<SimPlayer> Players)[]
        {
            ("slow", ordered.Take(third).ToList()),
            ("average", ordered.Skip(third).Take(ordered.Count - (2 * third)).ToList()),
            ("quick", ordered.Skip(ordered.Count - third).ToList()),
        };

        foreach (var (label, group) in groups)
        {
            ReachedRow(label, group, completeAt, gapAt);
        }

        ReachedRow("keepers", cohort.Where(player => player.IsGoalkeeper).ToList(), completeAt, gapAt);
        ReachedRow("everyone", cohort, completeAt, gapAt);
        Console.WriteLine();

        return snapshot;
    }

    private static void ReachedRow(string label, List<SimPlayer> group, Dictionary<int, int> completeAt, Dictionary<(int Id, int Age), double> gapAt)
    {
        if (group.Count == 0)
        {
            return;
        }

        var ages = group.Where(player => completeAt.ContainsKey(player.Id)).Select(player => completeAt[player.Id]).Order().ToList();
        var median = ages.Count == 0 ? "-" : ages[ages.Count / 2].ToString(CultureInfo.InvariantCulture);
        var never = group.Count - ages.Count;

        double By(int age) => group.Count(player => completeAt.TryGetValue(player.Id, out var at) && at <= age) / (double)group.Count;

        double Gap(int age) => group.Where(player => gapAt.ContainsKey((player.Id, age))).Select(player => gapAt[(player.Id, age)]).DefaultIfEmpty(double.NaN).Average();

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label,-14} | {median,19} | {By(22),14:P0} | {By(24):P0} | {By(26):P0} | {never,16} | {Gap(22),17:0.0} | {Gap(24):0.0}  (n={group.Count})"));
    }

    // ---- adults -----------------------------------------------------------------------------------------------------------------------

    /// <summary>A cohort of generated squad players of an age band: veterans decline, and the drift of a finished player is what the rule adds or takes away.</summary>
    private static List<SimPlayer> Adults(string title, int minAge, int maxAge, int seasons, int trainingPercent)
    {
        var cohort = new List<SimPlayer>();
        var index = 0;

        for (var club = 0; club < 40; club++)
        {
            var request = new SquadGenerationRequest("econ-probe", "england", "XX", Guid.Empty, EconSim.MakeGuid(1, club), club, 1, Guid.Empty, 1, EconSim.GameYearBase, Now);

            foreach (var member in PlayerGenerator.GenerateSquad(request).Where(member => member.Player.AgeIn(EconSim.GameYearBase) >= minAge && member.Player.AgeIn(EconSim.GameYearBase) <= maxAge))
            {
                cohort.Add(Make(member, 100_000 + index++));
            }
        }

        var wingers = cohort.Where(player => player.Position is PlayerPosition.RightWinger or PlayerPosition.LeftWinger).ToList();

        Console.WriteLine($"## Adults, {title} ({cohort.Count} players, {wingers.Count} wingers)");
        Console.WriteLine("year | mean age | skill (28) | own-position skill | developing attributes now / formula | 20s per player | 20s per winger | complete");

        var firstOwn = 0.0;
        var start = cohort.Select(Copy).ToList();

        Train(cohort, seasons, trainingPercent, (year, gameYear) =>
        {
            var own = cohort.Average(player => player.OwnSkill);

            firstOwn = year == 0 ? own : firstOwn;

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{year,4} | {cohort.Average(player => player.AgeIn(gameYear)),8:0.0} | {cohort.Average(player => (double)player.Ability),10:0.0} | {own,10:0.00} ({own - firstOwn:+0.00;-0.00;0.00}) | {cohort.Average(Developing),14:0.0} / {cohort.Average(Formula),4:0.0} | {cohort.Average(Twenties),14:0.00} | {wingers.Average(Twenties),14:0.00} | {cohort.Average(player => Complete(player) ? 1.0 : 0.0),8:P0}"));
        });

        Console.WriteLine();

        return start;
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------------------------

    private static SimPlayer Copy(SimPlayer player) => new()
    {
        Id = player.Id,
        Guid = player.Guid,
        Position = player.Position,
        Attr = [.. player.Attr],
        Potential = player.Potential,
        BirthYear = player.BirthYear,
        Aptitude = player.Aptitude,
    };

    private static SimPlayer Make(GeneratedSquadMember member, int index)
    {
        var guid = EconSim.MakeGuid(4, index);

        return new SimPlayer
        {
            Id = index,
            Guid = guid,
            Position = member.Player.PrimaryPosition,
            Attr = [.. member.Attributes.ToSet().Values],
            Potential = member.Player.Potential,
            BirthYear = member.Player.BirthGameYear,
            Aptitude = TrainingAptitude.For(guid),
            ConditionBp = member.State.ConditionBp,
            FatigueBp = member.State.FatigueBp,
            MoraleBp = member.State.MoraleBp,
            SharpnessBp = member.State.MatchSharpnessBp,
        };
    }

    private static void Train(List<SimPlayer> cohort, int seasons, int trainingPercent, Action<int, int> yearEnd)
    {
        for (var year = 0; year <= seasons; year++)
        {
            var gameYear = EconSim.GameYearBase + year;

            yearEnd(year, gameYear);

            if (year == seasons)
            {
                break;
            }

            for (var day = 0; day < TrainingAgeCurve.TrainingDaysPerSeason; day++)
            {
                var date = new DateOnly(2030, 1, 1).AddDays((year * 100) + day);

                foreach (var player in cohort)
                {
                    EconTraining.Day(player, player.AgeIn(gameYear), date, trainingPercent);
                }
            }
        }
    }

    private static double Developing(SimPlayer player)
    {
        var attributes = DevelopmentLimit.DevelopingAttributes(player.Position);

        return attributes.Average(name => (double)player.Attr[(int)name]);
    }

    private static double Formula(SimPlayer player) => DevelopmentLimit.CapSum(player.Position, player.Potential) / (double)DevelopmentLimit.DevelopingAttributes(player.Position).Count;

    private static bool Complete(SimPlayer player) => DevelopmentLimit.DevelopingAttributes(player.Position).Sum(name => player.Attr[(int)name]) >= DevelopmentLimit.CapSum(player.Position, player.Potential);

    private static double Twenties(SimPlayer player) => DevelopmentLimit.DevelopingAttributes(player.Position).Count(name => player.Attr[(int)name] >= 20);

    private static double OverLimit(SimPlayer player)
    {
        var limits = DevelopmentLimit.Limits(player.Position, player.Potential);

        return DevelopmentLimit.DevelopingAttributes(player.Position).Count(name => player.Attr[(int)name] > limits[(int)name]);
    }
}
