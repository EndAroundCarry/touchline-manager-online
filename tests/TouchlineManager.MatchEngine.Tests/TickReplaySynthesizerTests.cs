using FluentAssertions;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick replay synthesizer's passage chunking: a continuous recording becomes the presentation's
/// passages — entities, clock, cuts and event cover — without inventing anything the match did not do
/// (`tick-engine-v1`, Milestone 8).
/// </summary>
public sealed class TickReplaySynthesizerTests
{
    private const ulong Seed = 20_260_925;

    private static (MatchState State, TickMatchRecording Recording, TickReplayFilm Film) Run(ulong seed = Seed)
    {
        var state = TickTestMatchState.Create(seed);
        var recorder = new TickMatchRecorder();

        TickMatchLoop.Run(state, recorder);

        var recording = recorder.Build();

        return (state, recording, TickReplaySynthesizer.Synthesize(state, recording));
    }

    [Fact]
    public void The_film_slices_the_whole_match_into_passages_of_the_presentation_length()
    {
        var (_, recording, film) = Run();
        var options = new HighlightOptionsV1();

        film.Passages.Should().NotBeEmpty("a whole match was recorded");
        film.Passages.Count.Should().BeLessThanOrEqualTo(options.MaxPassages, "the presentation caps how many passages it carries");
        film.TotalMilliseconds.Should().BeInRange(9 * 60_000, options.MaxFilmMilliseconds, "a whole match is about ten minutes of film");

        for (var index = 0; index < film.Passages.Count; index++)
        {
            var slice = film.Passages[index];

            slice.Passage.DurationMilliseconds.Should().BeLessThanOrEqualTo(options.MaxPassageMilliseconds, $"passage {index} is inside the film's longest passage");
            slice.Passage.DurationMilliseconds.Should().BePositive($"passage {index} shows at least one frame");

            if (slice.Passage.DurationMilliseconds >= options.MinPassageMilliseconds)
            {
                continue;
            }

            // A passage is only shorter than the minimum where the film was cut, a stretch of play ended at a jump,
            // the interval holds (a passage either side of the seam), or the final passage ran to the whistle.
            var last = index == film.Passages.Count - 1;
            var startsAfterJump = index > 0 && slice.FirstTick > film.Passages[index - 1].LastTick + 1;
            var endsAtJump = index + 1 < film.Passages.Count && film.Passages[index + 1].FirstTick > slice.LastTick + 1;
            var endsAtInterval = slice.LastTick + 1 < recording.TickCount
                && recording.PeriodAt(slice.LastTick) != recording.PeriodAt(slice.LastTick + 1);

            (last || startsAfterJump || endsAtJump || endsAtInterval || slice.Passage.Cuts.Count > 0 || slice.Passage.OutcomeCode == "half_time")
                .Should().BeTrue($"passage {index} is short only where the film jumps or the interval holds");
        }
    }

    [Fact]
    public void The_passages_tile_the_film_and_their_frames_join()
    {
        var (_, recording, film) = Run();
        var options = new HighlightOptionsV1();
        var filmMsPerTick = 100 / options.FilmMatchSecondsPerFilmSecond;
        var elapsed = 0;

        film.Passages[0].FirstTick.Should().Be(0, "the film opens on the match's first frame");

        for (var index = 0; index < film.Passages.Count; index++)
        {
            var (first, last, passage) = (film.Passages[index].FirstTick, film.Passages[index].LastTick, film.Passages[index].Passage);

            passage.DurationMilliseconds.Should().Be((last - first) * filmMsPerTick, "every tick is a tenth of a second of film");
            passage.Clock.Should().NotBeEmpty();
            passage.Clock[0].Should().Be(new ClockKeyframeV1(0, passage.StartMatchSecond));
            passage.Clock[^1].Should().Be(new ClockKeyframeV1(passage.DurationMilliseconds, passage.EndMatchSecond), $"passage {index} closes on its own clock");

            for (var point = 1; point < passage.Clock.Count; point++)
            {
                passage.Clock[point].TimeMilliseconds.Should().BeGreaterThanOrEqualTo(passage.Clock[point - 1].TimeMilliseconds);
                passage.Clock[point].MatchSecond.Should().BeGreaterThanOrEqualTo(passage.Clock[point - 1].MatchSecond, "the match clock never runs backwards inside a passage");
            }

            passage.Period.Should().BeOneOf(1, 2);

            if (index > 0)
            {
                var previous = film.Passages[index - 1];

                if (passage.Cuts.Count == 0)
                {
                    (first - previous.LastTick).Should().BeOneOf([0, 1], $"passage {index} picks up at the frame the one before it ended on, or at the half's seam");
                }
                else
                {
                    first.Should().BeGreaterThan(previous.LastTick + 1, $"the cut at passage {index} covers a jump");
                    passage.Cuts.Should().OnlyContain(cut =>
                        cut.TimeMilliseconds == 0
                        && cut.DurationMilliseconds == options.CutMilliseconds
                        && (cut.Kind == "kick_off" || cut.Kind == "half_time"));
                }
            }

            elapsed += passage.DurationMilliseconds;
        }

        elapsed.Should().Be(film.TotalMilliseconds);
    }

