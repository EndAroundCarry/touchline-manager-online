using FluentAssertions;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Competition.Playoffs;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Shootout;

namespace Tactical11Manager.Application.Tests.Comms;

/// <summary>
/// The messages only a promotion play-off has, how a round after the thirtieth is named, and the document a shoot-out is stored in (`PR-4`, `PR-6`).
/// </summary>
public sealed class PlayoffTemplatesTests
{
    private static readonly DateTimeOffset Kickoff = new(2026, 12, 15, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_semi_final_draw_names_the_place_the_opponent_and_the_kickoff()
    {
        var draft = PlayoffInboxTemplates.DrawnInto(3, PlayoffStage.SemiFinal, "Vale Athletic", isHome: true, Kickoff, Guid.CreateVersion7());

        draft.Category.Should().Be(InboxCategory.Table);

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Play-off semi-final: you finished 3rd");
        text.Body.Should().Be("You host Vale Athletic. Kickoff is Tue 15 Dec 2026 19:00 UTC.");
        text.Spoiler.Should().BeNull("a draw is not a result");
    }

    [Fact]
    public void A_visiting_semi_finalist_is_told_it_travels()
    {
        var draft = PlayoffInboxTemplates.DrawnInto(4, PlayoffStage.SemiFinal, "Northfield", isHome: false, Kickoff, Guid.CreateVersion7());

        InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson).Body.Should().StartWith("You visit Northfield.");
    }

    [Fact]
    public void The_holder_waits_for_a_winner_it_cannot_name_yet()
    {
        var draft = PlayoffInboxTemplates.DrawnInto(14, PlayoffStage.Final, opponentName: null, isHome: true, Kickoff, fixtureId: null);

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Play-off final: you finished 14th");
        text.Body.Should().Be("The away side is the winner of the lower league's semi-final. Kickoff is Tue 15 Dec 2026 19:00 UTC.");
        draft.RelatedEntityId.Should().BeNull("the final is not scheduled yet");
    }

    [Fact]
    public void A_final_that_is_set_keeps_its_opponent_behind_the_gate_because_it_gives_the_semi_final_away()
    {
        var draft = PlayoffInboxTemplates.FinalIsSet("Vale Athletic", isHome: false, Kickoff, Guid.CreateVersion7());

        var text = InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Play-off final: your match is set");
        text.Body.Should().NotContain("Vale");
        text.Spoiler.Should().Be("Away to Vale Athletic. Kickoff is Tue 15 Dec 2026 19:00 UTC.");
    }

    [Fact]
    public void The_draw_is_news_for_the_whole_country()
    {
        var countryId = Guid.CreateVersion7();
        var divisionId = Guid.CreateVersion7();
        var draft = NewsTemplates.PlayoffDraw("Premier", "Championship", "Vale", "Northfield", "Harbour", countryId, divisionId);

        draft.CountryId.Should().Be(countryId);
        draft.DivisionId.Should().Be(divisionId);

        var text = NewsMessageText.Render(draft.TemplateKey, draft.ParametersJson);

        text.Title.Should().Be("Premier: the play-off is drawn");
        text.Body.Should().Be("Vale host Northfield in the Championship semi-final. The winner plays Harbour for a place in Premier.");
    }

    [Theory]
    [InlineData(PlayoffStage.SemiFinal, true, true, PlayoffFates.ToFinal)]
    [InlineData(PlayoffStage.SemiFinal, false, false, PlayoffFates.Out)]
    [InlineData(PlayoffStage.Final, true, true, PlayoffFates.StaysUp)]
    [InlineData(PlayoffStage.Final, true, false, PlayoffFates.Relegated)]
    [InlineData(PlayoffStage.Final, false, true, PlayoffFates.Promoted)]
    [InlineData(PlayoffStage.Final, false, false, PlayoffFates.StaysDown)]
    public void What_a_match_meant_follows_from_the_stage_and_who_held_the_place(PlayoffStage stage, bool isHolder, bool won, string expected) =>
        PlayoffFates.For(stage, isHolder, won).Should().Be(expected);

    [Fact]
    public void A_result_stored_before_the_play_off_existed_still_renders_as_a_league_round()
    {
        // The parameter document of a league result has none of the play-off fields.
        const string stored = """{"roundNumber":7,"opponentName":"Vale","isHome":true,"goalsFor":1,"goalsAgainst":0,"outcome":"win","position":2}""";

        var text = InboxMessageText.Render(InboxTemplates.ResultRecorded, stored);

        text.Title.Should().Be("Round 7: At home to Vale");
        text.Spoiler.Should().Be("Won 1–0. You are 2nd in the table after round 7.");
    }

    [Theory]
    [InlineData(31, "Play-off semi-final: submit your team sheet")]
    [InlineData(32, "Play-off final: submit your team sheet")]
    [InlineData(33, "Play-off final: submit your team sheet")]
    [InlineData(30, "Round 30: submit your team sheet")]
    public void A_reminder_names_a_play_off_match_by_its_stage(int round, string title)
    {
        var draft = InboxTemplates.Reminder(round, "Vale", isHome: true, Kickoff);

        InboxMessageText.Render(draft.TemplateKey, draft.ParametersJson).Title.Should().Be(title);
    }

    [Fact]
    public void A_shoot_out_is_stored_as_plain_codes_and_reads_back_the_same()
    {
        var taker = Guid.CreateVersion7();
        var keeper = Guid.CreateVersion7();

        var result = new ShootoutResult(
            PenaltyShootout.Version,
            MatchSide.Away,
            HomeScore: 1,
            AwayScore: 2,
            [
                new ShootoutKick(1, 1, MatchSide.Away, taker, keeper, ShootoutKickOutcome.Scored),
                new ShootoutKick(2, 1, MatchSide.Home, keeper, taker, ShootoutKickOutcome.Saved),
                new ShootoutKick(3, 2, MatchSide.Away, taker, null, ShootoutKickOutcome.Wide),
            ]);

        var json = PlayoffShootoutDocument.Write(result);

        json.Should().Contain("\"firstSide\":\"away\"").And.Contain("\"outcome\":\"saved\"").And.Contain("\"takerId\"");

        var read = PlayoffShootoutDocument.Read(json);

        read.Version.Should().Be("shootout-v1");
        read.FirstSide.Should().Be("away");
        read.Kicks.Select(kick => (kick.Number, kick.Round, kick.Side, kick.Outcome)).Should().Equal(
            (1, 1, "away", "scored"),
            (2, 1, "home", "saved"),
            (3, 2, "away", "wide"));
        read.Kicks[2].GoalkeeperId.Should().BeNull();
    }
}
