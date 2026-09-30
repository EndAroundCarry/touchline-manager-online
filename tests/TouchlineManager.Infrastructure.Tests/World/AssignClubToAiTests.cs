using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.World;

/// <summary>
/// The operator's ownership repair: handing a club back to the AI before the inactivity ladder does
/// (master plan §10.8, `OCC-6`, `F-46`, ADR-0045).
/// </summary>
/// <remarks>
/// Every command runs through the use case the API calls, against real PostgreSQL and the real advisory
/// locks, because the interesting claims — that the tenure closes, that no cooldown starts, and that the
/// club's own state is untouched — are claims about rows.
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class AssignClubToAiTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public AssignClubToAiTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task An_operator_hands_a_club_to_the_ai_without_a_cooldown_and_keeps_the_club_intact()
    {
        await using var scope = Fixture.CreateScope();
        var countryId = Fixture.Countries.Single(country => country.Code == LaunchCountries.EnglandCode).Id;
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var clubId = await FreeClubIdAsync(scope, countryId, CancellationToken.None);

        (await scope.ServiceProvider
            .GetRequiredService<ClaimClub>()
            .ExecuteAsync(
                userId,
                new ClaimClubRequest { ClubId = clubId },
                Guid.CreateVersion7().ToString(),
                CancellationToken.None))
            .Outcome.Should().Be(ClaimClubOutcome.Claimed);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var managerId = await db.Managers
            .Where(manager => manager.UserId == userId)
            .Select(manager => manager.Id)
            .SingleAsync();
        var cashBefore = await db.ClubAccounts
            .Where(account => account.ClubId == clubId)
            .Select(account => account.CashMinor)
            .SingleAsync(CancellationToken.None);

        const string reason = "operator repair under test";

        var result = await scope.ServiceProvider
            .GetRequiredService<AssignClubToAi>()
            .ExecuteAsync(clubId, reason, CancellationToken.None);

        result.Outcome.Should().Be(AssignClubToAiOutcome.Applied);
        result.ClubId.Should().Be(clubId);
        result.ManagerId.Should().Be(managerId);

        var tenure = await db.ClubTenures.SingleAsync(candidate => candidate.ManagerId == managerId);

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Closed);
        tenure.EndReason.Should().Be(ClubTenureEndReasons.AdministratorClosed);

        // OCC-4's cooldown is resignation-only; a repair the game makes on its own authority is not a
        // manager shopping for clubs.
        (await db.Managers.SingleAsync(manager => manager.Id == managerId))
            .TakeoverCooldownUntil.Should().BeNull();

        // OCC-5: nothing about the club itself rewound.
        (await db.ClubAccounts
            .Where(account => account.ClubId == clubId)
            .Select(account => account.CashMinor)
            .SingleAsync(CancellationToken.None))
            .Should().Be(cashBefore);

        // The club is free again, so the AI controls it and another manager may claim it.
        (await db.ClubTenures.CountAsync(candidate => candidate.ClubId == clubId && candidate.EndedAt == null))
            .Should().Be(0, "closing the tenure is what returns the club to the AI (OCC-3)");

        (await db.AuditEntries.AsNoTracking().Where(entry => entry.TargetId == tenure.Id).ToListAsync())
            .Should()
            .Contain(entry => entry.Action == WorldAuditActions.ClubAssignedToAi && entry.Reason == reason);
    }

    [Fact]
    public async Task A_club_that_already_has_no_manager_is_left_alone()
    {
        await using var scope = Fixture.CreateScope();
        var countryId = Fixture.Countries.Single(country => country.Code == LaunchCountries.EnglandCode).Id;
        var clubId = await FreeClubIdAsync(scope, countryId, CancellationToken.None);

        var result = await scope.ServiceProvider
            .GetRequiredService<AssignClubToAi>()
            .ExecuteAsync(clubId, "already under AI control", CancellationToken.None);

        result.Outcome.Should().Be(AssignClubToAiOutcome.AlreadyAi);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        (await db.AuditEntries.AsNoTracking().AnyAsync(entry => entry.Reason == "already under AI control"))
            .Should().BeFalse("nothing changed, so nothing is audited");
    }

    [Fact]
    public async Task An_unknown_club_is_not_found()
    {
        await using var scope = Fixture.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<AssignClubToAi>()
            .ExecuteAsync(Guid.CreateVersion7(), "no such club", CancellationToken.None);

        result.Outcome.Should().Be(AssignClubToAiOutcome.ClubNotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused(string reason)
    {
        await using var scope = Fixture.CreateScope();

        var act = async () => await scope.ServiceProvider
            .GetRequiredService<AssignClubToAi>()
            .ExecuteAsync(Guid.CreateVersion7(), reason, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
