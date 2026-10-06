using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Pins what the events of a spread of matches are, so that a change to play shows up as a changed hash and not as a
/// quiet difference (`engine-v10`).
/// </summary>
/// <remarks>
/// <para>
/// When the receiver chain was only a record of who passed to whom (M3) this hash was the proof that it moved
/// nothing: the chain drew from its own stream and never moved a point an event is read off, so the events of 40
/// matches were what they were before it existed. Since M4 the chain drives the outcome and a cross is headed, and since
/// M5 the holder may dribble on or shoot from distance, so play moves on purpose and the value was re-pinned in the
/// same commit, with the calibration it was re-pinned against.
/// </para>
/// <para>
/// A change that moves it is a change to play. Until engine-v10 is released it is re-pinned in the milestone that
/// makes it; after the release, a new engine version.
/// </para>
/// </remarks>
public sealed class ReceiverChoiceRegressionTests
{
    private const string PinnedEventsHash = "9509523edab285bb0df23deb94d618a7ad2559990b6471e114d3af8157dbf085";

    [Fact]
    public void The_events_and_scorelines_of_forty_matches_are_what_they_were_when_the_holder_began_to_choose_between_a_pass_a_dribble_and_a_shot()
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
