using FluentAssertions;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Tests.Squad;

/// <summary>
/// The AI club evaluation: what it fills, what it leaves alone, and that it commits once (`INS-12`).
/// </summary>
/// <remarks>
/// The policy itself is pinned by the domain tests. What this suite proves is the orchestration around it:
/// a club with no plans gets both, a club that already has one keeps it and gets the other, a repeated run
/// writes nothing, and every plan it writes is one the human validator accepts.
/// </remarks>
public sealed class EvaluateAiClubsTests
{
    [Fact]
    public async Task A_club_with_no_plans_gets_a_default_side_and_a_training_plan()
    {
        var clubId = ClubId(1);
        var repository = new StubAiClubRepository(Club(clubId));
        var (useCase, tactics, training, unitOfWork) = Create(repository);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        result.Clubs.Should().Be(1);
        result.TacticalPlansCreated.Should().Be(1);
        result.TrainingPlansCreated.Should().Be(1);
        result.Skipped.Should().Be(0);

        tactics.Plans.Should().ContainSingle();
        tactics.Plans[0].ClubId.Should().Be(clubId);
        tactics.Plans[0].IsDefault.Should().BeTrue("the AI's plan becomes the club's default (INS-11)");
        tactics.Slots.Should().HaveCount(FormationLayouts.SlotCount);

        ValidatePlan(tactics.Slots).IsComplete.Should().BeTrue("a full squad always fields eleven");

        training.Plans.Should().ContainSingle();
        training.Plans[0].Intensity.Should().Be(TrainingIntensity.Normal);

        unitOfWork.Saves.Should().Be(1, "a pass commits once, however many clubs it touched");
    }

    [Fact]
    public async Task A_club_that_already_has_one_plan_keeps_it_and_gets_the_other()
    {
        var tacticalOnly = ClubId(1);
        var trainingOnly = ClubId(2);
        var complete = ClubId(3);

        var repository = new StubAiClubRepository(
            Club(tacticalOnly, hasDefaultTacticalPlan: true),
            Club(trainingOnly, hasTrainingPlan: true),
            Club(complete, hasDefaultTacticalPlan: true, hasTrainingPlan: true));

        var (useCase, tactics, training, _) = Create(repository);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        result.Clubs.Should().Be(3);
        result.TacticalPlansCreated.Should().Be(1, "two clubs already have a default plan");
        result.TrainingPlansCreated.Should().Be(1, "two clubs already have a training plan");

        tactics.Plans.Should().ContainSingle().Which.ClubId.Should().Be(trainingOnly);
        training.Plans.Should().ContainSingle().Which.ClubId.Should().Be(tacticalOnly);
        training.Plans.Should().NotContain(plan => plan.ClubId == complete);
    }

    [Fact]
    public async Task A_club_that_already_has_both_plans_is_left_alone()
    {
        var repository = new StubAiClubRepository(
            Club(ClubId(1), hasDefaultTacticalPlan: true, hasTrainingPlan: true));

        var (useCase, tactics, training, unitOfWork) = Create(repository);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        result.TacticalPlansCreated.Should().Be(0);
        result.TrainingPlansCreated.Should().Be(0);
        tactics.Plans.Should().BeEmpty();
        tactics.Slots.Should().BeEmpty();
        training.Plans.Should().BeEmpty();
        unitOfWork.Saves.Should().Be(0, "nothing was written, so nothing is committed");
    }

