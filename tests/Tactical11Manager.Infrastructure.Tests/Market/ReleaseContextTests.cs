using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.Market;

/// <summary>
/// The facts a release is decided from, read over a real seeded world (`CON-12`).
/// </summary>
/// <remarks>
/// Its own world per test: one test closes the season and lists a player, which the other must not see.
/// </remarks>
public sealed class ReleaseContextTests : IAsyncLifetime, IDisposable
{
    private readonly FreeAgentFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task The_context_reads_the_contract_the_squad_and_the_calendar()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var contract = await db.PlayerContracts
            .Where(candidate => candidate.Status == ContractStatus.Active)
            .OrderBy(candidate => candidate.PlayerId)
            .FirstAsync();
        var season = await db.Seasons.SingleAsync(candidate => candidate.SequenceNumber == 1);
        var registered = await db.PlayerContracts.CountAsync(candidate =>
            candidate.ClubId == contract.ClubId && candidate.Status == ContractStatus.Active);
        var goalkeepers = await (
            from other in db.PlayerContracts
            join player in db.Players on other.PlayerId equals player.Id
            where other.ClubId == contract.ClubId
                && other.Status == ContractStatus.Active
                && player.PrimaryPosition == PlayerPosition.Goalkeeper
            select other.Id)
            .CountAsync();

        var context = await scope.ServiceProvider.GetRequiredService<ISquadQueries>()
            .GetReleaseContextAsync(contract.Id, CancellationToken.None);

        context.Should().NotBeNull();
        context!.ContractId.Should().Be(contract.Id);
        context.PlayerId.Should().Be(contract.PlayerId);
        context.ClubId.Should().Be(contract.ClubId);
        context.ContractVersion.Should().Be(contract.Version);
        context.SeasonWageMinor.Should().Be(contract.SeasonWageMinor);
        context.EndSeasonNumber.Should().Be(contract.EndSeasonNumber);
        context.CurrentSeasonNumber.Should().Be(1);
        context.CurrentSeasonEndsAt.Should().Be(season.RolloverEndsAt, "the season ends when its rollover does");
        context.WeeksPerSeason.Should().BeInRange(10, 14, "30 matchdays on three days a week fill ten weeks, and the rollover takes the eleventh");
        context.IsRollover.Should().BeFalse();
        context.IsListed.Should().BeFalse();
        context.RegisteredCount.Should().Be(registered);
        context.GoalkeeperCount.Should().Be(goalkeepers);
    }

    [Fact]
    public async Task The_context_sees_a_closing_season_and_an_open_listing()
    {
        Guid contractId;
        Guid playerId;
        Guid clubId;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var contract = await db.PlayerContracts
                .Where(candidate => candidate.Status == ContractStatus.Active)
                .OrderBy(candidate => candidate.PlayerId)
                .FirstAsync();

            contractId = contract.Id;
            playerId = contract.PlayerId;
            clubId = contract.ClubId;

            var write = await scope.ServiceProvider.GetRequiredService<IListingWriter>().ListAsync(
                clubId,
                new MarketActor(AuditActorTypes.User, UserId: null, IpHash: null, "release-context-test"),
                playerId,
                minimumFeeMinor: 1_000_000,
                seasons: 2,
                idempotencyKey: null,
                CancellationToken.None);

            write.Outcome.Should().Be(MarketOutcome.Found);

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            await db.Database.ExecuteSqlRawAsync(
                "update competition.seasons set status = 'rollover' where sequence_number = 1");
        }

        await using var read = _fixture.CreateScope();

        var context = await read.ServiceProvider.GetRequiredService<ISquadQueries>()
            .GetReleaseContextAsync(contractId, CancellationToken.None);

        context.Should().NotBeNull();
        context!.IsListed.Should().BeTrue("the player has an open listing (TRF-14)");
        context.IsRollover.Should().BeTrue("no contract may change while the season rolls over (CON-8)");
    }

    [Fact]
    public async Task A_closed_contract_has_no_context()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var contract = await db.PlayerContracts
            .Where(candidate => candidate.Status == ContractStatus.Active)
            .OrderBy(candidate => candidate.PlayerId)
            .FirstAsync();

        contract.Close(PlayerContractCloseReasons.Released, _fixture.Clock.UtcNow);

        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<ISquadQueries>()
                .GetReleaseContextAsync(contract.Id, CancellationToken.None))
            .Should().BeNull("a released contract cannot be released twice");
    }
}
