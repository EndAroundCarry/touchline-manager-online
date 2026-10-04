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
    public void A_published_round_names_both_sides_and_holds_the_score_back()
    {
        var matchId = Guid.CreateVersion7();
        var draft = NewsTemplates.Round(9, "Vale Athletic", 2, "Northfield", 1, Guid.CreateVersion7(), matchId);

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Round 9: Vale Athletic v Northfield");
        text.Body.Should().Be("The result is in.");
        text.Spoiler.Should().Be("Vale Athletic 2\u20131 Northfield");
        text.MatchId.Should().Be(matchId, "watching the match shows the result in the feed");
    }

    [Fact]
    public void Nothing_shown_before_asking_gives_a_round_away()
    {
        var draft = NewsTemplates.Round(9, "Vale Athletic", 3, "Northfield", 0, Guid.CreateVersion7());

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);
        var shown = $"{text.Title} {text.Body}";

        shown.Should().NotContain("3").And.NotContain("0").And.NotContain("\u2013");
        text.MatchId.Should().BeNull("an item published without its match cannot be tied to one");
    }

    [Fact]
    public void An_item_stored_before_the_match_was_recorded_still_renders_and_holds_its_score_back()
    {
        const string stored =
            "{\"roundNumber\":4,\"homeClubName\":\"Vale Athletic\",\"homeGoals\":1,\"awayClubName\":\"Northfield\",\"awayGoals\":1}";

        var text = NewsMessageText.Render(NewsTemplates.ResultPublished, stored);

        text.Title.Should().Be("Round 4: Vale Athletic v Northfield");
        text.Spoiler.Should().Be("Vale Athletic 1\u20131 Northfield");
        text.MatchId.Should().BeNull();
    }

    [Fact]
    public void A_news_item_with_nothing_to_give_away_has_no_spoiler()
    {
        var draft = NewsTemplates.Transfer("Ion Popescu", 12_500_000, "Vale Athletic", "Northfield");

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Spoiler.Should().BeNull();
        text.MatchId.Should().BeNull();
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
