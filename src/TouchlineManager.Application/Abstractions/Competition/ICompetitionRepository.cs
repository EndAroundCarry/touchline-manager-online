using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Application.Abstractions.Competition;

/// <summary>
/// Persistence for the competition module's schedule: the matchdays a division-season plays and the
/// fixtures within them.
/// </summary>
/// <remarks>
/// <para>
/// The competition module owns its own tables (`MOD-1`), and the world seeder — which creates the first
/// season's schedule as part of generation — stages them through this port like any other cross-module
/// write (`MOD-2`). Reads that feed a screen or a command live on the queries the feature needs, so a
/// screen can change without widening what a command can reach (`MOD-3`).
/// </para>
/// <para>
/// A fixture list is generated once, from a recorded seed (`CAL-8`), so there is no update path here yet;
/// the transitions that do change a fixture — lock, stage, publish — arrive with the matchday worker that
/// drives them.
/// </para>
/// </remarks>
public interface ICompetitionRepository
{
    /// <summary>Stages a matchday (round).</summary>
    void AddMatchday(Matchday matchday);

    /// <summary>Stages a fixture.</summary>
    void AddFixture(Fixture fixture);

    /// <summary>Stages a club's line in a division's table.</summary>
    void AddStanding(Standing standing);
}
