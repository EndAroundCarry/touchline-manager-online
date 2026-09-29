using FluentAssertions;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Domain.Tests.Finance;

/// <summary>The per-club season finance summary written at rollover (master plan §6.8).</summary>
public sealed class ClubSeasonFinanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_summary_records_its_opening_and_closing_cash()
    {
        var summary = ClubSeasonFinance.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            openingCashMinor: 10_000_000,
            closingCashMinor: 14_000_000,
            Now);

        summary.OpeningCashMinor.Should().Be(10_000_000);
        summary.ClosingCashMinor.Should().Be(14_000_000);
        summary.CreatedAt.Should().Be(Now);
        summary.Version.Should().Be(1);
    }

    [Fact]
    public void A_negative_opening_cash_is_a_programming_error()
    {
        var act = () => ClubSeasonFinance.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            openingCashMinor: -1,
            closingCashMinor: 0,
            Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_line_records_a_signed_category_total()
    {
        var summaryId = Guid.CreateVersion7();

        var line = ClubSeasonFinanceLine.Record(
            Guid.CreateVersion7(),
            summaryId,
            LedgerCategory.Wages,
            cashDeltaMinor: -1_200_000,
            Now);

        line.ClubSeasonFinanceId.Should().Be(summaryId);
        line.Category.Should().Be(LedgerCategory.Wages);
        line.CashDeltaMinor.Should().Be(-1_200_000);
    }
}
