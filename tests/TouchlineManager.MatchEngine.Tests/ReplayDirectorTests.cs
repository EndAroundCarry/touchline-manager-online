using FluentAssertions;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The replay director of `replay-v2` (master plan Stage 3): the condensed playback schedule, coordinated
/// team movement on the presentation's own axes, delta-compressed tracks, and synchronized commentary.
/// </summary>
public sealed class ReplayDirectorTests
{
    [Fact]
    public void The_replay_is_one_contiguous_schedule()
    {
        for (var seed = 1UL; seed <= 25; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

            presentation.Playback.Should().NotBeEmpty();
            presentation.Playback[0].StartMilliseconds.Should().Be(0);

            var cursor = 0;

            foreach (var segment in presentation.Playback)
            {
                segment.StartMilliseconds.Should().Be(cursor, "segments run back to back");
                segment.DurationMilliseconds.Should().BeGreaterThan(0);
                cursor += segment.DurationMilliseconds;
            }

            cursor.Should().Be(presentation.TotalPlaybackMilliseconds);

            presentation.Playback
                .Where(segment => segment.Kind == "highlight")
                .Select(segment => segment.SourceEventSequence)
                .Should().Equal(presentation.Highlights.Select(highlight => highlight.SourceEventSequence));

            presentation.Playback
                .Where(segment => segment.Kind == "bridge")
                .Select(segment => segment.SourceEventSequence)
                .Should().Equal(presentation.Bridges.Select(bridge => bridge.AfterEventSequence));

            foreach (var segment in presentation.Playback)
            {
                if (segment.Kind == "highlight")
                {
                    presentation.Highlights
                        .Single(highlight => highlight.SourceEventSequence == segment.SourceEventSequence)
                        .DurationMilliseconds.Should().Be(segment.DurationMilliseconds);
                }
                else
                {
                    presentation.Bridges
                        .Single(bridge => bridge.AfterEventSequence == segment.SourceEventSequence)
                        .DurationMilliseconds.Should().Be(segment.DurationMilliseconds);
                }
            }
        }
    }

    [Fact]
    public void The_replay_is_fitted_to_the_five_to_ten_minute_window()
    {
        var options = new HighlightOptionsV1();

        for (var seed = 1UL; seed <= 40; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

            // Ten minutes is a hard ceiling: the director sheds its lowest-quality chances until the replay
            // fits, and goals survive by design.
            presentation.TotalPlaybackMilliseconds.Should().BeLessThanOrEqualTo(options.MaxPlaybackMilliseconds);

            // Five minutes is the floor the recycling is stretched towards. A match with almost nothing in it
            // cannot be stretched that far without inventing football, so the promise is: the replay is either
            // five minutes long, or as long as every remaining gap bridged at its maximum can make it.
            var achievable = presentation.Highlights.Sum(highlight => highlight.DurationMilliseconds)
                + (Math.Max(0, presentation.Highlights.Count - 1) * options.MaxBridgeDurationMilliseconds);

            presentation.TotalPlaybackMilliseconds
                .Should().BeGreaterThanOrEqualTo(Math.Min(options.MinPlaybackMilliseconds, achievable));
        }
    }

    [Fact]
    public void Every_entity_stands_where_its_slot_says_on_the_presentations_axes()
    {
        // The play model speaks a 10_000 x 7_000 pitch; the presentation speaks 0..10_000 on both axes, which
        // is what the client scales onto a canvas. A mapping that forgot to scale the width would squeeze
        // every player into the top seventy per cent of the pitch, so the round trip is pinned here.
        var input = TestMatchFactory.Even();
        var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

        foreach (var highlight in presentation.Highlights)
        {
            foreach (var (side, isHome) in new[] { (input.Home, true), (input.Away, false) })
            {
                foreach (var slot in side.Slots)
                {
                    var entity = highlight.Entities
                        .Single(candidate => candidate.EntityId == $"{(isHome ? "H" : "A")}{slot.SlotNumber}");

                    entity.X.Should().BeCloseTo(isHome ? slot.X : 10_000 - slot.X, 150, "the anchor maps onto the presentation's length");
                    entity.Y.Should().BeCloseTo(isHome ? slot.Y : 10_000 - slot.Y, 60, "and the width is scaled, not shrunk");
                }
            }
        }
    }

