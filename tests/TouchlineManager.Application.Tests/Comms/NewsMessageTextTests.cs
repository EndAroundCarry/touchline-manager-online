using FluentAssertions;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Comms;

namespace TouchlineManager.Application.Tests.Comms;

/// <summary>
/// The English a stored news item renders to, and the cursor that pages it (`COM-1`, master plan §8.6).
/// </summary>
public sealed class NewsMessageTextTests
{
    [Fact]
    public void A_provisioned_division_names_the_country_and_the_tier()
    {
        var draft = NewsTemplates.DivisionActivated(
            countryName: "England",
            tierNumber: 2,
            divisionName: "England Division 2",
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("England Division 2 is born");
        text.Body.Should().Contain("England").And.Contain("tier 2").And.Contain("England Division 2");
    }

    [Fact]
    public void A_completed_transfer_names_the_money_and_both_clubs()
    {
        var draft = NewsTemplates.Transfer("Ion Popescu", 12_500_000, "Vale Athletic", "Northfield");

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Ion Popescu moves");
        text.Body.Should().Contain("Vale Athletic").And.Contain("Northfield").And.Contain("12,500,000");
    }

    [Fact]
    public void A_published_round_names_both_sides_and_the_score()
    {
        var draft = NewsTemplates.Round(9, "Vale Athletic", 2, "Northfield", 1, Guid.CreateVersion7());

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Round 9: Vale Athletic 2\u20131 Northfield");
        text.Body.Should().Be("Vale Athletic 2, Northfield 1.");
    }

    [Fact]
    public void An_operator_announcement_renders_the_title_and_body_it_was_given()
    {
        var draft = NewsTemplates.Announcement(
            "Scheduled maintenance",
            "The game will be read-only tonight from 22:00 to 22:30 UTC.",
            countryId: null,
            divisionId: null,
            expiresAt: null);

        draft.Category.Should().Be(TouchlineManager.Domain.Comms.NewsCategory.Announcement);

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Scheduled maintenance");
        text.Body.Should().Be("The game will be read-only tonight from 22:00 to 22:30 UTC.");
    }

    [Fact]
    public void An_unknown_template_is_refused_rather_than_rendered_vaguely()
    {
        var render = () => NewsMessageText.Render("news.future.unknown", "{}");

        render.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_cursor_round_trips_and_refuses_a_value_it_did_not_produce()
    {
        var position = new NewsCursorPosition(
            new DateTimeOffset(2026, 10, 6, 19, 0, 0, TimeSpan.Zero),
            Guid.CreateVersion7());

        var cursor = NewsCursor.Encode(position);

        NewsCursor.TryDecode(cursor, out var decoded).Should().BeTrue();
        decoded.Should().Be(position);

        NewsCursor.TryDecode(null, out var first).Should().BeTrue();
        first.Should().BeNull("no cursor is the first page, not an error");

        NewsCursor.TryDecode("not-a-cursor", out _).Should().BeFalse();
    }
}
