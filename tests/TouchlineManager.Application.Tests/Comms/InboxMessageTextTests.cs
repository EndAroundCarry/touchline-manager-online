using FluentAssertions;
using TouchlineManager.Application.Comms;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Tests.Comms;

/// <summary>
/// The English a stored inbox message renders to, and the cursor that pages it (`F-41`, master plan §8.6).
/// </summary>
public sealed class InboxMessageTextTests
{
    private static readonly Guid MatchId = Guid.CreateVersion7();

    [Fact]
    public void A_home_win_reads_as_a_win_with_the_new_position()
    {
        var draft = InboxTemplates.Result(
            roundNumber: 5,
            opponentName: "Vale Athletic",
            isHome: true,
            goalsFor: 2,
            goalsAgainst: 1,
            outcome: InboxTemplates.WinOutcome,
            position: 3,
            MatchId);

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Round 5: won 2\u20131");
        text.Body.Should().Be("At home to Vale Athletic. You are 3rd in the table after round 5.");
    }

    [Fact]
    public void An_away_defeat_reads_as_a_defeat_and_a_cameo_position_is_ordinal()
    {
        var draft = InboxTemplates.Result(
            roundNumber: 11,
            opponentName: "Northfield",
            isHome: false,
            goalsFor: 0,
            goalsAgainst: 2,
            outcome: InboxTemplates.LossOutcome,
            position: 12,
            MatchId);

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Round 11: lost 0\u20132");
        text.Body.Should().Be("Away to Northfield. You are 12th in the table after round 11.");
    }

    [Fact]
    public void A_table_move_names_both_positions()
    {
        var rising = InboxTemplates.Table(roundNumber: 6, position: 4, previousPosition: 7);
        var falling = InboxTemplates.Table(roundNumber: 6, position: 9, previousPosition: 8);

        InboxMessageText.Render(rising.TemplateKey, rising.ParametersJson).Body
            .Should().Be("After round 6 you moved up to 4th from 7th.");

        InboxMessageText.Render(falling.TemplateKey, falling.ParametersJson).Body
            .Should().Be("After round 6 you moved down to 9th from 8th.");
    }

    [Fact]
    public void A_suspension_names_every_reason_it_fired_for()
    {
        var both = InboxTemplates.Suspension(
            "Ion Popescu",
            [InboxTemplates.BookingsReason, InboxTemplates.RedCardReason],
            2,
            Guid.CreateVersion7());

        var bookings = InboxTemplates.Suspension(
            "Dan Marin",
            [InboxTemplates.BookingsReason],
            1,
            Guid.CreateVersion7());

        InboxMessageText.Render(both.TemplateKey, both.ParametersJson).Body
            .Should().Be(
                "Ion Popescu misses 2 fixtures after accumulating bookings and a sending-off.");

        InboxMessageText.Render(bookings.TemplateKey, bookings.ParametersJson).Body
            .Should().Be("Dan Marin misses 1 fixture after accumulating bookings.");
    }

    [Fact]
    public void An_injury_names_its_band_and_length()
    {
        var draft = InboxTemplates.Injury("Ana Ionescu", 3, InjurySeverity.Moderate, Guid.CreateVersion7());

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Ana Ionescu is injured");
        text.Body.Should().Be("Ana Ionescu is out for 3 fixtures with a moderate injury.");
    }

    [Fact]
    public void A_retirement_announcement_marks_the_final_season()
    {
        var draft = InboxTemplates.Retirement("Corin Alderwick", Guid.CreateVersion7());

        draft.TemplateKey.Should().Be(InboxTemplates.RetirementAnnounced);

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Corin Alderwick will retire");
        text.Body.Should().Be("Corin Alderwick has announced that this will be their final season.");
    }

