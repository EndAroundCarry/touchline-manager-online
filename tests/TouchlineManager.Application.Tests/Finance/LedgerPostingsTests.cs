using System.Text.Json;
using FluentAssertions;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Tests.Finance;

/// <summary>
/// The postings the game builds, so the opening balance carries its reason and its correlation key rather
/// than a bare amount (`FIN-1`, `FIN-17`).
/// </summary>
public sealed class LedgerPostingsTests
{
    [Fact]
    public void The_opening_posting_funds_the_cash_and_correlates_on_the_club()
    {
        var clubId = Guid.CreateVersion7();
        var entryId = Guid.CreateVersion7();

        var posting = LedgerPostings.OpeningBalance(entryId, clubId, 1_000);

        posting.EntryId.Should().Be(entryId);
        posting.Category.Should().Be(LedgerCategory.OpeningBalance);
        posting.CashDeltaMinor.Should().Be(1_000);
        posting.ReservedDeltaMinor.Should().Be(0, "an opening balance reserves nothing");
        posting.SourceType.Should().Be(LedgerSourceType.WorldSeed);
        posting.SourceId.Should().Be(clubId);
        posting.CorrelationId.Should().Be(
            clubId.ToString("D"),
            "a club is funded once, so its own identity is the key a retry collides on (FIN-17)");
    }

    [Fact]
    public void The_description_carries_the_amount_it_funded()
    {
        var posting = LedgerPostings.OpeningBalance(Guid.CreateVersion7(), Guid.CreateVersion7(), 2_500);

        posting.DescriptionTemplate.Should().Be(LedgerPostings.OpeningBalanceTemplate);

        using var document = JsonDocument.Parse(posting.DescriptionParametersJson);

        document.RootElement.GetProperty("amountMinor").GetInt64().Should().Be(2_500);
    }

    [Fact]
    public void A_negative_opening_balance_is_refused()
    {
        var act = () => LedgerPostings.OpeningBalance(Guid.CreateVersion7(), Guid.CreateVersion7(), -1);

        act.Should().Throw<ArgumentOutOfRangeException>("a club is never funded with a debt (FIN-14)");
    }
}
