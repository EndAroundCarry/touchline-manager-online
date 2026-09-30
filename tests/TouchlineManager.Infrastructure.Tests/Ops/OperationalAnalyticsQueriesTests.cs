using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Ops;

/// <summary>
/// The operational funnels, counted from real rows in real PostgreSQL (master plan §16 Stage 13, `F-54`,
/// ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// The counts are asserted as deltas, never as absolutes: the fixture's world is shared across the funnel
/// tests, so the only honest question is whether walking one manager through the funnel moved each count by
/// exactly the step it should.
/// </para>
/// <para>
/// The query is run through the same service collection the composition roots build, so the schema is the
/// real one and the auth-account join is exercised the way it is in production.
/// </para>
/// </remarks>
[Collection(AnalyticsCollection.Name)]
public sealed class OperationalAnalyticsQueriesTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public OperationalAnalyticsQueriesTests(AnalyticsFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task A_manager_walking_the_funnel_moves_each_step_by_one()
    {
        await using var scope = Fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IOperationalAnalyticsQueries>();

        var before = await queries.GetAsync(CancellationToken.None);

        before.Should().NotBeNull();
        before!.WorldId.Should().Be(Fixture.WorldId);
        before.SeasonNumber.Should().Be(1);

        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var clubId = await FreeClubIdAsync(scope, CountryId("ROU"), CancellationToken.None);

        await ClaimAsync(scope, userId, clubId);

        var after = await queries.GetAsync(CancellationToken.None);

        after!.Onboarding.Registered.Should().Be(before.Onboarding.Registered + 1);
        after.Onboarding.Verified.Should().Be(before.Onboarding.Verified + 1);
        after.Onboarding.ProfilesCreated.Should().Be(before.Onboarding.ProfilesCreated + 1);
        after.Onboarding.ClubsClaimed.Should().Be(before.Onboarding.ClubsClaimed + 1);
        after.Retention.ActiveTenures.Should().Be(before.Retention.ActiveTenures + 1);

        // The funnel never widens as it descends, which is what makes it a funnel rather than four numbers.
        after.Onboarding.Registered.Should().BeGreaterThanOrEqualTo(after.Onboarding.Verified);
        after.Onboarding.Verified.Should().BeGreaterThanOrEqualTo(after.Onboarding.ProfilesCreated);
        after.Onboarding.ProfilesCreated.Should().BeGreaterThanOrEqualTo(after.Onboarding.ClubsClaimed);
    }

    [Fact]
    public async Task A_closed_tenure_leaves_the_active_count_and_joins_the_closed_one()
    {
        await using var scope = Fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IOperationalAnalyticsQueries>();

        var before = await queries.GetAsync(CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var clubId = await FreeClubIdAsync(scope, CountryId("ROU"), CancellationToken.None);

        await ClaimAsync(scope, userId, clubId);

        var claimed = await queries.GetAsync(CancellationToken.None);

        claimed!.Retention.ActiveTenures.Should().Be(before!.Retention.ActiveTenures + 1);

        var resigned = await scope.ServiceProvider
            .GetRequiredService<ResignClub>()
            .ExecuteAsync(userId, CancellationToken.None);

        resigned.Outcome.Should().Be(ResignClubOutcome.Resigned);

        var after = await queries.GetAsync(CancellationToken.None);

        after!.Retention.ActiveTenures.Should().Be(before.Retention.ActiveTenures);
        after.Retention.ClosedTenures.Should().Be(before.Retention.ClosedTenures + 1);
    }

    private Guid CountryId(string code) =>
        Fixture.Countries.Single(country => country.Code == code).Id;

    private static Task<ClaimClubResult> ClaimAsync(AsyncServiceScope scope, Guid userId, Guid clubId) =>
        scope.ServiceProvider
            .GetRequiredService<ClaimClub>()
            .ExecuteAsync(
                userId,
                new ClaimClubRequest { ClubId = clubId },
                Guid.CreateVersion7().ToString(),
                CancellationToken.None);
}
