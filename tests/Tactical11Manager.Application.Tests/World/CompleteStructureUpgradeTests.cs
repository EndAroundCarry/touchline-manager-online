using FluentAssertions;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Application.World;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.World;

/// <summary>
/// What finishing an upgrade does: the level rises once, the manager is told once, and a stale or repeated
/// job changes nothing (`STRC-3`).
/// </summary>
public sealed class CompleteStructureUpgradeTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Finished = Started.AddDays(1);

    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ManagerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task The_level_rises_and_the_manager_is_told_in_the_same_save()
    {
        var run = Arrange(StructureKind.SnackBar, managerId: ManagerId);

        var outcome = await run.UseCase.ExecuteAsync(run.Building.Id, 2, CancellationToken.None);

        outcome.Should().Be(CompleteStructureUpgradeOutcome.Completed);
        run.Building.Level.Should().Be(2);
        run.Building.IsBuilding.Should().BeFalse();
        run.Unit.Saves.Should().Be(1);

        var message = run.Inbox.Added.Should().ContainSingle().Which;

        message.RecipientManagerId.Should().Be(ManagerId);
        message.Category.Should().Be(InboxCategory.System);
        message.TemplateKey.Should().Be(StructureInboxTemplates.UpgradeCompleted);
        message.RelatedEntityId.Should().Be(run.Building.Id);

        var text = InboxMessageText.Render(message.TemplateKey, message.ParametersJson);

        text.Title.Should().Be("Your snack bar is now level 2");
        text.Spoiler.Should().BeNull();
    }

    [Fact]
    public async Task The_shop_is_named_as_the_merchandise_shop()
    {
        var run = Arrange(StructureKind.Merchandise, managerId: ManagerId);

        await run.UseCase.ExecuteAsync(run.Building.Id, 2, CancellationToken.None);

        var message = run.Inbox.Added.Should().ContainSingle().Which;

        InboxMessageText.Render(message.TemplateKey, message.ParametersJson).Title
            .Should().Be("Your merchandise shop is now level 2");
    }

    [Fact]
    public async Task A_second_delivery_of_the_job_changes_and_says_nothing()
    {
        var run = Arrange(StructureKind.SnackBar, managerId: ManagerId);

        await run.UseCase.ExecuteAsync(run.Building.Id, 2, CancellationToken.None);

        var again = await run.UseCase.ExecuteAsync(run.Building.Id, 2, CancellationToken.None);

        again.Should().Be(CompleteStructureUpgradeOutcome.NothingToComplete);
        run.Building.Level.Should().Be(2, "the level rose once");
        run.Inbox.Added.Should().ContainSingle("the manager was told once");
        run.Unit.Saves.Should().Be(1);
    }

    [Fact]
    public async Task A_job_for_another_level_than_the_one_being_built_does_nothing()
    {
        var run = Arrange(StructureKind.SnackBar, managerId: ManagerId);

        var outcome = await run.UseCase.ExecuteAsync(run.Building.Id, 3, CancellationToken.None);

        outcome.Should().Be(CompleteStructureUpgradeOutcome.NothingToComplete);
        run.Building.Level.Should().Be(1);
        run.Building.IsBuilding.Should().BeTrue("the real work is still running");
        run.Inbox.Added.Should().BeEmpty();
        run.Unit.Saves.Should().Be(0);
    }

    [Fact]
    public async Task A_building_that_does_not_exist_is_reported_and_not_retried_into_a_crash()
    {
        var run = Arrange(StructureKind.SnackBar, managerId: ManagerId);

        var outcome = await run.UseCase.ExecuteAsync(Guid.CreateVersion7(), 2, CancellationToken.None);

        outcome.Should().Be(CompleteStructureUpgradeOutcome.NotFound);
        run.Unit.Saves.Should().Be(0);
    }

    [Fact]
    public async Task A_club_nobody_manages_gets_its_level_but_no_message()
    {
        var run = Arrange(StructureKind.SnackBar, managerId: null);

        var outcome = await run.UseCase.ExecuteAsync(run.Building.Id, 2, CancellationToken.None);

        outcome.Should().Be(CompleteStructureUpgradeOutcome.Completed);
        run.Building.Level.Should().Be(2);
        run.Inbox.Added.Should().BeEmpty("an AI club has nobody to tell");
    }

    [Fact]
    public void The_job_payload_names_the_building_and_the_level_and_reads_back()
    {
        var id = Guid.CreateVersion7();

        var payload = StructureJobPayload.For(id, 4);

        StructureJobPayload.TryRead(payload, out var readId, out var readLevel).Should().BeTrue();
        readId.Should().Be(id);
        readLevel.Should().Be(4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"structureId":"not-a-guid","targetLevel":2}""")]
    [InlineData("""{"structureId":"11111111-1111-1111-1111-111111111111"}""")]
    public void A_payload_that_does_not_name_a_building_and_a_level_is_not_read(string payload)
    {
        StructureJobPayload.TryRead(payload, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void The_job_key_names_the_building_and_the_level_so_each_upgrade_is_its_own_job()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");

        StructureJobTypes.CompleteUpgradeKey(id, 2).Should().Be("structure:33333333-3333-3333-3333-333333333333:level:2");
        StructureJobTypes.CompleteUpgradeKey(id, 3).Should().NotBe(StructureJobTypes.CompleteUpgradeKey(id, 2));
    }

    private static Run Arrange(StructureKind kind, Guid? managerId)
    {
        var building = ClubStructure.Open(Guid.CreateVersion7(), ClubId, kind, Started.AddDays(-30));

        building.StartUpgrade(Finished, Started);

        var inbox = new FakeInbox(managerId);
        var unit = new CountingUnitOfWork();

        return new Run(
            building,
            inbox,
            unit,
            new CompleteStructureUpgrade(new FakeStructures(building), inbox, new FixedClock(), unit));
    }

    private sealed record Run(
        ClubStructure Building,
        FakeInbox Inbox,
        CountingUnitOfWork Unit,
        CompleteStructureUpgrade UseCase);

    private sealed class FakeStructures : IStructureRepository
    {
        private readonly ClubStructure _building;

        public FakeStructures(ClubStructure building) => _building = building;

        public void Add(ClubStructure building)
        {
        }

        public void AddTakings(ClubStructureTakings takings)
        {
        }

        public Task<ClubStructure?> FindAsync(Guid buildingId, CancellationToken cancellationToken) =>
            Task.FromResult(buildingId == _building.Id ? _building : null);

        public Task<ClubStructure?> FindByClubAsync(Guid clubId, StructureKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClubStructure>> ListByClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeInbox : IInboxRepository
    {
        private readonly Guid? _managerId;

        public FakeInbox(Guid? managerId) => _managerId = managerId;

        public List<InboxMessage> Added { get; } = [];

        public void Add(InboxMessage message) => Added.Add(message);

        public Task<InboxMessage?> FindAsync(Guid managerId, Guid messageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<InboxMessage>> LoadUnreadAsync(Guid managerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClubInboxTarget>> FindClubTargetsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClubInboxTarget>>(
                [.. clubIds.Where(id => id == ClubId).Select(id => new ClubInboxTarget(id, _managerId, "Test FC"))]);

        public Task<IReadOnlyDictionary<Guid, string>> FindPlayerNamesAsync(
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, string>> FindManagerEmailsAsync(
            IReadOnlyCollection<Guid> managerIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
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
            throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = Finished;
    }
}
