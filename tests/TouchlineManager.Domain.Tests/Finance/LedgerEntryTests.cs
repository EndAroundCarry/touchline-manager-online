using FluentAssertions;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Domain.Tests.Finance;

/// <summary>
/// The ledger entry as a value: the balances it is allowed to claim, and the stable codes that describe what
/// it is and what wrote it (`FIN-11`, `FIN-13`).
/// </summary>
public sealed class LedgerEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    private static LedgerEntry Record(
        long cashDelta = 100,
        long reservedDelta = 0,
        long resultingCash = 100,
        long resultingReserved = 0,
        long sequence = 1,
        string correlationId = "op-1",
        string template = "test.ledger",
        string parameters = "{}") =>
        LedgerEntry.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence,
            LedgerCategory.OpeningBalance,
            cashDelta,
            reservedDelta,
            resultingCash,
            resultingReserved,
            LedgerSourceType.WorldSeed,
            null,
            correlationId,
            template,
            parameters,
            Now);

    [Fact]
    public void An_entry_carries_the_move_and_the_balances_it_produced()
    {
        var entry = Record(cashDelta: 250, resultingCash: 1_250);

        entry.CashDeltaMinor.Should().Be(250);
        entry.ResultingCashMinor.Should().Be(1_250);
        entry.ResultingReservedMinor.Should().Be(0);
        entry.Sequence.Should().Be(1);
        entry.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void An_entry_refuses_a_negative_resulting_balance()
    {
        var act = () => Record(cashDelta: -100, resultingCash: -100);

        act.Should().Throw<ArgumentOutOfRangeException>("a balance is never negative (FIN-13)");
    }

    [Fact]
    public void An_entry_refuses_reserved_funds_beyond_the_cash_behind_them()
    {
        var act = () => Record(
            cashDelta: 0,
            reservedDelta: 500,
            resultingCash: 400,
            resultingReserved: 500);

        act.Should().Throw<ArgumentOutOfRangeException>("reserved funds never exceed cash (FIN-10, FIN-13)");
    }

    [Fact]
    public void An_entry_refuses_a_sequence_that_does_not_start_at_one()
    {
        var act = () => Record(sequence: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_entry_refuses_a_blank_correlation_key_or_description()
    {
        var blankCorrelation = () => Record(correlationId: " ");

        blankCorrelation.Should().Throw<ArgumentException>("every entry is part of a correlatable operation (FIN-17)");

        var blankTemplate = () => Record(template: " ");

        blankTemplate.Should().Throw<ArgumentException>("an entry says why it happened");

        var blankParameters = () => Record(parameters: " ");

        blankParameters.Should().Throw<ArgumentException>("the description carries its stored document");
    }

    [Fact]
    public void An_entry_refuses_a_correlation_key_or_template_longer_than_its_column()
    {
        var longCorrelation = () => Record(correlationId: new string('c', LedgerEntry.MaxCorrelationIdLength + 1));

        longCorrelation.Should().Throw<ArgumentException>();

        var longTemplate = () => Record(template: new string('t', LedgerEntry.MaxDescriptionTemplateLength + 1));

        longTemplate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_category_code_round_trips_and_fits_its_column()
    {
        LedgerCategories.All.Should().OnlyHaveUniqueItems();

        foreach (var category in LedgerCategories.All)
        {
            var code = category.ToCode();

            code.Length.Should().BeLessThanOrEqualTo(LedgerCategories.MaxCodeLength, "a column holds every code");
            LedgerCategories.FromCode(code).Should().Be(category);
        }
    }

    [Fact]
    public void Every_source_code_round_trips_and_fits_its_column()
    {
        LedgerSourceTypes.All.Should().OnlyHaveUniqueItems();

        foreach (var sourceType in LedgerSourceTypes.All)
        {
            var code = sourceType.ToCode();

            code.Length.Should().BeLessThanOrEqualTo(LedgerSourceTypes.MaxCodeLength, "a column holds every code");
            LedgerSourceTypes.FromCode(code).Should().Be(sourceType);
        }
    }

    [Fact]
    public void A_compensating_entry_names_the_entry_it_corrects()
    {
        var corrected = Guid.CreateVersion7();

        var entry = Compensating(corrected);

        entry.Category.Should().Be(LedgerCategory.Compensation);
        entry.ReversesEntryId.Should().Be(corrected, "the ledger states which line it corrects (FIN-12)");
    }

    [Fact]
    public void Only_a_compensating_entry_may_name_the_entry_it_corrects()
    {
        var act = () => LedgerEntry.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 1,
            LedgerCategory.OpeningBalance,
            cashDeltaMinor: 1,
            reservedDeltaMinor: 0,
            resultingCashMinor: 1,
            resultingReservedMinor: 0,
            LedgerSourceType.WorldSeed,
            sourceId: null,
            correlationId: "op-1",
            descriptionTemplate: "test.ledger",
            descriptionParametersJson: "{}",
            Now,
            reversesEntryId: Guid.CreateVersion7());

        act.Should().Throw<ArgumentException>("only a compensating entry reverses another (FIN-12)");
    }

    [Fact]
    public void An_entry_cannot_reverse_itself_or_name_an_empty_entry()
    {
        var id = Guid.CreateVersion7();

        var self = () => LedgerEntry.Record(
            id,
            Guid.CreateVersion7(),
            sequence: 1,
            LedgerCategory.Compensation,
            cashDeltaMinor: 1,
            reservedDeltaMinor: 0,
            resultingCashMinor: 1,
            resultingReservedMinor: 0,
            LedgerSourceType.AdminRepair,
            sourceId: null,
            correlationId: "repair-1",
            descriptionTemplate: "finance.compensation",
            descriptionParametersJson: "{}",
            Now,
            reversesEntryId: id);

        self.Should().Throw<ArgumentException>();

        var empty = () => Compensating(Guid.Empty);

        empty.Should().Throw<ArgumentException>();
    }

    private static LedgerEntry Compensating(Guid reversesEntryId) =>
        LedgerEntry.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sequence: 2,
            LedgerCategory.Compensation,
            cashDeltaMinor: -100,
            reservedDeltaMinor: 0,
            resultingCashMinor: 0,
            resultingReservedMinor: 0,
            LedgerSourceType.AdminRepair,
            sourceId: null,
            correlationId: "repair-1",
            descriptionTemplate: "finance.compensation",
            descriptionParametersJson: "{}",
            Now,
            reversesEntryId);
}
