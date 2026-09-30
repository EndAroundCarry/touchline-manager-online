using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The operational funnels, counted from the rows the game already writes (master plan §16 Stage 13,
/// `F-54`, ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is stored: every number is a count over accounts, manager profiles, and tenures. That is
/// the whole privacy argument — the surface collects no new personal data, so it needs no table, no
/// retention rule, and no deletion path of its own.
/// </para>
/// <para>
/// Counting the auth module's accounts is a cross-module read, which is permitted (`MOD-3`); onboarding
/// already does the same to measure occupancy.
/// </para>
/// </remarks>
internal sealed class OperationalAnalyticsQueries : IOperationalAnalyticsQueries
{
    /// <summary>How far back "seen recently" reaches for the retention read.</summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromDays(7);

    private readonly TouchlineManagerDbContext _dbContext;
    private readonly IClock _clock;

    /// <summary>Initializes the queries.</summary>
    public OperationalAnalyticsQueries(TouchlineManagerDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<OperationalAnalyticsSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (world is null)
        {
            return null;
        }

        var generatedAt = _clock.UtcNow;
        var activeSince = generatedAt - ActiveWindow;

        // Anonymized accounts are excluded from the account steps: they are no longer a manager walking the
        // funnel, and counting them would inflate "registered" against "verified" for no operational reason.
        var registered = await _dbContext.Users
            .CountAsync(user => user.Status != UserStatus.Anonymized, cancellationToken);

        var verified = await _dbContext.Users
            .CountAsync(
                user => user.EmailVerifiedAt != null && user.Status != UserStatus.Anonymized,
                cancellationToken);

        var profilesCreated = await _dbContext.Managers.CountAsync(cancellationToken);

        // A manager holds at most one club (OCC-9), so distinct managers is the count of claimed clubs.
        var clubsClaimed = await _dbContext.ClubTenures
            .Where(tenure => tenure.ControlStatus != ClubTenureControlStatus.Closed)
            .Select(tenure => tenure.ManagerId)
            .Distinct()
            .CountAsync(cancellationToken);

        var activeTenures = await _dbContext.ClubTenures
            .CountAsync(tenure => tenure.ControlStatus == ClubTenureControlStatus.Active, cancellationToken);

        var inactiveTenures = await _dbContext.ClubTenures
            .CountAsync(tenure => tenure.ControlStatus == ClubTenureControlStatus.Inactive, cancellationToken);

        var closedTenures = await _dbContext.ClubTenures
            .CountAsync(tenure => tenure.ControlStatus == ClubTenureControlStatus.Closed, cancellationToken);

        var activeAccounts = await _dbContext.Users
            .CountAsync(user => user.Status == UserStatus.Active, cancellationToken);

        var accountsActiveRecently = await _dbContext.Users
            .CountAsync(user => user.LastLoginAt >= activeSince, cancellationToken);

        return new OperationalAnalyticsSnapshot(
            world.Id,
            world.CurrentSeasonNumber,
            generatedAt,
            new OnboardingFunnelCounts(registered, verified, profilesCreated, clubsClaimed),
            new RetentionFunnelCounts(
                activeTenures,
                inactiveTenures,
                closedTenures,
                activeAccounts,
                accountsActiveRecently));
    }
}
