using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The competition module's schedule persistence: the matchdays and fixtures it stages.
/// </summary>
internal sealed class CompetitionRepository : ICompetitionRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public CompetitionRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void AddMatchday(Matchday matchday) => _dbContext.Matchdays.Add(matchday);

    /// <inheritdoc />
    public void AddFixture(Fixture fixture) => _dbContext.Fixtures.Add(fixture);

    public void AddStanding(Standing standing) => _dbContext.Standings.Add(standing);
}
