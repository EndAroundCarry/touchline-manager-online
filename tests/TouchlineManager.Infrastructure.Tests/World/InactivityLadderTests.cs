using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.World;

/// <summary>
/// The inactivity ladder as it actually runs (`OCC-1`–`OCC-3`, `OCC-8`, ADR-0027).
/// </summary>
/// <remarks>
/// <para>
/// Against real PostgreSQL with the real repositories and the fixture's clock, so the thresholds are tested
/// as elapsed real time rather than as arithmetic. The latch is driven by back-dating a tenure's
/// <c>LastActiveAt</c> rather than by moving the shared clock: the ladder reads elapsed time, and a test that
/// moved a clock the whole collection shares would age every other suite's tenures with it.
/// </para>
/// <para>
/// The world is the shared seeded one, and every tenure is opened in England. The other suites take the other
/// countries and some assert their exact occupancy, so confining this suite to one country no peer asserts
/// on keeps the ladder's scenery out of their fixtures. Every other tenure a peer leaves open was created at
/// the current instant, so its elapsed time is zero and the ladder leaves it alone.
/// </para>
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class InactivityLadderTests : WorldTestBase
{
    /// <summary>The country this suite uses, so it never collides with a peer's occupancy assertions.</summary>
    private const string Country = "ENG";

    /// <summary>Initializes the tests.</summary>
    public InactivityLadderTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task A_quiet_manager_is_warned_at_ten_days_and_not_warned_again()
    {
        Guid managerId;
        Guid tenureId;

        await using (var scope = Fixture.CreateScope())
        {
            var claim = await QuietTenureAsync(scope, TimeSpan.FromDays(11));
            managerId = claim.ManagerId;
            tenureId = claim.TenureId;

            (await RunLadderAsync(scope)).Warned.Should().BeGreaterOrEqualTo(1);

            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (await db.ClubTenures.SingleAsync(tenure => tenure.Id == tenureId))
                .InactivityWarningAt.Should().NotBeNull();

            (await db.InboxMessages.CountAsync(
                message => message.RecipientManagerId == managerId
                    && message.TemplateKey == "inbox.occupancy.inactivity_warning"))
                .Should().Be(1);

            (await db.OutboxMessages.CountAsync(message => message.Type == "email"))
                .Should().BeGreaterOrEqualTo(1, "the warning emails the manager unless they opted out");
        }

        await using (var scope = Fixture.CreateScope())
        {
            (await RunLadderAsync(scope)).Warned.Should().Be(0, "the warning is once per lapse (OCC-1)");

            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            (await db.InboxMessages.CountAsync(
                message => message.RecipientManagerId == managerId
                    && message.TemplateKey == "inbox.occupancy.inactivity_warning"))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task A_quiet_manager_hands_control_to_the_ai_at_fourteen_days_and_a_return_resumes_it()
    {
        await using var scope = Fixture.CreateScope();
        var claim = await QuietTenureAsync(scope, TimeSpan.FromDays(15));

        (await RunLadderAsync(scope)).MarkedInactive.Should().BeGreaterOrEqualTo(1);

        // OCC-2: logging in is the return. `Login` calls exactly this on a successful sign-in.
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var tenure = await db.ClubTenures.SingleAsync(candidate => candidate.Id == claim.TenureId);

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Inactive);

        tenure.Resume(Fixture.Clock.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Active);
        tenure.InactivityWarningAt.Should().BeNull();
    }

    [Fact]
    public async Task A_quiet_manager_loses_the_club_at_twenty_one_days_and_it_is_claimable_again()
    {
        Guid clubId;

        await using (var scope = Fixture.CreateScope())
        {
            var claim = await QuietTenureAsync(scope, TimeSpan.FromDays(22));
            clubId = claim.ClubId;

            (await RunLadderAsync(scope)).Closed.Should().BeGreaterOrEqualTo(1);

            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var tenure = await db.ClubTenures.SingleAsync(candidate => candidate.Id == claim.TenureId);

            tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Closed);
            tenure.EndReason.Should().Be(ClubTenureEndReasons.InactivityClosed);

            (await db.InboxMessages.CountAsync(
                message => message.TemplateKey == "inbox.occupancy.inactivity_closed"))
                .Should().BeGreaterOrEqualTo(1);

            // OCC-5: nothing about the club rewound — it simply has no open tenure any more.
            (await db.ClubTenures.CountAsync(candidate => candidate.ClubId == clubId
                && candidate.ControlStatus != ClubTenureControlStatus.Closed))
                .Should().Be(0);

            (await db.Clubs.SingleAsync(club => club.Id == clubId)).Status.Should().Be(ClubStatus.Active);
        }

        await using (var scope = Fixture.CreateScope())
        {
            // The freed club is claimable again, which is what the closure is for.
            var manager = await CreateManagerAsync(scope, CancellationToken.None);

            var result = await scope.ServiceProvider
                .GetRequiredService<ClaimClub>()
                .ExecuteAsync(
                    manager,
                    new ClaimClubRequest { ClubId = clubId },
                    Guid.CreateVersion7().ToString(),
                    CancellationToken.None);

            result.Outcome.Should().Be(ClaimClubOutcome.Claimed);
        }
    }

    [Fact]
    public async Task A_suspended_account_is_skipped_by_the_ladder()
    {
        await using var scope = Fixture.CreateScope();
        var claim = await QuietTenureAsync(scope, TimeSpan.FromDays(30));

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == claim.UserId);

        user.Suspend($"stamp-{Guid.NewGuid():N}", Fixture.Clock.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        await RunLadderAsync(scope);

        // OCC-5: a blocked account keeps its club rather than being made an absence of its own making.
        (await db.ClubTenures.SingleAsync(tenure => tenure.Id == claim.TenureId))
            .ControlStatus.Should().NotBe(ClubTenureControlStatus.Closed);
    }

    /// <summary>
    /// Opens a tenure for a fresh manager and back-dates its last-activity stamp, so the ladder sees the
    /// elapsed time the test is about without moving the clock the whole collection shares.
    /// </summary>
    private async Task<QuietTenure> QuietTenureAsync(AsyncServiceScope scope, TimeSpan idle)
    {
        var countryId = Fixture.Countries.Single(country => country.Code == Country).Id;
        var userId = await CreateManagerAsync(scope, CancellationToken.None);
        var clubId = await FreeClubIdAsync(scope, countryId, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var managerId = await db.Managers
            .Where(manager => manager.UserId == userId)
            .Select(manager => manager.Id)
            .SingleAsync();

        var tenure = ClubTenure.Start(
            Guid.CreateVersion7(),
            clubId,
            managerId,
            Guid.CreateVersion7().ToString(),
            Fixture.Clock.UtcNow.Subtract(idle));

        db.ClubTenures.Add(tenure);
        await db.SaveChangesAsync(CancellationToken.None);

        return new QuietTenure(userId, managerId, tenure.Id, clubId);
    }

    private static Task<InactivityResult> RunLadderAsync(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<EvaluateInactivity>().ExecuteAsync(CancellationToken.None);

    /// <summary>The ids a back-dated tenure gives a test.</summary>
    private sealed record QuietTenure(Guid UserId, Guid ManagerId, Guid TenureId, Guid ClubId);
}