    [Fact]
    public void Every_bridge_keyframe_is_on_the_pitch_and_inside_its_bridge()
    {
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

            presentation.Bridges.Should().NotBeEmpty();

            foreach (var bridge in presentation.Bridges)
            {
                bridge.Tracks.Should().HaveCount(23, "twenty-two players and one ball travel through the bridge");

                foreach (var track in bridge.Tracks)
                {
                    track.Keyframes.Should().NotBeEmpty();

                    var previous = -1;

                    foreach (var keyframe in track.Keyframes)
                    {
                        keyframe.X.Should().BeInRange(0, 10_000);
                        keyframe.Y.Should().BeInRange(0, 10_000);
                        keyframe.TimeMilliseconds.Should().BeInRange(0, bridge.DurationMilliseconds);
                        keyframe.TimeMilliseconds.Should().BeGreaterThanOrEqualTo(previous);
                        previous = keyframe.TimeMilliseconds;
                    }
                }
            }
        }
    }

    [Fact]
    public void A_bridge_continues_from_one_passage_into_the_next()
    {
        // What makes the cut between two highlights read as recycling rather than as a teleport: every entity
        // is picked up exactly where it was left, and put down exactly where the next passage begins.
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

            foreach (var bridge in presentation.Bridges)
            {
                var index = presentation.Highlights
                    .Select((highlight, position) => (highlight, position))
                    .Single(pair => pair.highlight.SourceEventSequence == bridge.AfterEventSequence)
                    .position;

                index.Should().BeGreaterThan(0, "no bridge precedes the first highlight");

                var previous = presentation.Highlights[index - 1];
                var next = presentation.Highlights[index];

                foreach (var track in bridge.Tracks)
                {
                    var from = previous.Tracks.Single(candidate => candidate.EntityId == track.EntityId).Keyframes[^1];
                    var to = next.Tracks.Single(candidate => candidate.EntityId == track.EntityId).Keyframes[0];

                    track.Keyframes[0].X.Should().Be(from.X);
                    track.Keyframes[0].Y.Should().Be(from.Y);
                    track.Keyframes[0].Z.Should().Be(from.Z);
                    track.Keyframes[^1].X.Should().Be(to.X);
                    track.Keyframes[^1].Y.Should().Be(to.Y);
                    track.Keyframes[^1].Z.Should().Be(to.Z);
                }
            }
        }
    }

    [Fact]
    public void Every_track_is_already_delta_compressed()
    {
        // Re-running the compressor over a director-produced track changes nothing: the frames that describe
        // no change in velocity or direction are already gone (Stage 3's delta-compression requirement).
        for (var seed = 1UL; seed <= 12; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

            foreach (var track in presentation.Highlights.SelectMany(highlight => highlight.Tracks)
                .Concat(presentation.Bridges.SelectMany(bridge => bridge.Tracks)))
            {
                KeyframeCompressor.Compress(track.Keyframes)
                    .Should().BeEquivalentTo(track.Keyframes, options => options.WithStrictOrdering());
            }
        }
    }

    [Fact]
    public void The_block_moves_with_the_play()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);
        var presentation = HighlightDirector.Build(input, result);
        var checkedAny = false;

        foreach (var highlight in presentation.Highlights)
        {
            var matchEvent = result.Events.Single(candidate => candidate.Sequence == highlight.SourceEventSequence);
            var ballY = highlight.Entities.Single(entity => entity.IsBall).Y;

            // The presentation carries the starting eleven as entities for now, so a chance taken by a
            // substitute has no entity to name. That gap — the entity list becoming the current XI — is the
            // replay director's own milestone; until then the block assertion is about the players it does
            // carry.
            var shooterId = highlight.Entities
                .FirstOrDefault(entity => !entity.IsBall && entity.ParticipantId == matchEvent.ParticipantId)
                ?.EntityId;

            foreach (var entity in highlight.Entities.Where(entity =>
                !entity.IsBall
                && entity.Side == matchEvent.Side
                && entity.Family is MatchPositionFamily.Midfield or MatchPositionFamily.Attack
                && entity.EntityId != shooterId))
            {
                var track = highlight.Tracks.Single(candidate => candidate.EntityId == entity.EntityId);
                var first = track.Keyframes[0];
                var last = track.Keyframes[^1];

                // Midfielders and forwards track the ball across the pitch, so the last frame of the passage
                // is at least as close to the ball's line as the first. The only allowed failure is a player
                // who is already standing on it.
                Math.Abs(last.Y - ballY).Should().BeLessThanOrEqualTo(Math.Abs(first.Y - ballY));

                // And the block shifts as one: nobody leaves the pitch or the move.
                last.X.Should().BeInRange(0, 10_000);
                last.Y.Should().BeInRange(0, 10_000);
                checkedAny = true;
            }
        }

        checkedAny.Should().BeTrue("every highlight has players to move");
    }

    [Fact]
    public void The_block_drops_off_without_the_ball()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);
        var presentation = HighlightDirector.Build(input, result);

        foreach (var highlight in presentation.Highlights)
        {
            var attackingSide = result.Events
                .Single(matchEvent => matchEvent.Sequence == highlight.SourceEventSequence)
                .Side;

            foreach (var entity in highlight.Entities.Where(entity => !entity.IsBall && entity.Side != attackingSide))
            {
                var track = highlight.Tracks.Single(candidate => candidate.EntityId == entity.EntityId);
                var first = track.Keyframes[0];
                var last = track.Keyframes[^1];

                if (entity.Side == MatchSide.Home)
                {
                    last.X.Should().BeLessThanOrEqualTo(first.X, "the home side's defenders retreat towards X = 0");
                }
                else
                {
                    last.X.Should().BeGreaterThanOrEqualTo(first.X, "the away side's defenders retreat towards X = 10,000");
                }
            }
        }
    }

    [Fact]
    public void The_goalkeepers_angle_towards_the_ball()
    {
        var input = TestMatchFactory.Even();
        var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

        foreach (var highlight in presentation.Highlights)
        {
            var ballY = highlight.Entities.Single(entity => entity.IsBall).Y;

            foreach (var keeper in highlight.Entities.Where(entity => entity.Family == MatchPositionFamily.Goalkeeper))
            {
                var track = highlight.Tracks.Single(candidate => candidate.EntityId == keeper.EntityId);
                var first = track.Keyframes[0];
                var last = track.Keyframes[^1];

                Math.Abs(last.Y - ballY).Should().BeLessThanOrEqualTo(
                    Math.Abs(first.Y - ballY),
                    "a goalkeeper covers the angle rather than holding his line");
            }
        }
    }

    [Fact]
    public void The_strike_is_tagged_with_the_action_it_is()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);
        var presentation = HighlightDirector.Build(input, result);

        foreach (var highlight in presentation.Highlights)
        {
            var matchEvent = result.Events.Single(candidate => candidate.Sequence == highlight.SourceEventSequence);

            if (matchEvent.ParticipantId is not Guid shooterId)
            {
                continue;
            }

            // A substitute who takes the chance is not carried as an entity yet; the entity list becoming the
            // current XI belongs to the replay director's own milestone.
            var entity = highlight.Entities.FirstOrDefault(candidate => candidate.ParticipantId == shooterId);

            if (entity is null)
            {
                continue;
            }

            var track = highlight.Tracks.Single(candidate => candidate.EntityId == entity.EntityId);

            track.Keyframes.Should().Contain(
                keyframe => keyframe.Action != null,
                "the renderer needs the action to show a shot rather than a stroll");

            if (matchEvent.IsGoal)
            {
                track.Keyframes[^1].Action.Should().Be("celebrate", "a goal is worth a celebration");
            }
        }
    }

    [Fact]
    public void Every_highlight_carries_commentary_synchronized_to_its_own_clock()
    {
        // The ticker requirement: a passage's lines arrive on the millisecond clock of the animation, inside
        // the passage, and in order — so what a viewer reads and what they watch are the same moment.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "absenceFixtures",
            "club",
            "clubId",
            "opponent",
            "playerId",
            "second",
            "secondId",
            "shotZone",
        };

        for (var seed = 1UL; seed <= 25; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var presentation = HighlightDirector.Build(input, MatchSimulator.Simulate(input));

            foreach (var highlight in presentation.Highlights)
            {
                highlight.Commentary.Should().NotBeEmpty();
                highlight.Commentary[0].TimeMilliseconds.Should().Be(0, "the passage opens with a line");

                var previous = -1;

                foreach (var line in highlight.Commentary)
                {
                    line.TimeMilliseconds.Should().BeInRange(0, highlight.DurationMilliseconds);
                    line.TimeMilliseconds.Should().BeGreaterThanOrEqualTo(previous);
                    previous = line.TimeMilliseconds;

                    line.TemplateKey.Should().StartWith("match.");
                    line.Text.Should().NotBeEmpty();
                    line.Text.Should().NotContain("{", "an unfilled placeholder is a bug a manager would read");

                    foreach (var parameter in line.Parameters)
                    {
                        allowed.Should().Contain(parameter.Name, "MAT-11 forbids anything else reaching a manager");
                    }
                }
            }
        }
    }

    [Fact]
    public void A_goals_commentary_names_the_scorer_and_lands_after_the_strike()
    {
        for (var seed = 1UL; seed <= 25; seed++)
        {
            var input = TestMatchFactory.Even(seed);
            var result = MatchSimulator.Simulate(input);
            var presentation = HighlightDirector.Build(input, result);

            foreach (var goal in result.Events.Where(matchEvent => matchEvent.IsGoal))
            {
                var highlight = presentation.Highlights.Single(candidate => candidate.SourceEventSequence == goal.Sequence);
                var scorer = input.SideOf(goal.Side).Squad.Single(participant => participant.ParticipantId == goal.ParticipantId);
                var key = goal.Type == EngineEventType.Goal ? "match.goal" : "match.penalty.goal";
                var line = highlight.Commentary.Single(candidate => candidate.TemplateKey == key);

                line.Text.Should().Contain(scorer.DisplayName);
                line.TimeMilliseconds.Should().BeGreaterThan(0, "the goal is not scored before the whistle");
                line.TimeMilliseconds.Should().BeLessThan(highlight.DurationMilliseconds);
            }
        }
    }
}
