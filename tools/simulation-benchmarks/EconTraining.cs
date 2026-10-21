using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Training;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>One training day for one player, as the game runs it, with two what-ifs: development speed, and a slow background growth of the attributes his programme does not train.</summary>
internal static class EconTraining
{
    public static void Day(SimPlayer player, int age, DateOnly date, int trainingPercent, int backgroundPercent)
    {
        var programme = TrainingProgrammes.DefaultFor(player.Position);
        var outcome = DailyProgression.Advance(new DailyProgressionInput(
            player.Guid,
            age,
            player.AttrSet,
            player.Potential,
            player.Aptitude * trainingPercent / 100,
            programme,
            TrainingIntensity.Normal,
            player.ConditionBp,
            player.FatigueBp,
            player.MoraleBp,
            player.SharpnessBp,
            player.Progress,
            date));

        player.ConditionBp = outcome.ConditionBp;
        player.FatigueBp = outcome.FatigueBp;
        player.MoraleBp = outcome.MoraleBp;
        player.SharpnessBp = outcome.MatchSharpnessBp;

        var progress = outcome.AttributeProgress.ToArray();
        var values = outcome.AttributeChanges.Count > 0 ? outcome.Attributes.Values.ToArray() : null;
        var changed = values is not null;

        if (backgroundPercent > 0)
        {
            var definition = TrainingProgrammes.Of(programme);
            var capacity = Math.Min(WorldRuleSet.AttributeMax, player.Potential);
            var current = values ?? [.. player.Attr];

            // Only the attributes his position uses: 24 for an outfield player, the 4 goalkeeping ones for a keeper.
            var untrained = AttributeNames.All
                .Where(name => definition.WeightOf(name) == 0 && player.IsGoalkeeper == ((int)name >= (int)AttributeName.Handling) && current[(int)name] < capacity)
                .ToList();

            if (untrained.Count > 0)
            {
                values ??= [.. player.Attr];

                // The day's budget, shared by the attributes the programme leaves alone, in millionths of a point.
                var budgetMicro = DailyProgression.BudgetMilli(age, player.Aptitude * trainingPercent / 100, TrainingIntensity.Normal, player.FatigueBp) * 1_000L * backgroundPercent / 100;
                var share = (int)(budgetMicro / untrained.Count);

                foreach (var name in untrained)
                {
                    var index = (int)name;

                    progress[index] += share;

                    while (progress[index] >= AttributeProgress.Basis && values[index] < capacity)
                    {
                        values[index]++;
                        progress[index] -= AttributeProgress.Basis;
                        changed = true;
                    }

                    if (values[index] >= capacity && progress[index] > 0)
                    {
                        progress[index] = 0;
                    }
                }
            }
        }

        player.Progress = progress;

        if (changed)
        {
            player.SetAttributes(values!);
        }
    }
}
