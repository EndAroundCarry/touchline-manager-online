using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The deterministic AI club policy (`INS-12`).
/// </summary>
/// <remarks>
/// The policy is a pure function, so these tests need neither a clock nor a database. What they pin is the
/// contract the evaluator depends on: the same club always decides the same way, different clubs differ,
/// every decision is one a human validator accepts, and a squad that cannot field a legal eleven still
/// yields a legal shape rather than an illegal side.
/// </remarks>
public sealed class AiClubPolicyTests
{
    /// <summary>Twenty-two players in the generated composition: three goalkeepers and nineteen outfielders.</summary>
    private static readonly PlayerPosition[] Composition =
    [
        PlayerPosition.Goalkeeper,
        PlayerPosition.Goalkeeper,
        PlayerPosition.Goalkeeper,
        PlayerPosition.RightBack,
        PlayerPosition.CentreBack,
        PlayerPosition.CentreBack,
        PlayerPosition.LeftBack,
        PlayerPosition.RightBack,
        PlayerPosition.CentreBack,
        PlayerPosition.LeftBack,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.AttackingMidfielder,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.AttackingMidfielder,
        PlayerPosition.RightWinger,
        PlayerPosition.LeftWinger,
        PlayerPosition.Striker,
        PlayerPosition.RightWinger,
        PlayerPosition.Striker,
    ];

    [Fact]
    public void The_same_club_and_squad_decide_the_same_way()
    {
        var first = AiClubPolicy.Decide(ClubId(1), Squad());
        var second = AiClubPolicy.Decide(ClubId(1), Squad());

        // Compared field by field: a record's collection member is compared by reference, so the two
        // independently-built slot lists would never be the same instance.
        second.Formation.Should().Be(first.Formation, "the draw is seeded from the club identity (INS-12)");
        second.Instructions.Should().Be(first.Instructions);
        second.Slots.Should().Equal(first.Slots);
        second.TrainingIntensity.Should().Be(first.TrainingIntensity);
    }

    [Fact]
    public void Different_clubs_choose_different_tactics()
    {
        var formations = Enumerable
            .Range(0, 50)
            .Select(index => AiClubPolicy.Decide(ClubId(index), Squad()).Formation)
            .Distinct()
            .ToList();

        formations.Should()
            .HaveCountGreaterThan(1, "a division of eighteen identical sides is not a league (INS-12)");
    }

    [Fact]
    public void Every_decision_is_one_a_human_save_would_accept()
    {
        for (var index = 0; index < 50; index++)
        {
            var squad = Squad();
            var plan = AiClubPolicy.Decide(ClubId(index), squad);
            var validation = TacticalPlanValidator.Validate(
                Definitions(plan),
                [.. squad.Select(player => player.PlayerId)],
                []);

            validation.IsValid.Should().BeTrue("INS-12 holds the AI to the manager's validator");
            validation.IsComplete.Should().BeTrue("a full squad always fields eleven");
        }
    }

    [Fact]
    public void Every_slot_is_filled_once_with_its_own_family_and_role()
    {
        var squad = Squad();
        var plan = AiClubPolicy.Decide(ClubId(7), squad);

        plan.Slots.Should().HaveCount(FormationLayouts.SlotCount);
        plan.Slots.Select(slot => slot.SlotNumber).Should().OnlyHaveUniqueItems();
        plan.Slots.Select(slot => slot.SlotNumber).Should().Equal(Enumerable.Range(1, 11));
        plan.Slots.Should().OnlyContain(slot => PlayerRoles.FamilyOf(slot.Role) == slot.PositionFamily);

        var assigned = plan.Slots.Select(slot => slot.AssignedPlayerId).ToList();

        assigned.Should().NotContainNulls();
        assigned.Should().OnlyHaveUniqueItems("a player cannot occupy two slots (SQ-4)");
    }

    [Fact]
    public void Exactly_one_recognised_goalkeeper_is_in_the_eleven_and_none_outfield()
    {
        var squad = Squad();
        var byId = squad.ToDictionary(player => player.PlayerId);
        var plan = AiClubPolicy.Decide(ClubId(11), squad);

        var goalkeepers = plan.Slots
            .Where(slot => slot.AssignedPlayerId is not null)
            .Count(slot => byId[slot.AssignedPlayerId!.Value].PrimaryPosition == PlayerPosition.Goalkeeper);

        goalkeepers.Should().Be(1, "the engine refuses a side with no keeper or with two (MAT-6)");
    }

    [Fact]
    public void Only_available_players_are_picked()
    {
        // Exactly eleven available: one goalkeeper and ten outfielders. The policy must pick them all and
        // nobody who is injured or suspended (TRN-12, DIS-5).
        var squad = Squad();
        var available = new HashSet<Guid>
        {
            squad.First(player => player.PrimaryPosition == PlayerPosition.Goalkeeper).PlayerId,
        };

        foreach (var player in squad.Where(player => player.PrimaryPosition != PlayerPosition.Goalkeeper).Take(10))
        {
            available.Add(player.PlayerId);
        }

        var limited = squad
            .Select(player => player with { IsAvailable = available.Contains(player.PlayerId) })
            .ToList();

        var plan = AiClubPolicy.Decide(ClubId(3), limited);

        plan.Slots.Select(slot => slot.AssignedPlayerId).Should().BeEquivalentTo(available);
    }

    [Fact]
    public void A_squad_with_no_available_goalkeeper_still_yields_a_legal_plan_with_no_lineup()
    {
        var squad = Squad(includeGoalkeepers: false);
        var plan = AiClubPolicy.Decide(ClubId(5), squad);

        plan.Slots.Should().HaveCount(FormationLayouts.SlotCount);
        plan.Slots.Should().OnlyContain(slot => slot.AssignedPlayerId == null);

        var validation = TacticalPlanValidator.Validate(
            Definitions(plan),
            [.. squad.Select(player => player.PlayerId)],
            []);

        validation.IsValid.Should().BeTrue(
            "a plan with a shape and no lineup is legal; the snapshot builder decides the eleven at the lock");
    }

    [Fact]
    public void Training_is_normal_intensity_for_every_club()
    {
        for (var index = 0; index < 20; index++)
        {
            var plan = AiClubPolicy.Decide(ClubId(index), Squad());

            plan.TrainingIntensity.Should().Be(TrainingIntensity.Normal);
        }
    }

    private static IEnumerable<TacticalSlotDefinition> Definitions(AiClubPlan plan) =>
        plan.Slots.Select(slot => new TacticalSlotDefinition(
            slot.SlotNumber,
            slot.PositionFamily,
            slot.Role,
            slot.NormalizedX,
            slot.NormalizedY,
            slot.AssignedPlayerId));

    private static Guid ClubId(int index) => Guid.Parse($"0192f100-0000-7000-8000-{index:D12}");

    private static Guid PlayerId(int ordinal) => Guid.Parse($"0192f200-0000-7000-8000-{ordinal:D12}");

    private static List<AiSquadPlayer> Squad(bool includeGoalkeepers = true)
    {
        var players = new List<AiSquadPlayer>();
        var ordinal = 0;

        foreach (var position in Composition)
        {
            ordinal++;

            if (!includeGoalkeepers && position == PlayerPosition.Goalkeeper)
            {
                continue;
            }

            players.Add(new AiSquadPlayer(
                PlayerId(ordinal),
                position,
                [],
                [.. Enumerable.Range(0, AttributeNames.Count).Select(index => 10 + ((ordinal + index) % 6))],
                8_000,
                IsAvailable: true));
        }

        return players;
    }
}
