using System.Text.Json.Nodes;
using FluentAssertions;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.Application.Tests.Match;

/// <summary>
/// The stored documents: the frozen snapshot and the result's statistics (`MAT-9`, master plan §4.5).
/// </summary>
/// <remarks>
/// A snapshot that did not round-trip would simulate into a result whose recorded provenance is a lie, so the
/// property these tests pin is the one the lock workflow verifies before every simulation: reading the stored
/// document back reproduces the hash it was stored with, to the byte.
/// </remarks>
public sealed class MatchDocumentTests
{
    private static readonly Guid FixtureId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SeasonId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorldId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void A_snapshot_document_round_trips_to_the_same_hash()
    {
        var input = Snapshot(seed: 987_654_321UL);

        var repairs = new List<SnapshotRepair>
        {
            new(FixtureId, 3, SnapshotRepairReason.PlayerUnavailable, Guid.CreateVersion7(), Guid.CreateVersion7()),
            new(FixtureId, 12, SnapshotRepairReason.SlotEmpty, null, Guid.CreateVersion7()),
        };

        var content = MatchSnapshotDocument.Read(MatchSnapshotDocument.Write(input, repairs));

        CanonicalMatchSerializer.InputHash(content.Input)
            .Should()
            .Be(CanonicalMatchSerializer.InputHash(input), "a stored snapshot must reproduce itself (MAT-9)");

        content.Input.Seed.Should().Be(987_654_321UL);
        content.Input.Home.ClubName.Should().Be("Home");
        content.Input.Home.Squad.Should().HaveCount(2);
        content.Input.Home.Squad[0].Attributes.Values.Should().Equal(input.Home.Squad[0].Attributes.Values);
        content.Repairs.Should().Equal(repairs);
    }

    [Fact]
    public void A_snapshot_document_of_another_schema_is_refused()
    {
        var json = MatchSnapshotDocument.Write(Snapshot(seed: 1UL), []);
        var foreign = json.Replace(MatchSnapshotDocument.Schema, "match-snapshot-v2", StringComparison.Ordinal);

        var act = () => MatchSnapshotDocument.Read(foreign);

        act.Should()
            .Throw<InvalidMatchInputException>()
            .Where(exception => exception.Message.Contains("match-snapshot-v1"));
    }

    [Fact]
    public void An_unreadable_snapshot_document_is_refused()
    {
        var act = () => MatchSnapshotDocument.Read("not json at all");

        act.Should().Throw<InvalidMatchInputException>();
    }

    [Fact]
    public void A_snapshot_document_refuses_attributes_outside_the_scale()
    {
        // The attribute converter goes through the engine's own factory, so a document whose values were
        // edited out of range is refused on the way in rather than simulated.
        var document = JsonNode.Parse(MatchSnapshotDocument.Write(Snapshot(seed: 1UL), []))!;

        document["input"]!["home"]!["squad"]![0]!["attributes"]![27] = 99;

        var act = () => MatchSnapshotDocument.Read(document.ToJsonString());

        act.Should().Throw<InvalidMatchInputException>();
    }

    [Fact]
    public void A_snapshot_document_refuses_a_truncated_attribute_set()
    {
        var document = JsonNode.Parse(MatchSnapshotDocument.Write(Snapshot(seed: 1UL), []))!;
        var attributes = (JsonArray)document["input"]!["home"]!["squad"]![0]!["attributes"]!;

        attributes.RemoveAt(27);

        var act = () => MatchSnapshotDocument.Read(document.ToJsonString());

        act.Should().Throw<InvalidMatchInputException>("a player has exactly twenty-eight attributes (TRN-4)");
    }

    [Fact]
    public void A_statistics_document_round_trips()
    {
        var result = SimulatedMatch(seed: 5UL);

        var content = MatchStatisticsDocument.Read(MatchStatisticsDocument.Write(result));

        content.Home.Should().Be(result.Home);
        content.Away.Should().Be(result.Away);
        content.TotalMinutesPlayed.Should().Be(result.TotalMinutesPlayed);
    }

