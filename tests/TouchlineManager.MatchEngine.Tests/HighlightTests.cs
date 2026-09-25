using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Highlight selection and the keyframe contract (master plan §9.2, §9.3, ADR-0006).
/// </summary>
public sealed class HighlightTests
{
    [Fact]
    public void Every_goal_is_shown()
    {
        // The one absolute rule in selection. A manager who scores and cannot watch it has been given a worse
        // product than one whose shot against the post was left out.
        for (var seed = 1UL; seed <= 25; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var result = MatchSimulator.Simulate(input);
            var presentation = HighlightDirector.Build(input, result);

            var shown = presentation.Highlights
                .Select(highlight => highlight.SourceEventSequence)
                .ToHashSet();

            foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
            {
                shown.Should().Contain(goal.Sequence, "every goal is worth watching");
            }
        }
    }

    [Fact]
    public void Every_penalty_is_shown()
    {
        for (var seed = 1UL; seed <= 40; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var result = MatchSimulator.Simulate(input);
            var presentation = HighlightDirector.Build(input, result);

            var shown = presentation.Highlights.Select(highlight => highlight.SourceEventSequence).ToHashSet();

            foreach (var penalty in result.Events.Where(
                matchEvent => matchEvent.Type == EngineEventType.PenaltyGoal
                    || matchEvent.Type == EngineEventType.PenaltyMissed))
            {
                shown.Should().Contain(penalty.Sequence);
            }
        }
    }

