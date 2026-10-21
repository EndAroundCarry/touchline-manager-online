using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Training;

namespace Tactical11Manager.SimulationBenchmarks;

/// <summary>One training day for one player, as the game runs it, with one what-if: development speed.</summary>
internal static class EconTraining
{
    public static void Day(SimPlayer player, int age, DateOnly date, int trainingPercent)
    {
        var programme = TrainingProgrammes.DefaultFor(player.Position);
        var outcome = DailyProgression.Advance(new DailyProgressionInput(
            player.Guid,
            age,
            player.Position,
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
        player.Progress = outcome.AttributeProgress.ToArray();

        if (outcome.AttributeChanges.Count > 0)
        {
            player.SetAttributes(outcome.Attributes.Values.ToArray());
        }
    }
}
