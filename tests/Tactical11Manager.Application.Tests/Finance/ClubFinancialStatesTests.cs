using System.Text.Json;
using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Auth;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using static Tactical11Manager.Application.Tests.Finance.FinanceDoubles;

namespace Tactical11Manager.Application.Tests.Finance;

/// <summary>
/// What reading a club's money does: who is told, what the board does, and when a manager is dismissed (`DEBT-2`…`DEBT-5`).
/// </summary>
public sealed class ClubFinancialStatesTests
{
    private const long Limit = 1_000_000;

    [Fact]
    public async Task A_healthy_club_is_left_alone()
    {
        var clubId = Guid.CreateVersion7();
        var run = Arrange(clubId, Funded(clubId, 5_000_000, Limit));

        var result = await run.Subject.ApplyAsync([clubId], 10, "test", Now, CancellationToken.None);

        result.Should().Be(new FinancialStatesResult(1, 0, 0, 0));
        run.Inbox.Messages.Should().BeEmpty();
        run.Audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_club_past_its_limit_enters_administration_and_the_manager_is_told_how_long_there_is()
    {
        var clubId = Guid.CreateVersion7();
        var account = InTheRed(clubId, owed: 1_500_000);
        var run = Arrange(clubId, account);

        var result = await run.Subject.ApplyAsync([clubId], 20, "test", Now, CancellationToken.None);

        result.Changed.Should().Be(1);
        account.FinancialState.Should().Be(FinancialStateKind.Administration);
        account.StateSinceMatchday.Should().Be(20);

        var message = run.Inbox.Messages.Should().ContainSingle().Which;

        message.TemplateKey.Should().Be(FinanceInboxTemplates.AdministrationEntered);
        InboxMessageText.Render(message.TemplateKey, message.ParametersJson).Body.Should().Contain("34 matchdays");
    }

    [Fact]
    public async Task A_club_that_is_back_inside_its_limit_leaves_administration_and_stays_in_the_red()
    {
        var clubId = Guid.CreateVersion7();
        var account = InTheRed(clubId, owed: 1_500_000);

        account.RecordState(ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 20, account.CashMinor, Limit), Now);
        account.Post(LedgerPostings.OpeningBalance(Guid.CreateVersion7(), clubId, 800_000), Now);

        var run = Arrange(clubId, account);

        await run.Subject.ApplyAsync([clubId], 25, "test", Now, CancellationToken.None);

        account.FinancialState.Should().Be(FinancialStateKind.InTheRed);
        account.CashMinor.Should().Be(-700_000);
        run.Inbox.Messages.Should().ContainSingle().Which.TemplateKey.Should().Be(FinanceInboxTemplates.AdministrationLeft);
    }

    [Fact]
    public async Task The_board_lists_the_highest_earners_with_the_flag_and_the_asking_price_of_eighty_percent()
    {
        var clubId = Guid.CreateVersion7();
        var account = InTheRed(clubId, owed: 1_500_000);
        var run = Arrange(clubId, account);
        var earner = Guid.CreateVersion7();

        run.Queries.Basis.Add(new ClubFinanceBasis(
            clubId, 1, new StadiumSeats(1_000, 0, 0, 0), 1_000, 5_000, 1, 1, 0, SeasonWageMinor: 100_000_000_000, ContractedPlayers: 22));

        run.Queries.Squad.Add(new AdministrationPlayer(earner, Tactical11Manager.Domain.Squad.PositionFamily.Attack, 90_000_000_000, 200_000_000, false));

        foreach (var index in Enumerable.Range(0, 21))
        {
            run.Queries.Squad.Add(new AdministrationPlayer(
                Guid.CreateVersion7(),
                index == 0 ? Tactical11Manager.Domain.Squad.PositionFamily.Goalkeeper : Tactical11Manager.Domain.Squad.PositionFamily.Midfield,
                1_000_000,
                1_000_000,
                false));
        }

        var result = await run.Subject.ApplyAsync([clubId], 20, "test", Now, CancellationToken.None);

        result.Listed.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(EconomyRuleSet.AdministrationMaxListings);

        var first = run.Listings.Calls.First();

        first.ListedByBoard.Should().BeTrue();
        first.PlayerId.Should().Be(earner, "the highest earner goes first");
        first.MinimumFeeMinor.Should().Be(200_000_000L * 8_000 / 10_000);
        first.Key.Should().NotBeNull().And.StartWith("board:");

        run.Inbox.Messages.Should().Contain(message => message.TemplateKey == FinanceInboxTemplates.BoardListedPlayers);
    }

