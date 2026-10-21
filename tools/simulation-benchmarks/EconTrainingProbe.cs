using System.Globalization;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.Squad.Training;

namespace Tactical11Manager.SimulationBenchmarks;

// Follows a cohort of generated 17 and 18-year-olds through daily training, alone: no club, no market, no contracts. It answers whether a youth
// reaches his ceiling by the age the generator assumes (TRN-18: a 17-year-old starts seven points under it). ECON_PROBE=1 runs it instead of a world.
internal static class EconTrainingProbe
{
    public static void Run(int players, int seasons, int trainingPercent, int backgroundPercent)
    {
        var cohort = new List<(SimPlayer Player, int StartAge)>();
        var ordinal = 0;
        var positions = Enum.GetValues<PlayerPosition>();

        for (var index = 0; index < players; index++)
        {
            var member = PlayerGenerator.GenerateYouth(
                "econ-probe", "england", "XX", Guid.Empty, EconSim.MakeGuid(1, index), tier: 1, Guid.Empty, 1, EconSim.GameYearBase, positions[index % positions.Length], ordinal++, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var guid = EconSim.MakeGuid(4, index);

            cohort.Add((
                new SimPlayer
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
                },
                member.Player.AgeIn(EconSim.GameYearBase)));
        }

        Adults(positions);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"econ training probe: {players} youths of 17-18, development at {trainingPercent}% of today's, background growth of untrained attributes {backgroundPercent}%"));
        Console.WriteLine("year | mean age | skill (28) | own-position skill | ceiling | attributes at the ceiling | trained attributes at the ceiling");

        for (var year = 0; year <= seasons; year++)
        {
            var gameYear = EconSim.GameYearBase + year;

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{year,4} | {cohort.Average(entry => entry.Player.AgeIn(gameYear)),8:0.0} | {cohort.Average(entry => (double)entry.Player.Ability),10:0.0} | {cohort.Average(entry => entry.Player.OwnSkill),18:0.0} | {cohort.Average(entry => (double)entry.Player.Potential),7:0.0} | {cohort.Average(entry => entry.Player.Attr.Count(value => value >= entry.Player.Potential)),25:0.0} | {cohort.Average(entry => TrainedAtCeiling(entry.Player)),33:0.0} of {cohort.Average(entry => (double)TrainingProgrammes.Of(TrainingProgrammes.DefaultFor(entry.Player.Position)).Attributes.Count):0.0}"));

            if (year == seasons)
            {
                break;
            }

            for (var day = 0; day < TrainingAgeCurve.TrainingDaysPerSeason; day++)
            {
                var date = new DateOnly(2030, 1, 1).AddDays((year * 100) + day);

                foreach (var (player, _) in cohort)
                {
                    EconTraining.Day(player, player.AgeIn(gameYear), date, trainingPercent, backgroundPercent);
                }
            }
        }
    }

    /// <summary>What a finished, generated adult looks like against his own ceiling, split by what his programme trains.</summary>
    private static void Adults(PlayerPosition[] positions)
    {
        var trainedGap = new List<double>();
        var otherGap = new List<double>();
        var trainedAbove = new List<double>();
        var otherAbove = new List<double>();
        var own = new List<double>();
        var ceiling = new List<double>();

        for (var club = 0; club < 40; club++)
        {
            var request = new SquadGenerationRequest("econ-probe", "england", "XX", Guid.Empty, EconSim.MakeGuid(1, club), club, 1, Guid.Empty, 1, EconSim.GameYearBase, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

            foreach (var member in PlayerGenerator.GenerateSquad(request).Where(member => member.Player.AgeIn(EconSim.GameYearBase) is >= 24 and <= 29 && member.Player.PrimaryPosition != PlayerPosition.Goalkeeper))
            {
                var values = member.Attributes.ToSet().Values;
                var definition = TrainingProgrammes.Of(TrainingProgrammes.DefaultFor(member.Player.PrimaryPosition));
                var potential = member.Player.Potential;

                for (var index = 0; index < (int)AttributeName.Handling; index++)
                {
                    var name = (AttributeName)index;
                    var (gaps, above) = definition.WeightOf(name) > 0 ? (trainedGap, trainedAbove) : (otherGap, otherAbove);

                    gaps.Add(values[index] - potential);
                    above.Add(values[index] > potential ? 1 : 0);
                }

                own.Add(TeamStrength.AbilityOf(values, false) / 100.0);
                ceiling.Add(potential);
            }
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"generated outfield adults aged 24-29 ({own.Count}): ceiling {ceiling.Average():0.0}, own-position skill {own.Average():0.0}; the attributes his programme trains sit {trainedGap.Average():+0.0;-0.0} from the ceiling ({trainedAbove.Average():P0} above it), the other own-position attributes {otherGap.Average():+0.0;-0.0} ({otherAbove.Average():P0} above it)"));
        Console.WriteLine();
    }

    private static double TrainedAtCeiling(SimPlayer player) =>
        TrainingProgrammes.Of(TrainingProgrammes.DefaultFor(player.Position)).Attributes
            .Count(entry => player.Attr[(int)entry.Attribute] >= player.Potential);
}
