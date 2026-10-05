using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The receiver chain names who passed to whom and moves the ball's waypoints, and nothing else (`engine-v10`).
/// </summary>
/// <remarks>
/// <para>
/// The chain draws from its own stream, so no play draw moves, and the final waypoint and the point a ball is
/// lost at are never pulled, so every position an event is read off stays where it was. This test pins that: a
/// hash of every event of a spread of matches, with the scoreline, as the engine played them before the chain
/// existed. Passes, assists, passages and the film are allowed to move; this hash is not.
/// </para>
/// <para>
/// A change that moves it is a change to play, and belongs to a later milestone (the chain driving the outcome,
/// solo play), where it is made on purpose and this value re-pinned in the same commit.
/// </para>
/// </remarks>
public sealed class ReceiverChoiceRegressionTests
{
    private const string PinnedEventsHash = "a9d860ea3f596a172fec6881cc4d87fda465e9b4c8f3ee9d98d7bdffa62dc471";

    [Fact]
    public void The_events_and_scorelines_of_forty_matches_are_what_they_were_before_the_receiver_chain()
    {
        var text = new StringBuilder();

        for (var seed = 1UL; seed <= 20; seed++)
        {
            Append(text, MatchSimulator.Simulate(TestMatchFactory.Even(seed)));
            Append(text, MatchSimulator.Simulate(TestMatchFactory.Mismatched(seed)));
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

        hash.Should().Be(PinnedEventsHash);
    }

    private static void Append(StringBuilder text, MatchResultV1 result)
    {
        text.Append(result.HomeGoals).Append('-').Append(result.AwayGoals).Append('\n');

        foreach (var matchEvent in result.Events)
        {
            text.Append(matchEvent.Sequence).Append('|')
                .Append(matchEvent.Minute).Append('|')
                .Append(matchEvent.StoppageMinute).Append('|')
                .Append((int)matchEvent.Side).Append('|')
                .Append((int)matchEvent.Type).Append('|')
                .Append(matchEvent.ParticipantId).Append('|')
                .Append(matchEvent.SecondaryParticipantId).Append('|')
                .Append(matchEvent.Zone).Append('|')
                .Append(matchEvent.QualityBasisPoints).Append('|')
                .Append(matchEvent.X).Append('|')
                .Append(matchEvent.Y).Append('\n');
        }
    }
}
