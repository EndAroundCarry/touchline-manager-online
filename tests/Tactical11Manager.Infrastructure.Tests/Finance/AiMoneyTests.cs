using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Infrastructure.Persistence;
using Tactical11Manager.Infrastructure.Tests.World;

namespace Tactical11Manager.Infrastructure.Tests.Finance;

/// <summary>
/// What computer clubs do with their money against real PostgreSQL 17: they build through the writer a manager uses, and their market reads
/// wages and a budget (`ai-finance-v1`, `ai-market-v2`, plan toasty-scott section 7).
/// </summary>
/// <remarks>Its own world per test, because the tests spend a club's cash and list its players.</remarks>
public sealed class AiMoneyTests : IAsyncLifetime, IDisposable
{
    private readonly AiMoneyFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Every_club_of_a_new_world_is_a_computer_club_with_an_account_and_the_season_has_published_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IAiFinanceQueries>();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var money = await queries.LoadAiClubMoneyAsync(CancellationToken.None);

        money.Should().HaveCount(await db.Clubs.CountAsync());
        money.Should().OnlyContain(club => club.State == FinancialStateKind.Healthy && club.AvailableMinor > 0);
        (await queries.GetRoundsPublishedAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task A_computer_club_with_a_big_following_upgrades_a_building_through_the_ledger_and_the_job_a_manager_uses()
    {
        Guid clubId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var clubs = await db.ClubFanBases.OrderBy(fanBase => fanBase.ClubId).Take(1).ToListAsync();
            var fanBase = clubs.Single();

            clubId = fanBase.ClubId;
            fanBase.Apply(new FanBaseState(Fans: 400_000, MoodBp: FanRuleSet.CalmMoodBp, LastWeekGap: null), _fixture.Clock.UtcNow);

            // Cash enough that the reserve and the step's price are no obstacle.
            var account = await db.ClubAccounts.SingleAsync(candidate => candidate.ClubId == clubId);

            account.Post(LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, 500_000_000_000), _fixture.Clock.UtcNow);

            await db.SaveChangesAsync();
        }

        EvaluateAiBuildingResult result;

        await using (var scope = _fixture.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<EvaluateAiBuilding>().ExecuteAsync(CancellationToken.None);
        }

        result.Skipped.Should().Be(0);
        result.Upgrades.Should().BeGreaterThan(0);

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await db.LedgerEntries.CountAsync(entry => entry.ClubId == clubId && entry.Category == LedgerCategory.StructureUpgrade))
                .Should().BeGreaterThan(0);
            (await db.ClubStructures.Where(building => building.ClubId == clubId).ToListAsync()).Should().Contain(building => building.IsBuilding);
            (await db.Jobs.CountAsync(job => job.JobType == Tactical11Manager.Application.Jobs.StructureJobTypes.CompleteUpgrade))
                .Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task The_market_pass_reads_every_clubs_wages_and_budget_from_the_database_without_a_refusal()
    {
        await using var scope = _fixture.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<EvaluateAiMarket>().ExecuteAsync(CancellationToken.None);

        result.Clubs.Should().BeGreaterThan(0);
        result.Skipped.Should().Be(0);
    }
}

/// <summary>The computer clubs' money tests' own seeded world, one per test.</summary>
public sealed class AiMoneyFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public AiMoneyFixture()
        : base("tactical11_aimoney")
    {
    }
}
