using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad.Training;

namespace TouchlineManager.Application.Squad;

/// <summary>What one daily progression run did.</summary>
/// <param name="Clubs">How many clubs the run covered.</param>
/// <param name="Players">How many players were progressed.</param>
public sealed record RunDailyProgressionResult(int Clubs, int Players);

/// <summary>
/// Advances every player in the world by one day of training (master plan §7.2, §3.9; `TRN-3`, `TRN-9`).
/// </summary>
/// <remarks>
/// <para>
/// One run covers the whole world, because training is a world-scoped daily fact: a human club and an AI
/// club progress by the same rules, with no privileged path (`INS-12`). The calculation is the pure
/// <see cref="DailyProgression"/> function, so the result is reproducible from the player, the day, and
/// the training in force.
/// </para>
/// <para>
/// The run is idempotent for a day. A player whose <c>LastProgressionDate</c> is already at or after the
/// day being run is skipped, so a retried job — or a duplicate enqueue that slipped through the business
/// key — cannot develop a player twice.
/// </para>
/// </remarks>
public sealed class RunDailyProgression
{
    private readonly ITrainingRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public RunDailyProgression(ITrainingRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Runs one day of progression.</summary>
    /// <param name="day">The day to progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RunDailyProgressionResult> ExecuteAsync(
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var rosters = await _repository.LoadRostersAsync(cancellationToken);
        var progressed = 0;

        foreach (var roster in rosters)
        {
            foreach (var member in roster.Players)
            {
                if (member.State.LastProgressionDate is { } last && last >= day)
                {
                    continue;
                }

                var outcome = DailyProgression.Advance(new DailyProgressionInput(
                    member.Player.Id,
                    member.Player.AgeIn(roster.GameYear),
                    member.Attributes.ToSet(),
                    member.Potential,
                    roster.TeamFocus,
                    roster.Intensity,
                    member.IndividualFocus,
                    member.State.ConditionBp,
                    member.State.FatigueBp,
                    member.State.MoraleBp,
                    member.State.MatchSharpnessBp,
                    member.State.DevelopmentRemainder,
                    day));

                member.State.ApplyProgression(
                    outcome.ConditionBp,
                    outcome.FatigueBp,
                    outcome.MoraleBp,
                    outcome.MatchSharpnessBp,
                    outcome.DevelopmentRemainder,
                    day);

                member.Attributes.Apply(outcome.Attributes);

                progressed++;
            }
        }

        if (progressed > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new RunDailyProgressionResult(rosters.Count, progressed);
    }
}