    [Fact]
    public void A_statistics_document_of_another_schema_is_refused()
    {
        var json = MatchStatisticsDocument
            .Write(SimulatedMatch(seed: 5UL))
            .Replace(MatchStatisticsDocument.Schema, "match-statistics-v2", StringComparison.Ordinal);

        var act = () => MatchStatisticsDocument.Read(json);

        act.Should().Throw<InvalidMatchInputException>();
    }

    /// <summary>A minimal but complete snapshot, with two players a side.</summary>
    private static MatchInputV1 Snapshot(ulong seed) => new()
    {
        FixtureId = FixtureId,
        WorldId = WorldId,
        SeasonId = SeasonId,
        EngineVersion = EngineVersions.EngineLabel,
        RuleSetVersion = EngineVersions.RuleSetLabel,
        Home = Side("Home", Guid.Parse("11111111-1111-1111-1111-111111111111")),
        Away = Side("Away", Guid.Parse("22222222-2222-2222-2222-222222222222")),
        HomeAdvantageBasisPoints = 10_300,
        FormulaConfigurationHash = "formula-hash",
        Seed = seed,
    };

    private static MatchSideV1 Side(string name, Guid clubId) => new()
    {
        ClubId = clubId,
        ClubName = name,
        Squad =
        [
            Participant(clubId, Guid.Parse($"{clubId.ToString("N")[..28]}0001"), "Keeper", 1, MatchPosition.Goalkeeper),
            Participant(clubId, Guid.Parse($"{clubId.ToString("N")[..28]}0002"), "Striker", 2, MatchPosition.Striker),
        ],
        Slots =
        [
            new MatchSlotV1
            {
                SlotNumber = 1,
                Family = MatchPositionFamily.Goalkeeper,
                Role = MatchRole.Goalkeeper,
                X = 500,
                Y = 5_000,
                ParticipantId = Guid.Parse($"{clubId.ToString("N")[..28]}0001"),
            },
        ],
        Instructions = new MatchInstructionsV1(),
    };

    private static MatchParticipantV1 Participant(
        Guid clubId,
        Guid playerId,
        string displayName,
        int shirtNumber,
        MatchPosition position) => new()
    {
        ParticipantId = playerId,
        PlayerId = playerId,
        ClubId = clubId,
        DisplayName = displayName,
        ShirtNumber = shirtNumber,
        Position = position,
        Attributes = PlayerAttributesV1.From([.. Enumerable.Repeat(12, MatchAttributeNames.Count)]),
        State = new PlayerMatchStateV1
        {
            ConditionBasisPoints = 9_000,
            FatigueBasisPoints = 100,
            MoraleBasisPoints = PlayerState.NeutralBasisPoints,
            SharpnessBasisPoints = PlayerState.NeutralBasisPoints,
        },
    };

    private static MatchResultV1 SimulatedMatch(ulong seed) => new()
    {
        EngineVersion = EngineVersions.EngineLabel,
        RuleSetVersion = EngineVersions.RuleSetLabel,
        HomeGoals = 2,
        AwayGoals = 1,
        Home = Statistics(goals: 2),
        Away = Statistics(goals: 1),
        Events = [],
        PlayerLines = [],
        TotalMinutesPlayed = 94,
        InputHash = CanonicalMatchSerializer.InputHash(Snapshot(seed)),
        OutputHash = "output-hash",
    };

    private static MatchStatisticsV1 Statistics(int goals) => new()
    {
        PossessionBasisPoints = 5_200,
        Goals = goals,
        Shots = 14,
        ShotsOnTarget = 6,
        ShotsOffTarget = 5,
        ShotsBlocked = 2,
        WoodworkHits = 1,
        Saves = 4,
        Corners = 6,
        Offsides = 2,
        Fouls = 11,
        YellowCards = 3,
        RedCards = 0,
        PenaltiesAwarded = 0,
        PenaltiesScored = 0,
        Injuries = 0,
        Substitutions = 2,
    };
}