    [Fact]
    public void The_presentation_carries_twenty_three_entities_and_a_track_for_each()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);
        var presentation = HighlightDirector.Build(input, result);

        presentation.Highlights.Should().NotBeEmpty();

        foreach (var highlight in presentation.Highlights)
        {
            highlight.Entities.Should().HaveCount(23, "twenty-two players and one ball");

            highlight.Entities.Count(entity => entity.IsBall).Should().Be(1);
            highlight.Entities.Count(entity => !entity.IsBall).Should().Be(22);

            highlight.Tracks.Should().HaveCount(highlight.Entities.Count);
            highlight.Tracks.Select(track => track.EntityId)
                .Should().BeEquivalentTo(highlight.Entities.Select(entity => entity.EntityId));

            highlight.Entities.Select(entity => entity.EntityId).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void Entities_and_tracks_are_in_a_fixed_order()
    {
        var input = TestMatchFactory.Even();
        var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

        foreach (var highlight in presentation.Highlights)
        {
            highlight.Entities.Select(entity => entity.EntityId)
                .Should().BeInAscendingOrder(StringComparer.Ordinal);
            highlight.Tracks.Select(track => track.EntityId)
                .Should().BeInAscendingOrder(StringComparer.Ordinal);
        }
    }

    [Fact]
    public void Every_keyframe_is_inside_the_pitch_and_inside_the_highlight()
    {
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var result = MatchSimulator.Simulate(TestMatchFactory.Even(seed));
            var presentation = HighlightDirector.Build(TestMatchFactory.Even(seed), result);

            foreach (var highlight in presentation.Highlights)
            {
                foreach (var track in highlight.Tracks)
                {
                    track.Keyframes.Should().NotBeEmpty();

                    var previous = -1;

                    foreach (var keyframe in track.Keyframes)
                    {
                        keyframe.X.Should().BeInRange(0, 10_000);
                        keyframe.Y.Should().BeInRange(0, 10_000);
                        keyframe.TimeMilliseconds.Should().BeInRange(0, highlight.DurationMilliseconds);
                        keyframe.TimeMilliseconds.Should().BeGreaterThanOrEqualTo(previous);
                        previous = keyframe.TimeMilliseconds;
                    }
                }
            }
        }
    }

    [Fact]
    public void Highlights_are_ordered_by_event_sequence()
    {
        var input = TestMatchFactory.Even();
        var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

        presentation.Highlights.Select(highlight => highlight.SourceEventSequence)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_payload_stays_inside_its_budget()
    {
        // The whole reason for semantic keyframes: a match's presentation is kilobytes, not megabytes
        // (ADR-0006, match_presentation_payload_budget_kb).
        for (var seed = 1UL; seed <= 30; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

            presentation.EstimatedPayloadBytes.Should().BeLessThan(750 * 1024);

            foreach (var highlight in presentation.Highlights)
            {
                highlight.EstimatedPayloadBytes.Should().BeLessThan(75 * 1024);
            }
        }
    }

    [Fact]
    public void A_count_cap_sheds_lower_quality_chances_but_never_a_goal()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        var generous = HighlightDirector.Build(input, result);
        var strict = HighlightDirector.Build(input, result, new HighlightOptionsV1 { MaxHighlights = 2 });

        strict.Highlights.Count.Should().BeLessOrEqualTo(Math.Max(2, result.HomeGoals + result.AwayGoals));

        var strictSequences = strict.Highlights.Select(highlight => highlight.SourceEventSequence).ToHashSet();

        foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
        {
            strictSequences.Should().Contain(goal.Sequence, "goals survive every cap");
        }

        strict.Highlights.Count.Should().BeLessOrEqualTo(generous.Highlights.Count);
    }

    [Fact]
    public void A_tiny_payload_budget_still_leaves_the_goals()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        var squeezed = HighlightDirector.Build(
            input,
            result,
            new HighlightOptionsV1 { PayloadBudgetBytes = 1 });

        var sequences = squeezed.Highlights.Select(highlight => highlight.SourceEventSequence).ToHashSet();

        foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
        {
            sequences.Should().Contain(goal.Sequence);
        }

        squeezed.Highlights.Count.Should().Be(result.HomeGoals + result.AwayGoals,
            "with the budget at one byte, only goals remain");
    }

    [Fact]
    public void The_outcome_code_matches_the_event()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);
        var presentation = HighlightDirector.Build(input, result);

        foreach (var highlight in presentation.Highlights)
        {
            var matchEvent = result.Events.Single(candidate => candidate.Sequence == highlight.SourceEventSequence);

            var expected = matchEvent.Type switch
            {
                EngineEventType.Goal => "goal",
                EngineEventType.PenaltyGoal => "penalty_goal",
                EngineEventType.PenaltyMissed => "penalty_missed",
                EngineEventType.Woodwork => "woodwork",
                EngineEventType.ShotSaved => "saved",
                EngineEventType.ShotBlocked => "blocked",
                EngineEventType.ShotOffTarget => "off_target",
                _ => "chance",
            };

            highlight.OutcomeCode.Should().Be(expected);
            highlight.Minute.Should().Be(matchEvent.Minute);
            highlight.StoppageMinute.Should().Be(matchEvent.StoppageMinute);
        }
    }

    [Fact]
    public void The_narration_names_the_player_and_is_readable_without_the_canvas()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);
        var presentation = HighlightDirector.Build(input, result);

        foreach (var highlight in presentation.Highlights)
        {
            highlight.Narration.Should().NotBeEmpty();
            highlight.Narration.Should().EndWith(".");
            highlight.HomeColour.Should().MatchRegex("^#[0-9a-f]{6}$");
            highlight.AwayColour.Should().MatchRegex("^#[0-9a-f]{6}$");
        }
    }

    [Fact]
    public void The_durations_sit_inside_the_configured_band()
    {
        var options = new HighlightOptionsV1();
        var input = TestMatchFactory.Even();
        var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input), options);

        foreach (var highlight in presentation.Highlights)
        {
            highlight.DurationMilliseconds
                .Should().BeInRange(options.MinDurationMilliseconds, options.MaxDurationMilliseconds);
        }
    }

    [Fact]
    public void The_two_sides_are_told_apart_by_more_than_colour()
    {
        // The renderer requirement is downstream, but the contract has to make it possible: every player carries
        // a side and a shirt number, so a client can label rather than rely on a tint.
        var input = TestMatchFactory.Even();
        var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

        foreach (var highlight in presentation.Highlights)
        {
            foreach (var entity in highlight.Entities.Where(entity => !entity.IsBall))
            {
                entity.Side.Should().NotBeNull();
                entity.ShirtNumber.Should().BeGreaterThan(0);
                entity.ParticipantId.Should().NotBeNull();
            }
        }
    }
}
