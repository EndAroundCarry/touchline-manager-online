using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.World;

/// <summary>
/// A new manager's clean slate: the fans go back to the tier's opening numbers on every takeover, once, and a resignation leaves them
/// alone (`FAN-9`).
/// </summary>
/// <remarks>
/// Every claim is made through the use case the API calls, against real PostgreSQL. The tests share the seeded world, so each one asks for a
/// free club of Spain and writes the fans it wants into that club before the claim.
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class FanTakeoverTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public FanTakeoverTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData(60_000, 9_500, "well above its tier's defaults")]
    [InlineData(4_000, 300, "well below its tier's defaults")]
    public async Task Claiming_a_club_resets_its_fans_to_the_tiers_defaults_and_writes_one_takeover_event(
        int fans,
        int moodBp,
        string reason)
    {
        await using var scope = Fixture.CreateScope();
        var clubId = await FreeClubIdAsync(scope, Fixture.Countries.Single(country => country.Code == "ESP").Id, CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);

        await SetFansAsync(clubId, fans, moodBp, lastWeekGap: 4, target: 17);

        var takeoversBefore = await CountTakeoversAsync(clubId);
        var claimed = await ClaimAsync(scope, userId, clubId, Guid.CreateVersion7().ToString());

        claimed.Outcome.Should().Be(ClaimClubOutcome.Claimed, reason);

        await using var read = Fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fanBase = await db.ClubFanBases.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);
        var opening = FanRuleSet.OpeningFor(1);
        var tenure = await db.ClubTenures.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId && candidate.EndedAt == null);
        var takeovers = await db.ClubFanEvents.AsNoTracking()
            .Where(fanEvent => fanEvent.ClubId == clubId && fanEvent.Kind == FanEventKind.Takeover && fanEvent.TenureId == tenure.Id)
            .ToListAsync();

        fanBase.Fans.Should().Be(opening.Fans, reason);
        fanBase.MoodBp.Should().Be(opening.MoodBp);
        fanBase.LastWeekGap.Should().BeNull("judging closer or further from the target starts from the takeover");
        fanBase.SeasonTargetRank.Should().BeInRange(1, 18, "the target is worked out again from today's squad");

        takeovers.Should().ContainSingle();
        (await CountTakeoversAsync(clubId)).Should().Be(takeoversBefore + 1, "one takeover writes one event");
        takeovers[0].FansBefore.Should().Be(fans);
        takeovers[0].FansAfter.Should().Be(opening.Fans);
        takeovers[0].MoodBeforeBp.Should().Be(moodBp);
        takeovers[0].MoodAfterBp.Should().Be(opening.MoodBp);

        // The history before the takeover stays.
        (await db.ClubFanEvents.CountAsync(fanEvent => fanEvent.ClubId == clubId && fanEvent.Kind == FanEventKind.Opening))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_retried_claim_with_the_same_key_does_not_reset_the_fans_or_write_a_second_event()
    {
        await using var scope = Fixture.CreateScope();
        var clubId = await FreeClubIdAsync(scope, Fixture.Countries.Single(country => country.Code == "ESP").Id, CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var key = Guid.CreateVersion7().ToString();
        var takeoversBefore = await CountTakeoversAsync(clubId);

        (await ClaimAsync(scope, userId, clubId, key)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        // The fans move after the takeover; a retry of the same request must not wipe them again.
        await SetFansAsync(clubId, 12_345, 7_000, lastWeekGap: -2, target: 3);

        await using var retry = Fixture.CreateScope();

        (await ClaimAsync(retry, userId, clubId, key)).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await using var read = Fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fanBase = await db.ClubFanBases.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);

        fanBase.Fans.Should().Be(12_345);
        fanBase.MoodBp.Should().Be(7_000);
        (await CountTakeoversAsync(clubId)).Should().Be(takeoversBefore + 1);
    }

    [Fact]
    public async Task Resigning_leaves_the_fans_as_they_are()
    {
        await using var scope = Fixture.CreateScope();
        var clubId = await FreeClubIdAsync(scope, Fixture.Countries.Single(country => country.Code == "ESP").Id, CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var takeoversBefore = await CountTakeoversAsync(clubId);

        (await ClaimAsync(scope, userId, clubId, Guid.CreateVersion7().ToString())).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await SetFansAsync(clubId, 15_000, 8_200, lastWeekGap: 1, target: 5);

        await using var resign = Fixture.CreateScope();
        var resigned = await resign.ServiceProvider.GetRequiredService<ResignClub>().ExecuteAsync(userId, CancellationToken.None);

        resigned.Outcome.Should().Be(ResignClubOutcome.Resigned);

        await using var read = Fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fanBase = await db.ClubFanBases.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);

        fanBase.Fans.Should().Be(15_000, "the club goes back to the AI with its fans as they are");
        fanBase.MoodBp.Should().Be(8_200);
        fanBase.LastWeekGap.Should().Be(1);
        fanBase.SeasonTargetRank.Should().Be(5);
        (await CountTakeoversAsync(clubId)).Should().Be(takeoversBefore + 1, "a resignation writes no event of its own");
    }

    [Fact]
    public async Task A_manager_who_returns_to_a_club_gets_a_new_takeover_and_a_new_slate()
    {
        await using var scope = Fixture.CreateScope();
        var clubId = await FreeClubIdAsync(scope, Fixture.Countries.Single(country => country.Code == "ESP").Id, CancellationToken.None);
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var takeoversBefore = await CountTakeoversAsync(clubId);

        (await ClaimAsync(scope, userId, clubId, Guid.CreateVersion7().ToString())).Outcome.Should().Be(ClaimClubOutcome.Claimed);
        await scope.ServiceProvider.GetRequiredService<ResignClub>().ExecuteAsync(userId, CancellationToken.None);

        await SetFansAsync(clubId, 20_000, 9_000, lastWeekGap: 0, target: 2);

        // The cooldown is cleared as it would lapse, so the same manager may take a club again without moving the shared clock.
        await using (var cooldown = Fixture.CreateScope())
        {
            var cooldownDb = cooldown.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var manager = await cooldownDb.Managers.SingleAsync(candidate => candidate.UserId == userId);

            manager.ClearTakeoverCooldown(Fixture.Clock.UtcNow);
            await cooldownDb.SaveChangesAsync();
        }

        await using var again = Fixture.CreateScope();

        (await ClaimAsync(again, userId, clubId, Guid.CreateVersion7().ToString())).Outcome.Should().Be(ClaimClubOutcome.Claimed);

        await using var read = Fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fanBase = await db.ClubFanBases.AsNoTracking().SingleAsync(candidate => candidate.ClubId == clubId);
        var takeovers = await db.ClubFanEvents.AsNoTracking()
            .Where(fanEvent => fanEvent.ClubId == clubId && fanEvent.Kind == FanEventKind.Takeover)
            .ToListAsync();

        fanBase.Fans.Should().Be(FanRuleSet.OpeningFor(1).Fans);
        takeovers.Should().HaveCount(takeoversBefore + 2);
        takeovers.Select(fanEvent => fanEvent.TenureId).Should().OnlyHaveUniqueItems("each takeover is its own tenure");
    }

    private async Task<int> CountTakeoversAsync(Guid clubId)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        return await db.ClubFanEvents.CountAsync(fanEvent => fanEvent.ClubId == clubId && fanEvent.Kind == FanEventKind.Takeover);
    }

    private static Task<ClaimClubResult> ClaimAsync(AsyncServiceScope scope, Guid userId, Guid clubId, string idempotencyKey) =>
        scope.ServiceProvider
            .GetRequiredService<ClaimClub>()
            .ExecuteAsync(userId, new ClaimClubRequest { ClubId = clubId }, idempotencyKey, CancellationToken.None);

    /// <summary>Writes the fans a test wants into a club, as the matches and reviews would have left them.</summary>
    private async Task SetFansAsync(Guid clubId, int fans, int moodBp, int lastWeekGap, int target)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var fanBase = await db.ClubFanBases.SingleAsync(candidate => candidate.ClubId == clubId);

        fanBase.Apply(new FanBaseState(fans, moodBp, lastWeekGap), Fixture.Clock.UtcNow);
        fanBase.SetTarget(target, Fixture.Clock.UtcNow);

        await db.SaveChangesAsync();
    }
}