    [Fact]
    public void A_season_movement_names_the_destination_and_the_direction()
    {
        var promoted = InboxTemplates.Movement(
            "Vale Athletic",
            "England Division 2",
            "England Division 1",
            promoted: true);
        var relegated = InboxTemplates.Movement(
            "Bramford Rovers",
            "England Division 1",
            "England Division 2",
            promoted: false);

        promoted.TemplateKey.Should().Be(InboxTemplates.SeasonMovement);

        var promotedText = InboxMessageText.Render(promoted.TemplateKey, promoted.ParametersJson);

        promotedText.Title.Should().Be("Promoted to England Division 1");
        promotedText.Body.Should().Contain("Vale Athletic").And.Contain("England Division 1");

        var relegatedText = InboxMessageText.Render(relegated.TemplateKey, relegated.ParametersJson);

        relegatedText.Title.Should().Be("Relegated to England Division 2");
        relegatedText.Body.Should().Contain("Bramford Rovers").And.Contain("England Division 2");
    }

    [Fact]
    public void A_repaired_side_lists_the_decisions_and_the_replacements()
    {
        var draft = InboxTemplates.Repair(
            roundNumber: 2,
            [
                new InboxTemplates.RepairParameter(
                    7,
                    SnapshotRepairReason.PlayerUnavailable.ToCode(),
                    "Dan Marin"),
                new InboxTemplates.RepairParameter(
                    12,
                    SnapshotRepairReason.SlotEmpty.ToCode(),
                    null),
            ],
            Guid.CreateVersion7());

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Your side was changed");
        text.Body.Should().Be(
            "Before round 2, these places in your side were decided for you: "
            + "slot 7: the player was unavailable, Dan Marin came in; slot 12: it was empty.");
    }

    [Fact]
    public void An_unknown_template_is_refused_rather_than_rendered_vaguely()
    {
        var render = () => InboxMessageText.Render("inbox.future.unknown", "{}");

        render.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_welcome_names_the_club_and_its_division()
    {
        var draft = InboxTemplates.Welcome("Vale Athletic", "England Division 2");

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Welcome to Vale Athletic");
        text.Body.Should().Contain("Vale Athletic").And.Contain("England Division 2");
    }

    [Fact]
    public void An_inactivity_warning_names_the_lapse_and_a_closure_names_the_return()
    {
        var warning = InboxTemplates.Warned(11);
        var closed = InboxTemplates.ClosedForInactivity(22);

        var warningText = InboxMessageText.Render(warning.TemplateKey, warning.ParametersJson);

        warningText.Title.Should().Be("We have not seen you in a while");
        warningText.Body.Should().Contain("11 days");

        var closedText = InboxMessageText.Render(closed.TemplateKey, closed.ParametersJson);

        closedText.Title.Should().Be("Your club has returned to the AI");
        closedText.Body.Should().Contain("22 days");
    }

    [Fact]
    public void A_single_day_lapse_reads_in_the_singular()
    {
        var draft = InboxTemplates.Warned(1);

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Body.Should().Contain("away for 1 day.").And.NotContain("away for 1 days.");
    }

    [Fact]
    public void A_deadline_reminder_names_the_round_the_venue_and_the_lock()
    {
        var draft = InboxTemplates.Reminder(
            roundNumber: 8,
            opponentName: "Northfield",
            isHome: true,
            lockAt: new DateTimeOffset(2026, 10, 6, 18, 30, 0, TimeSpan.Zero));

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Round 8: submit your team sheet");
        text.Body.Should().Contain("At home to Northfield").And.Contain("18:30");
    }

    [Fact]
    public void A_cursor_round_trips_and_refuses_a_value_it_did_not_produce()
    {
        var position = new Abstractions.Comms.InboxCursorPosition(
            new DateTimeOffset(2026, 10, 6, 19, 0, 0, TimeSpan.Zero),
            Guid.CreateVersion7());

        var cursor = InboxCursor.Encode(position);

        InboxCursor.TryDecode(cursor, out var decoded).Should().BeTrue();
        decoded.Should().Be(position);

        InboxCursor.TryDecode(null, out var first).Should().BeTrue();
        first.Should().BeNull("no cursor is the first page, not an error");

        InboxCursor.TryDecode("not-a-cursor", out _).Should().BeFalse();
    }
}
