using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when a club's dashboard was read.</summary>
public enum ClubDashboardOutcome
{
    /// <summary>Read.</summary>
    Found = 0,

    /// <summary>No such club.</summary>
    ClubNotFound = 1,

    /// <summary>No world has been seeded, so no club can exist.</summary>
    WorldNotSeeded = 2,
}

/// <summary>The result of a club-dashboard read.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Dashboard">The dashboard, when one was read.</param>
public sealed record ClubDashboardResult(ClubDashboardOutcome Outcome, ClubDashboardResponse? Dashboard);

/// <summary>
/// Reads the inherited-club dashboard (master plan §7.6, `WORLD-9`).
/// </summary>
/// <remarks>
/// What a manager inherits at this stage of the build: the club's identity and standing, the competition
/// it plays in, who controls it, and its money. A takeover resets none of it. The squad, contracts,
/// fixtures, and history that §WORLD-9 also lists join this response in the stages that create them,
/// rather than appearing here as permanently-empty fields.
/// </remarks>
public sealed class GetClubDashboard
{
    private readonly Abstractions.IClock _clock;
    private readonly IWorldRepository _world;
    private readonly IOnboardingQueries _queries;

    /// <summary>Initializes the query.</summary>
    public GetClubDashboard(
        Abstractions.IClock clock,
        IWorldRepository world,
        IOnboardingQueries queries)
    {
        _clock = clock;
        _world = world;
        _queries = queries;
    }

    /// <summary>Reads the club's dashboard for the season in progress.</summary>
    /// <param name="clubId">The club to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ClubDashboardResult> ExecuteAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return new ClubDashboardResult(ClubDashboardOutcome.WorldNotSeeded, null);
        }

        var snapshot = await _queries.GetClubDashboardAsync(
            clubId,
            world.CurrentSeasonNumber,
            cancellationToken);

        if (snapshot is null)
        {
            return new ClubDashboardResult(ClubDashboardOutcome.ClubNotFound, null);
        }

        return new ClubDashboardResult(
            ClubDashboardOutcome.Found,
            snapshot.ToDashboard(_clock.UtcNow));
    }
}

/// <summary>
/// Reads where an account stands in onboarding (master plan §10.2).
/// </summary>
/// <remarks>
/// One request answers the whole routing question — no profile, a profile but no club, or a club — so the
/// client renders a decision it was told rather than inferring one from two separate reads that could
/// disagree.
/// </remarks>
public sealed class GetOnboardingState
{
    private readonly Abstractions.IClock _clock;
    private readonly IManagerRepository _managers;
    private readonly IOnboardingQueries _queries;

    /// <summary>Initializes the query.</summary>
    public GetOnboardingState(
        Abstractions.IClock clock,
        IManagerRepository managers,
        IOnboardingQueries queries)
    {
        _clock = clock;
        _managers = managers;
        _queries = queries;
    }

    /// <summary>Reads the account's manager profile and current club.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OnboardingStateResponse> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var manager = await _managers.FindByUserIdAsync(userId, cancellationToken);

        if (manager is null)
        {
            return new OnboardingStateResponse(null, null, _clock.UtcNow);
        }

        var tenure = await _queries.GetCurrentTenureAsync(manager.Id, cancellationToken);

        return new OnboardingStateResponse(
            manager.ToResponse(),
            tenure?.Tenure.ToSummary(tenure.Club, tenure.Country, tenure.Division),
            _clock.UtcNow);
    }
}
