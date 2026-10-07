using FluentAssertions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.Application.Tests.Match;

/// <summary>
/// Reading a played match's replay: the presentation and the entity tag it is cached under (§9.5).
/// </summary>
/// <remarks>
/// The replay is re-derived from the frozen snapshot rather than stored, so the read is where the whole
/// presentation contract is exercised in one place: the commentary, the highlights, both lineups, and the
/// live condition and rating curve, all built from the same frozen input the hash check verifies against.
/// </remarks>
public sealed class GetMatchPresentationTests
{
    private static readonly Guid FixtureId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SeasonId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorldId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid HomeClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AwayClubId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task A_replay_carries_both_lineups_and_the_live_condition_and_rating_curve()
    {
        var (matchId, snapshot, result) = StoredMatch();
        var query = new GetMatchPresentation(new SingleMatchQueries(Stored(matchId, snapshot, result)));

        var read = await query.ExecuteAsync(matchId, CancellationToken.None);

        read.Outcome.Should().Be(MatchReadOutcome.Found);
        read.EntityTag.Should().Be($"{result.OutputHash}:replay-v7", "the replay is cached under the result and its presentation version");
        read.Presentation.Should().NotBeNull();

        var presentation = read.Presentation!;

        presentation.PresentationVersion.Should().Be("replay-v7");
        presentation.HomeLineup.Should().NotBeNull();
        presentation.AwayLineup.Should().NotBeNull();
        presentation.LiveMetrics.Should().NotBeNull().And.NotBeEmpty();

        foreach (var lineup in new[] { presentation.HomeLineup!, presentation.AwayLineup! })
        {
            lineup.Formation.Should().Be("4-4-2", "a club with no saved plan takes the field in the default shape");
            lineup.PrimaryColour.Should().StartWith("#");
            lineup.SecondaryColour.Should().StartWith("#");
            lineup.Starters.Should().HaveCount(11);
            lineup.Bench.Should().HaveCount(7);
        }

        presentation.HomeLineup!.ShortName.Should().Be("HOM", "the short name is derived from the frozen club name");

        // The curve ends where the panel's last bar and badge end, so the panel and the replay describe the
        // same match rather than two derivations of it.
        var player = presentation.HomeLineup.Starters
            .First(candidate => candidate.SubbedOutMinute is null && !candidate.SentOff);
        var last = presentation.LiveMetrics!
            .Where(metric => metric.ParticipantId == player.ParticipantId)
            .OrderBy(metric => metric.Minute)
            .Last();

        last.ConditionBasisPoints.Should().Be(player.FinalCondition);
        last.RatingBasisPoints.Should().Be(player.FinalRating);
    }

