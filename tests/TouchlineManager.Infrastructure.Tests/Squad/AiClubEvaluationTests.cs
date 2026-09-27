using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// The AI club evaluation over a real seeded world (`INS-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// <para>
/// This is the guarantee the pure policy tests cannot make: that the evaluation reaches every club no human
/// holds, writes through the real tables, produces plans the human validator accepts against the real
/// squads, is idempotent on a repeat, and leaves a club a manager holds alone.
/// </para>
/// <para>
/// Its own collection, and therefore its own seeded world, because the evaluation writes the squad plans
/// that the tactics and world tests assert are absent. Sharing their world would make this test's outcome
/// depend on which class xUnit happened to run first.
/// </para>
/// </remarks>
[Collection(Name)]
public sealed class AiClubEvaluationTests : WorldTestBase
{
    /// <summary>The collection name. Its own, so the evaluation never races the world module's reads.</summary>
    public const string Name = "ai-clubs";

    /// <summary>Initializes the tests.</summary>
    public AiClubEvaluationTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Every_AI_club_is_given_a_side_the_human_validator_accepts()
    {
        await EvaluateAsync();

        await using var scope = Fixture.CreateScope();
        var clubs = await scope.ServiceProvider
            .GetRequiredService<IAiClubRepository>()
            .LoadAiClubsAsync(CancellationToken.None);

        clubs.Should().NotBeEmpty("the seeded world starts with every club under AI control (WORLD-5)");

        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var plans = await db.TacticalPlans.Where(plan => plan.IsDefault).ToListAsync();
        var slots = await db.TacticalSlots.ToListAsync();
        var trainingPlans = await db.TrainingPlans.ToListAsync();

        plans.Select(plan => plan.ClubId).Should().OnlyHaveUniqueItems("a club has one default plan (INS-11)");
        trainingPlans.Select(plan => plan.ClubId).Should().OnlyHaveUniqueItems("a club has one training plan (TRN-1)");

        foreach (var club in clubs)
        {
            var plan = plans.SingleOrDefault(candidate => candidate.ClubId == club.ClubId);

            plan.Should().NotBeNull("every AI club has a default plan");
            trainingPlans.Should().ContainSingle(candidate => candidate.ClubId == club.ClubId);

            var clubSlots = slots
                .Where(slot => slot.PlanId == plan!.Id)
                .OrderBy(slot => slot.SlotNumber)
                .ToList();

            clubSlots.Should().HaveCount(FormationLayouts.SlotCount);

            var validation = TacticalPlanValidator.Validate(
                clubSlots.Select(slot => new TacticalSlotDefinition(
                    slot.SlotNumber,
                    slot.PositionFamily,
                    slot.Role,
                    slot.NormalizedX,
                    slot.NormalizedY,
                    slot.AssignedPlayerId)),
                [.. club.Players.Select(player => player.PlayerId)],
                [.. club.Players.Where(player => !player.IsAvailable).Select(player => player.PlayerId)]);

            validation.IsValid.Should().BeTrue("INS-12 gives the AI no bypass");
            validation.IsComplete.Should().BeTrue("a seeded squad always fields eleven (SQ-1)");
        }
    }

    [Fact]
    public async Task A_repeat_run_writes_nothing_new()
    {
        await EvaluateAsync();

        var stored = await CountPlansAsync();

        await using var scope = Fixture.CreateScope();
        var repeat = await scope.ServiceProvider
            .GetRequiredService<EvaluateAiClubs>()
            .ExecuteAsync(CancellationToken.None);

        repeat.TacticalPlansCreated.Should().Be(0, "a club that already has a side keeps it (OCC-5)");
        repeat.TrainingPlansCreated.Should().Be(0, "a club that already has a training plan keeps it");
        repeat.Skipped.Should().Be(0);

        (await CountPlansAsync()).Should().Be(stored, "the second pass changes no row");
    }

    [Fact]
    public async Task A_club_a_human_holds_is_never_loaded()
    {
        Guid heldClubId;

        await using (var scope = Fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
            var userId = await CreateUserAsync(scope, UserStatus.Active, CancellationToken.None);
            var managerId = await CreateManagerForAsync(scope, userId, CancellationToken.None);

            heldClubId = await FreeClubIdAsync(scope, Fixture.Countries[0].Id, CancellationToken.None);

            db.ClubTenures.Add(ClubTenure.Start(
                Guid.CreateVersion7(),
                heldClubId,
                managerId,
                $"takeover-{heldClubId:N}",
                Fixture.Clock.UtcNow));

            await db.SaveChangesAsync(CancellationToken.None);
        }

        await using var readScope = Fixture.CreateScope();
        var clubs = await readScope.ServiceProvider
            .GetRequiredService<IAiClubRepository>()
            .LoadAiClubsAsync(CancellationToken.None);

        clubs.Should().NotContain(club => club.ClubId == heldClubId, "an open tenure occupies its club (OCC-8)");
        clubs.Should().HaveCount(108 - 1, "every other club in the seeded world is still the AI's");

        // The evaluation is measured against exactly that list, so a held club is never considered — the
        // load is its only input.
        var result = await readScope.ServiceProvider
            .GetRequiredService<EvaluateAiClubs>()
            .ExecuteAsync(CancellationToken.None);

        result.Clubs.Should().Be(clubs.Count);
    }

    private async Task EvaluateAsync()
    {
        await using var scope = Fixture.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<EvaluateAiClubs>()
            .ExecuteAsync(CancellationToken.None);
    }

    private async Task<(int Plans, int TrainingPlans, int Slots)> CountPlansAsync()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return (
            await db.TacticalPlans.CountAsync(plan => plan.IsDefault),
            await db.TrainingPlans.CountAsync(),
            await db.TacticalSlots.CountAsync());
    }
}

/// <summary>Shares one seeded world with the AI evaluation, isolated from the world module's collection.</summary>
[CollectionDefinition(AiClubEvaluationTests.Name)]
public sealed class AiClubCollection : ICollectionFixture<WorldFixture>
{
}