    [Fact]
    public async Task A_manager_whose_club_stays_in_administration_for_a_whole_season_is_dismissed()
    {
        var clubId = Guid.CreateVersion7();
        var account = InTheRed(clubId, owed: 1_500_000);
        var club = Club.Generate(
            clubId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new ClubIdentity("Northgate United", "NOR", "Northgate", "North", "badge-seed"),
            1,
            1950,
            Now);
        var user = User.Register(Guid.CreateVersion7(), "boss@example.com", "Boss", "hash", "stamp", Now);
        var manager = Manager.Create(Guid.CreateVersion7(), user.Id, "en", "UTC", Now);
        var tenure = ClubTenure.Start(Guid.CreateVersion7(), clubId, manager.Id, "takeover-key", Now);

        var tenures = new StubTenureRepository(new OpenTenureRow(tenure, club, manager, user));
        var outbox = new RecordingOutboxWriter();
        var metrics = new RecordingMetrics();
        var run = Arrange(clubId, account, human: true, tenures, outbox, metrics);

        // In administration since matchday 3, and now it is matchday 37.
        account.RecordState(ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 3, account.CashMinor, Limit), Now);

        var early = await run.Subject.ApplyAsync([clubId], 36, "test", Now, CancellationToken.None);

        early.Dismissed.Should().Be(0, "33 matchdays have passed, one short of a season");

        var result = await run.Subject.ApplyAsync([clubId], 37, "test", Now, CancellationToken.None);

        result.Dismissed.Should().Be(1);
        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Closed);
        tenure.EndReason.Should().Be(ClubTenureEndReasons.Dismissed);
        manager.TakeoverCooldownUntil.Should().Be(Now.Add(WorldRuleSet.ResignationCooldown), "the usual cooldown applies");
        metrics.Closed.Should().ContainSingle().Which.Should().Be(ClubTenureEndReasons.Dismissed);

        var draft = outbox.Drafts.Should().ContainSingle().Which;

        JsonSerializer.Serialize(draft).Should().Contain("boss@example.com");
        run.Inbox.Messages.Should().Contain(message => message.TemplateKey == FinanceInboxTemplates.ManagerDismissed);
        run.Audit.Entries.Should().Contain(entry => entry.Action == FinanceAuditActions.ManagerDismissed);
    }

    [Fact]
    public async Task A_computer_club_in_administration_for_a_whole_season_just_keeps_selling()
    {
        var clubId = Guid.CreateVersion7();
        var account = InTheRed(clubId, owed: 1_500_000);
        var metrics = new RecordingMetrics();
        var run = Arrange(clubId, account, human: false, metrics: metrics);

        account.RecordState(ClubFinancialState.Next(FinancialStateKind.Healthy, 0, 3, account.CashMinor, Limit), Now);

        var result = await run.Subject.ApplyAsync([clubId], 60, "test", Now, CancellationToken.None);

        result.Dismissed.Should().Be(0, "no manager holds the club");
        metrics.Closed.Should().BeEmpty();
        account.FinancialState.Should().Be(FinancialStateKind.Administration);
    }

    private static ClubAccount InTheRed(Guid clubId, long owed)
    {
        var account = Funded(clubId, 0, Limit);

        account.Post(LedgerPostings.OperatingCost(Guid.CreateVersion7(), clubId, new DateOnly(2026, 9, 1), owed), Now);

        return account;
    }

    private static Run Arrange(
        Guid clubId,
        ClubAccount account,
        bool human = true,
        StubTenureRepository? tenures = null,
        RecordingOutboxWriter? outbox = null,
        RecordingMetrics? metrics = null)
    {
        var queries = new StubFinanceQueries();
        var accounts = new RecordingAccountRepository(account);
        var inbox = new RecordingInboxRepository();
        var audit = new RecordingAuditWriter();
        var listings = new RecordingListingWriter();

        inbox.Targets.Add(new ClubInboxTarget(clubId, human ? Guid.CreateVersion7() : null, "Test FC"));

        return new Run(States(accounts, queries, inbox, listings, audit, tenures, outbox, metrics), queries, inbox, audit, listings);
    }

    private sealed record Run(
        ClubFinancialStates Subject,
        StubFinanceQueries Queries,
        RecordingInboxRepository Inbox,
        RecordingAuditWriter Audit,
        RecordingListingWriter Listings);
}