    [Fact]
    public void Every_passage_carries_the_viewers_entities_at_the_recordings_positions()
    {
        var (state, recording, film) = Run();
        var dismissals = Dismissals(state, recording);
        var indexOf = recording.Entities.ToDictionary(entity => entity.IsBall ? "ball" : FilmRoster.EntityId(entity.Index), entity => entity.Index);

        foreach (var slice in film.Passages)
        {
            var expected = recording.Entities
                .Where(entity => entity.IsBall || !dismissals.TryGetValue(entity.Index, out var dismissed) || slice.FirstTick < dismissed)
                .Select(entity => entity.IsBall ? "ball" : FilmRoster.EntityId(entity.Index))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            slice.Passage.Entities.Select(entity => entity.EntityId).Should().Equal(expected, "every entity is drawn in the order the client reads");
            slice.Passage.HomeColour.Should().MatchRegex("^#[0-9a-f]{6}$");
            slice.Passage.AwayColour.Should().MatchRegex("^#[0-9a-f]{6}$");

            foreach (var entity in slice.Passage.Entities)
            {
                var point = FilmSpace.FromEngine(
                    recording.XAt(slice.FirstTick, indexOf[entity.EntityId]),
                    recording.YAt(slice.FirstTick, indexOf[entity.EntityId]));

                entity.X.Should().Be(FilmSpace.NormalizeX(point.X));
                entity.Y.Should().Be(FilmSpace.NormalizeY(point.Y));
                entity.X.Should().BeInRange(0, FilmSpace.Normalized);
                entity.Y.Should().BeInRange(0, FilmSpace.Normalized);
            }
        }

        var opening = film.Passages[0].Passage.Entities;

        opening.Should().ContainSingle(entity => entity.IsBall, "the ball is always drawn");
        opening.Single(entity => entity.IsBall).ParticipantId.Should().BeNull("the ball belongs to no player");

        foreach (var entity in opening.Where(entity => !entity.IsBall))
        {
            entity.Side.Should().NotBeNull();
            entity.ParticipantId.Should().NotBeNull();
            entity.ShirtNumber.Should().BePositive();
            entity.Name.Should().NotBeNullOrWhiteSpace();
            entity.Family.Should().NotBeNull();
            entity.Position.Should().BeOneOf("GK", "DF", "MF", "FW");
        }
    }

    [Fact]
    public void Every_recorded_event_lands_in_exactly_one_passage()
    {
        var (_, recording, film) = Run();

        var sequences = film.Passages.SelectMany(slice => slice.Passage.EventSequences).ToArray();

        sequences.Should().Equal(recording.Events.Select(stamp => stamp.Sequence), "every event is shown exactly once, in order");

        foreach (var slice in film.Passages)
        {
            if (slice.Passage.SourceEventSequence != 0)
            {
                slice.Passage.EventSequences.Should().Contain(slice.Passage.SourceEventSequence);
            }

            slice.Passage.Narration.Should().NotBeNullOrWhiteSpace();
            slice.Passage.Narration.Should().EndWith(".");
        }
    }

    [Fact]
    public void A_goal_chains_into_a_kick_off_cut_and_the_interval_into_a_half_time_cut()
    {
        var (state, _, film) = Run();
        var options = new HighlightOptionsV1();

        var cuts = film.Passages
            .SelectMany((slice, index) => slice.Passage.Cuts.Select(cut => (Index: index, Cut: cut)))
            .ToArray();

        cuts.Should().NotBeEmpty("the match has goals and an interval for the film to jump over");
        cuts.Select(entry => entry.Cut.Kind).Should().BeSubsetOf(["kick_off", "half_time"]);
        cuts.Select(entry => entry.Index).Should().OnlyHaveUniqueItems("a cut opens a passage, and a passage is opened once");

        var goals = state.Events.Count(matchEvent => matchEvent.IsGoal);

        cuts.Count(entry => entry.Cut.Kind == "kick_off").Should().Be(goals, "every goal is followed by the conceding side's kick-off");

        var interval = film.Passages.Select((slice, index) => (slice, index)).Single(entry => entry.slice.Passage.OutcomeCode == "half_time");
        var halfTimeCut = cuts.Single(entry => entry.Cut.Kind == "half_time");

        halfTimeCut.Index.Should().Be(interval.index + 1, "the second half resumes at the interval's cut");
        film.Passages[halfTimeCut.Index].Passage.Period.Should().Be(2);
    }