    [Fact]
    public async Task A_squad_that_cannot_field_eleven_still_gets_a_valid_plan_with_no_lineup()
    {
        var repository = new StubAiClubRepository(Club(ClubId(4), includeGoalkeepers: false));
        var (useCase, tactics, _, _) = Create(repository);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        result.TacticalPlansCreated.Should().Be(1);
        result.Skipped.Should().Be(0, "a shape with no lineup is a legal plan (TAC-10)");

        tactics.Slots.Should().HaveCount(FormationLayouts.SlotCount);
        tactics.Slots.Should().OnlyContain(slot => slot.AssignedPlayerId == null);

        ValidatePlan(tactics.Slots).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Every_plan_it_writes_is_one_the_human_validator_accepts()
    {
        var repository = new StubAiClubRepository(
            [.. Enumerable.Range(0, 12).Select(index => Club(ClubId(index)))]);

        var (useCase, tactics, _, _) = Create(repository);

        await useCase.ExecuteAsync(CancellationToken.None);

        tactics.Plans.Should().HaveCount(12);

        foreach (var plan in tactics.Plans)
        {
            var validation = ValidatePlan(tactics.Slots.Where(slot => slot.PlanId == plan.Id).ToList());

            validation.IsValid.Should().BeTrue("INS-12 gives the AI no bypass");
            validation.IsComplete.Should().BeTrue();
        }
    }

    /// <summary>Validates one plan's eleven slots with the same validator a human save goes through.</summary>
    private static TacticalPlanValidation ValidatePlan(IReadOnlyList<TacticalSlot> slots)
    {
        var selectable = slots
            .Where(slot => slot.AssignedPlayerId is not null)
            .Select(slot => slot.AssignedPlayerId!.Value)
            .ToList();

        var definitions = slots
            .Select(slot => new TacticalSlotDefinition(
                slot.SlotNumber,
                slot.PositionFamily,
                slot.Role,
                slot.NormalizedX,
                slot.NormalizedY,
                slot.AssignedPlayerId))
            .ToList();

        return TacticalPlanValidator.Validate(definitions, selectable, []);
    }

    private static (EvaluateAiClubs UseCase, RecordingTacticsRepository Tactics, RecordingTrainingRepository Training, RecordingUnitOfWork UnitOfWork) Create(StubAiClubRepository repository)
    {
        var tactics = new RecordingTacticsRepository();
        var training = new RecordingTrainingRepository();
        var unitOfWork = new RecordingUnitOfWork();

        return (
            new EvaluateAiClubs(repository, tactics, training, new FixedClock(), unitOfWork),
            tactics,
            training,
            unitOfWork);
    }

    private static Guid ClubId(int index) => Guid.Parse($"0192f300-0000-7000-8000-{index:D12}");

    private static Guid PlayerId(int index) => Guid.Parse($"0192f400-0000-7000-8000-{index:D12}");

    private static AiClubRecord Club(
        Guid clubId,
        bool hasDefaultTacticalPlan = false,
        bool hasTrainingPlan = false,
        bool includeGoalkeepers = true) =>
        new(clubId, hasDefaultTacticalPlan, hasTrainingPlan, Squad(includeGoalkeepers));

    /// <summary>A twenty-two-player squad: three goalkeepers, seven defenders, seven midfielders, five forwards.</summary>
    private static List<AiClubPlayerRow> Squad(bool includeGoalkeepers)
    {
        PlayerPosition[] composition =
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

        var players = new List<AiClubPlayerRow>();
        var ordinal = 0;

        foreach (var position in composition)
        {
            ordinal++;

            if (!includeGoalkeepers && position == PlayerPosition.Goalkeeper)
            {
                continue;
            }

            players.Add(new AiClubPlayerRow(
                PlayerId(ordinal),
                position,
                [],
                [.. Enumerable.Range(0, AttributeNames.Count).Select(index => 10 + ((ordinal + index) % 6))],
                8_000,
                IsAvailable: true));
        }

        return players;
    }

    private sealed class StubAiClubRepository(params AiClubRecord[] clubs) : IAiClubRepository
    {
        private readonly IReadOnlyList<AiClubRecord> _clubs = clubs;

        public Task<IReadOnlyList<AiClubRecord>> LoadAiClubsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_clubs);
    }

    private sealed class RecordingTacticsRepository : ITacticsRepository
    {
        public List<TacticalPlan> Plans { get; } = [];

        public List<TacticalSlot> Slots { get; } = [];

        public void AddPlan(TacticalPlan plan) => Plans.Add(plan);

        public void AddSlot(TacticalSlot slot) => Slots.Add(slot);

        public void AddBenchSlot(TacticalBenchSlot benchSlot)
        {
        }

        public void RemoveBenchSlot(TacticalBenchSlot benchSlot)
        {
        }

        public Task<TacticalPlanRecord?> FindAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult<TacticalPlanRecord?>(null);

        public Task<TacticalPlanRecord?> FindDefaultAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<TacticalPlanRecord?>(null);
    }

    private sealed class RecordingTrainingRepository : ITrainingRepository
    {
        public List<TrainingPlan> Plans { get; } = [];

        public Task<TrainingPlan?> FindPlanAsync(Guid clubId, CancellationToken cancellationToken) =>
            Task.FromResult<TrainingPlan?>(null);

        public void AddTrainingPlan(TrainingPlan plan) => Plans.Add(plan);

        public Task<PlayerTrainingFocus?> FindFocusAsync(Guid playerId, CancellationToken cancellationToken) =>
            Task.FromResult<PlayerTrainingFocus?>(null);

        public Task<TrainablePlayer?> FindTrainablePlayerAsync(Guid playerId, CancellationToken cancellationToken) =>
            Task.FromResult<TrainablePlayer?>(null);

        public void AddPlayerFocus(PlayerTrainingFocus focus)
        {
        }

        public void RemovePlayerFocus(PlayerTrainingFocus focus)
        {
        }

        public void AddTrainingDay(PlayerTrainingDay day)
        {
        }

        public Task<IReadOnlyList<ClubTrainingRoster>> LoadRostersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubTrainingRoster>>([]);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;

            return Task.FromResult(0);
        }

        public Task<IDatabaseTransaction> BeginTransactionAsync(
            TransactionIsolation isolation,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The AI evaluation commits one unit of work.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 25, 2, 0, 0, TimeSpan.Zero);
    }
}
