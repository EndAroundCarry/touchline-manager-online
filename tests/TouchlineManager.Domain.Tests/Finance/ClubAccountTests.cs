using FluentAssertions;
using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Domain.Tests.Finance;

/// <summary>
/// A club's account and the postings that move it: the only path a balance changes, and the rules that
/// refuse a move the club cannot afford (`FIN-10`, `FIN-11`, `FIN-13`).
/// </summary>
public sealed class ClubAccountTests
{
    private const string Template = "test.ledger";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    private static ClubAccount Account() => ClubAccount.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    private static LedgerPosting Posting(
        LedgerCategory category,
        long cashDelta,
        long reservedDelta,
        string correlationId = "op-1") =>
        new(
            Guid.CreateVersion7(),
            category,
            cashDelta,
            reservedDelta,
            LedgerSourceType.WorldSeed,
            SourceId: null,
            correlationId,
            Template,
            "{}");

    [Fact]
    public void A_new_account_holds_nothing()
    {
        var account = Account();

        account.CashMinor.Should().Be(0, "the balance arrives as a ledger entry, not as a starting value (FIN-18)");
        account.ReservedMinor.Should().Be(0);
        account.AvailableMinor.Should().Be(0);
        account.LastLedgerSequence.Should().Be(0);
        account.Version.Should().Be(1);
    }

    [Fact]
    public void An_opening_posting_funds_the_account_as_its_first_entry()
    {
        var account = Account();

        var entry = account.Post(Posting(LedgerCategory.OpeningBalance, cashDelta: 500, reservedDelta: 0), Now);

        entry.Sequence.Should().Be(1, "a ledger's sequence starts at one (FIN-11)");
        entry.ResultingCashMinor.Should().Be(500);
        entry.ResultingReservedMinor.Should().Be(0);
        entry.ClubId.Should().Be(account.ClubId);

        account.CashMinor.Should().Be(500);
        account.LastLedgerSequence.Should().Be(1);
        account.Version.Should().Be(2, "a posting advances the aggregate's version");
    }

    [Fact]
    public void A_posting_states_the_balances_it_produced_and_its_reason()
    {
        var account = Account();

        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000, 0), Now);

        var entry = account.Post(Posting(LedgerCategory.Wages, cashDelta: -250, reservedDelta: 0), Now.AddHours(1));

        entry.Sequence.Should().Be(2);
        entry.Category.Should().Be(LedgerCategory.Wages);
        entry.CashDeltaMinor.Should().Be(-250);
        entry.ReservedDeltaMinor.Should().Be(0);
        entry.ResultingCashMinor.Should().Be(750, "the entry records what the club held, not only what moved");
        entry.ResultingReservedMinor.Should().Be(0);
        entry.CorrelationId.Should().Be("op-1");
        entry.DescriptionTemplate.Should().Be(Template);
        entry.CreatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void A_reservation_holds_funds_without_spending_them()
    {
        var account = Account();
        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000, 0), Now);

        var entry = account.Post(Posting(LedgerCategory.BidReservation, cashDelta: 0, reservedDelta: 300), Now);

        account.CashMinor.Should().Be(1_000, "a reservation commits money without paying it (TRF-7)");
        account.ReservedMinor.Should().Be(300);
        account.AvailableMinor.Should().Be(700, "affordability is about what is left, not the raw balance (FIN-10)");
        entry.ReservedDeltaMinor.Should().Be(300);
    }

    [Fact]
    public void A_reservation_is_refused_when_it_exceeds_the_available_cash()
    {
        var account = Account();
        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000, 0), Now);
        account.Post(Posting(LedgerCategory.BidReservation, 0, 800), Now);

        var act = () => account.Post(Posting(LedgerCategory.BidReservation, 0, 300), Now);

        act.Should().Throw<InvalidOperationException>("a club cannot reserve money it does not have (FIN-10)");
        account.ReservedMinor.Should().Be(800, "a refused posting changes nothing");
        account.LastLedgerSequence.Should().Be(2);
    }

    [Fact]
    public void A_posting_is_refused_when_the_cash_would_go_negative()
    {
        var account = Account();
        account.Post(Posting(LedgerCategory.OpeningBalance, 100, 0), Now);

        var act = () => account.Post(Posting(LedgerCategory.Wages, cashDelta: -200, reservedDelta: 0), Now);

        act.Should().Throw<InvalidOperationException>("cash is never negative (FIN-13)");
        account.CashMinor.Should().Be(100);
        account.LastLedgerSequence.Should().Be(1);
    }

    [Fact]
    public void A_debit_is_refused_when_it_would_spend_money_already_reserved()
    {
        var account = Account();
        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000, 0), Now);
        account.Post(Posting(LedgerCategory.BidReservation, 0, 800), Now);

        var act = () => account.Post(Posting(LedgerCategory.Wages, cashDelta: -500, reservedDelta: 0), Now);

        act.Should().Throw<InvalidOperationException>(
            "reserved funds may never exceed the cash behind them, so only 200 is available (FIN-10, FIN-13)");
        account.CashMinor.Should().Be(1_000);
    }

    [Fact]
    public void A_release_is_refused_when_it_would_make_reserved_funds_negative()
    {
        var account = Account();
        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000, 0), Now);
        account.Post(Posting(LedgerCategory.BidReservation, 0, 300), Now);

        var act = () => account.Post(Posting(LedgerCategory.ReservationRelease, 0, -400), Now);

        act.Should().Throw<InvalidOperationException>("reserved funds are never negative (FIN-13)");
        account.ReservedMinor.Should().Be(300);
    }

    [Fact]
    public void A_release_frees_exactly_what_was_held()
    {
        var account = Account();
        account.Post(Posting(LedgerCategory.OpeningBalance, 1_000, 0), Now);
        account.Post(Posting(LedgerCategory.BidReservation, 0, 300), Now);

        account.Post(Posting(LedgerCategory.ReservationRelease, 0, -300), Now);

        account.ReservedMinor.Should().Be(0);
        account.AvailableMinor.Should().Be(1_000, "an outbid club can commit the money again (TRF-7)");
    }

    [Fact]
    public void A_posting_that_moves_nothing_is_refused()
    {
        var account = Account();

        var act = () => account.Post(Posting(LedgerCategory.Compensation, 0, 0), Now);

        act.Should().Throw<InvalidOperationException>("a ledger entry exists because something moved (FIN-11)");
        account.LastLedgerSequence.Should().Be(0);
    }
}