    [Fact]
    public void The_half_time_passage_carries_the_interval_on_the_second_halfs_clock()
    {
        var (state, _, film) = Run();

        var interval = film.Passages.Single(slice => slice.Passage.OutcomeCode == "half_time");
        var halfTimeSecond = state.Rules.HalfTimeMinute * state.Rules.SecondsPerMinute;

        interval.Passage.Period.Should().Be(2);
        interval.Passage.Narration.Should().Be("Half-time.");
        interval.Passage.StartMatchSecond.Should().Be(halfTimeSecond, "the interval stands on 45:00");
        interval.Passage.EndMatchSecond.Should().BeInRange(halfTimeSecond, halfTimeSecond + 2, "the hold consumes a couple of seconds of the clock");
        interval.Passage.SourceEventSequence.Should().Be(0, "no event is the interval itself");
        interval.Passage.Cuts.Should().BeEmpty();
    }

    [Fact]
    public void The_synthesis_is_deterministic()
    {
        var first = Run(Seed);
        var second = Run(Seed);

        first.Film.Should().BeEquivalentTo(second.Film, options => options.WithStrictOrdering(), "the same recording always synthesizes the same film");
    }

    /// <summary>Gets the tick each player left the pitch at, by entity, from the answer's own vocabulary.</summary>
    private static Dictionary<int, int> Dismissals(MatchState state, TickMatchRecording recording)
    {
        var entities = recording.Entities.Where(entity => !entity.IsBall).ToDictionary(entity => entity.ParticipantId, entity => entity.Index);
        var dismissals = new Dictionary<int, int>();

        foreach (var stamp in recording.Events)
        {
            var matchEvent = state.Events[stamp.Sequence - 1];

            if (matchEvent.Type is EngineEventType.RedCard or EngineEventType.SecondYellowCard
                && matchEvent.ParticipantId is Guid participant
                && entities.TryGetValue(participant, out var index))
            {
                dismissals[index] = stamp.Tick;
            }
        }

        return dismissals;
    }

    [Fact]
    public void A_player_who_has_been_sent_off_is_not_drawn_after_the_tick_he_leaves()
    {
        // No seed's match has produced a sending-off yet (the discipline model barely reaches one), so the rule is
        // pinned on a minimal trace rather than on a lucky match.
        var state = TickTestMatchState.Create(Seed);
        var participant = Guid.NewGuid();
        const int Dismissed = 7;

        state.Events.Add(new EngineEventV1
        {
            Sequence = 1,
            Minute = 30,
            Side = MatchSide.Home,
            ClubId = state.Input.Home.ClubId,
            Type = EngineEventType.RedCard,
            ParticipantId = participant,
        });

        var after = TickReplaySynthesizer.Synthesize(state, Trace(participant, Dismissed, dismissalTick: 0, ticks: 2));
        var before = TickReplaySynthesizer.Synthesize(state, Trace(participant, Dismissed, dismissalTick: 1, ticks: 2));

        after.Passages.Should().HaveCount(1);
        after.Passages[0].Passage.Entities.Should().NotContain(
            entity => entity.EntityId == FilmRoster.EntityId(Dismissed),
            "a sent-off player is not on the pitch to be drawn");
        after.Passages[0].Passage.Entities.Should().HaveCount(22, "his side plays on a man short");

        before.Passages.Should().HaveCount(1);
        before.Passages[0].Passage.Entities.Should().Contain(
            entity => entity.EntityId == FilmRoster.EntityId(Dismissed),
            "the player was still on the pitch when the passage began");
    }

    /// <summary>Builds a minimal continuous trace of a match that never kicked off, so a rule can be checked without its seed.</summary>
    /// <param name="dismissed">The participant sent off.</param>
    /// <param name="dismissedEntity">The entity the sent-off participant occupies.</param>
    /// <param name="dismissalTick">The tick the sending-off was stamped at.</param>
    /// <param name="ticks">How many ticks the trace runs for.</param>
    private static TickMatchRecording Trace(Guid dismissed, int dismissedEntity, int dismissalTick, int ticks)
    {
        var entities = new List<TickRecordingEntity>();
        var second = new int[ticks];
        var period = new int[ticks];

        for (var index = 0; index < TickMatchRecording.EntityCount; index++)
        {
            var ball = index == TickMatchRecording.BallEntityIndex;
            var participant = ball
                ? Guid.Empty
                : index == dismissedEntity ? dismissed : Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}");

            entities.Add(new TickRecordingEntity(
                index,
                ball,
                index < 11 ? MatchSide.Home : MatchSide.Away,
                participant,
                ball ? 0 : index + 1,
                MatchPositionFamily.Midfield,
                ball ? string.Empty : $"Player {index + 1}"));
        }

        for (var tick = 0; tick < ticks; tick++)
        {
            second[tick] = tick + 1;
            period[tick] = 1;
        }

        return new TickMatchRecording(
            entities,
            ticks,
            new int[ticks * TickMatchRecording.EntityCount],
            new int[ticks * TickMatchRecording.EntityCount],
            new int[ticks * TickMatchRecording.EntityCount],
            second,
            period,
            [new TickEventStamp(dismissalTick, 1)],
            [],
            []);
    }
}
