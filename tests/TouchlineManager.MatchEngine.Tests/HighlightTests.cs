using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Reel selection and the passage contract (`replay-v3`, master plan §9.2–§9.3, ADR-0006).
/// </summary>
public sealed class HighlightTests
{
    [Fact]
    public void Every_goal_is_on_the_reel()
    {
        // The one absolute rule in selection. A manager who scores and cannot watch it has been given a worse
        // product than one whose shot against the post was left out.
        for (var seed = 1UL; seed <= 25; seed++)
        {
            var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
            {
                TestMatchFactory.ReelCovers(presentation, goal.Sequence)
                    .Should().BeTrue("every goal is worth watching");
            }
        }
    }

    [Fact]
    public void Every_penalty_is_on_the_reel()
    {
        for (var seed = 1UL; seed <= 40; seed++)
        {
            var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var penalty in result.Events.Where(
                matchEvent => matchEvent.Type is EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed))
            {
                TestMatchFactory.ReelCovers(presentation, penalty.Sequence).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void A_clip_leads_into_its_chance_and_never_runs_back_past_the_start()
    {
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            foreach (var clip in presentation.Reel)
            {
                clip.StartMilliseconds.Should().BeGreaterThanOrEqualTo(0);
                clip.EndMilliseconds.Should().BeGreaterThan(clip.StartMilliseconds);
                clip.StartMilliseconds.Should().BeLessThanOrEqualTo(presentation.TotalPlaybackMilliseconds);
            }
        }
    }

    [Fact]
    public void The_reel_is_ordered_and_inside_the_twelve_minute_cap()
    {
        var options = new HighlightOptionsV1();

        for (var seed = 1UL; seed <= 20; seed++)
        {
            var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even(seed));

            presentation.Reel.Should().BeInAscendingOrder(clip => clip.StartMilliseconds);

            var total = presentation.Reel.Sum(clip => clip.DurationMilliseconds);

            total.Should().BeLessOrEqualTo(options.MaxReelMilliseconds);
        }
    }

    [Fact]
    public void A_clip_outcome_code_matches_the_event()
    {
        var (result, presentation) = TestMatchFactory.Play(TestMatchFactory.Even());

        foreach (var clip in presentation.Reel)
        {
            var matchEvent = result.Events.Single(candidate => candidate.Sequence == clip.SourceEventSequence);

            var expected = matchEvent.Type switch
            {
                EngineEventType.Goal => "goal",
                EngineEventType.PenaltyGoal => "penalty_goal",
                EngineEventType.PenaltyMissed => "penalty_missed",
                EngineEventType.FreeKickShot => "free_kick_shot",
                EngineEventType.Woodwork => "woodwork",
                EngineEventType.ShotSaved => "saved",
                EngineEventType.ShotBlocked => "blocked",
                EngineEventType.ShotOffTarget => "off_target",
                _ => "play",
            };

            clip.OutcomeCode.Should().Be(expected);
            clip.Minute.Should().Be(matchEvent.Minute);
            clip.StoppageMinute.Should().Be(matchEvent.StoppageMinute);
        }
    }

    [Fact]
    public void Every_passage_carries_the_colours_and_a_readable_narration()
    {
        var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even());

        foreach (var passage in presentation.Passages)
        {
            passage.Narration.Should().NotBeEmpty().And.EndWith(".");
            passage.HomeColour.Should().MatchRegex("^#[0-9a-f]{6}$");
            passage.AwayColour.Should().MatchRegex("^#[0-9a-f]{6}$");
        }
    }

    [Fact]
    public void The_two_sides_are_told_apart_by_more_than_colour()
    {
        var (_, presentation) = TestMatchFactory.Play(TestMatchFactory.Even());

        foreach (var passage in presentation.Passages)
        {
            foreach (var entity in passage.Entities.Where(entity => !entity.IsBall))
            {
                entity.Side.Should().NotBeNull();
                entity.ShirtNumber.Should().BeGreaterThan(0);
                entity.ParticipantId.Should().NotBeNull();
            }
        }
    }

    [Fact]
    public void A_tiny_payload_budget_does_not_break_the_goals()
    {
        // Even when the ladder cannot fit the film, every goal is still in a passage and on the reel.
        var input = TestMatchFactory.Even();
        var (result, presentation) = TestMatchFactory.Play(input, new HighlightOptionsV1 { PayloadBudgetBytes = 1 });

        var events = presentation.Passages.SelectMany(passage => passage.EventSequences).ToHashSet();

        foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
        {
            events.Should().Contain(goal.Sequence);
        }
    }
}