    [Fact]
    public async Task A_match_that_does_not_reproduce_its_stored_hash_is_refused()
    {
        // The engine changed without a version bump, which is a deployment defect rather than a manager's
        // problem, so the read fails loudly instead of serving a replay of a different match (MAT-9).
        var (matchId, snapshot, result) = StoredMatch();

        var stored = Stored(matchId, snapshot, result) with { OutputHash = new string('0', 64) };
        var query = new GetMatchPresentation(new SingleMatchQueries(stored));

        var act = async () => await query.ExecuteAsync(matchId, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*did not reproduce its stored output hash*");
    }

    [Fact]
    public async Task An_unknown_match_is_reported_rather_than_refused()
    {
        var query = new GetMatchPresentation(new SingleMatchQueries(null));

        var read = await query.ExecuteAsync(Guid.CreateVersion7(), CancellationToken.None);

        read.Outcome.Should().Be(MatchReadOutcome.MatchNotFound);
        read.Presentation.Should().BeNull();
        read.EntityTag.Should().BeNull();
    }

    /// <summary>Builds a frozen snapshot and the result it simulates to, as the workflow's own steps do.</summary>
    private static (Guid MatchId, InputSnapshot Snapshot, MatchResultV1 Result) StoredMatch()
    {
        var rules = EngineRulesV2.Default;
        var matchId = Guid.CreateVersion7();

        var built = MatchSnapshotBuilder.Build(
            new FixtureSidesSnapshot(
                FixtureId,
                SeasonId,
                WorldId,
                Side(HomeClubId, "Home Town"),
                Side(AwayClubId, "Away City"),
                []),
            rules);

        var contentHash = CanonicalMatchSerializer.ContentHash(built.Input);
        var seed = MatchSeed.Derive("application-test-secret", FixtureId, contentHash, EngineVersions.EngineLabel);
        var input = built.Input with { Seed = seed };

        var snapshot = InputSnapshot.Freeze(
            Guid.CreateVersion7(),
            FixtureId,
            EngineVersions.EngineLabel,
            EngineVersions.RuleSetLabel,
            seed,
            MatchSeed.CommitmentOf(seed, EngineVersions.EngineLabel),
            MatchSnapshotDocument.Write(input, built.Repairs),
            CanonicalMatchSerializer.InputHash(input),
            DateTimeOffset.UnixEpoch);

        return (matchId, snapshot, MatchSimulator.Simulate(input, rules));
    }

    /// <summary>Wraps a frozen snapshot and its result as the read projection.</summary>
    private static MatchReadSnapshot Stored(Guid matchId, InputSnapshot snapshot, MatchResultV1 result) =>
        new(
            matchId,
            FixtureId,
            Guid.CreateVersion7(),
            "Premier Division",
            1,
            Guid.CreateVersion7(),
            "EN",
            "England",
            1,
            "Season 1",
            1,
            DateTimeOffset.UnixEpoch,
            result.EngineVersion,
            result.RuleSetVersion,
            result.HomeGoals,
            result.AwayGoals,
            MatchStatisticsDocument.Write(result),
            result.OutputHash,
            new MatchClubRow(HomeClubId, "Home Town", "HOM"),
            new MatchClubRow(AwayClubId, "Away City", "AWY"),
            snapshot);

    /// <summary>A club with a full squad and nothing prepared, so the builder lays out the default shape.</summary>
    private static ClubSideSource Side(Guid clubId, string name) =>
        new(clubId, name, null, [], [], [.. Players(clubId)]);

    private static IEnumerable<SnapshotPlayerRow> Players(Guid clubId)
    {
        for (var number = 1; number <= 18; number++)
        {
            var position = number switch
            {
                1 or 2 => PlayerPosition.Goalkeeper,
                3 or 4 => PlayerPosition.CentreBack,
                5 => PlayerPosition.LeftBack,
                6 or 15 => PlayerPosition.RightBack,
                7 => PlayerPosition.DefensiveMidfielder,
                8 or 9 => PlayerPosition.CentralMidfielder,
                10 => PlayerPosition.AttackingMidfielder,
                11 => PlayerPosition.RightWinger,
                12 or 17 => PlayerPosition.LeftWinger,
                16 => PlayerPosition.CentreBack,
                _ => PlayerPosition.Striker,
            };

            yield return Player(GuidFor(clubId, number), position, number);
        }
    }

    private static SnapshotPlayerRow Player(Guid playerId, PlayerPosition position, int shirtNumber) =>
        new(
            playerId,
            $"Player {shirtNumber}",
            $"P{shirtNumber}",
            position,
            [],
            [.. Enumerable.Repeat(12, MatchAttributeNames.Count)],
            9_000,
            100,
            PlayerState.NeutralBasisPoints,
            PlayerState.NeutralBasisPoints,
            true);

    private static Guid GuidFor(Guid clubId, int number) =>
        Guid.Parse($"{clubId.ToString("N")[..28]}{number:D4}");

    /// <summary>The read projection over one stored match, with no database behind it.</summary>
    private sealed class SingleMatchQueries(MatchReadSnapshot? stored) : IMatchQueries
    {
        public Task<MatchReadSnapshot?> GetMatchAsync(Guid matchId, CancellationToken cancellationToken) =>
            Task.FromResult(stored is not null && stored.MatchId == matchId ? stored : null);
    }
}
