using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Squad;

/// <summary>
/// The player aggregates: identity, attributes, state, contract, and registration invariants
/// (`SQ-6`, `TRN-4`, `TRN-5..TRN-7`, `CON-1`, `CON-8`).
/// </summary>
public sealed class PlayerAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_player_is_created_active_with_its_identity_normalised()
    {
        var player = GeneratedPlayer();

        player.Status.Should().Be(PlayerStatus.Active);
        player.NationalityCode.Should().Be("ENG");
        player.FullName.Should().Be("Corin Alderwick");
        player.Version.Should().Be(1);
        player.CreatedAt.Should().Be(Now);
        player.SecondaryPositionCodes.Should().Be("cb");
        player.SecondaryPositions.Should().Equal(PlayerPosition.CentreBack);
    }

    [Fact]
    public void Secondary_positions_are_stored_in_a_stable_order()
    {
        var player = Player.Generate(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Identity() with
            {
                SecondaryPositions = [PlayerPosition.Striker, PlayerPosition.CentreBack, PlayerPosition.Striker],
            },
            Now);

        // Ordered by enum value and de-duplicated, so a regenerated squad compares row for row.
        player.SecondaryPositionCodes.Should().Be("cb,st");
    }

    [Fact]
    public void A_player_knows_its_age_in_a_game_year()
    {
        var player = GeneratedPlayer();

        player.AgeIn(2026).Should().Be(2026 - player.BirthGameYear);
    }

    [Fact]
    public void A_player_is_at_home_in_a_family_through_either_a_primary_or_a_secondary_position()
    {
        var player = GeneratedPlayer();

        player.PlaysIn(PositionFamily.Defence).Should().BeTrue("centre back is a secondary position");
        player.PrimaryPosition.Should().Be(PlayerPosition.RightBack);
    }

    [Fact]
    public void An_impossible_birth_day_or_physique_is_a_programming_error()
    {
        var badDay = () => Player.Generate(Guid.CreateVersion7(), Guid.CreateVersion7(), Identity() with { BirthDayOfYear = 0 }, Now);
        var tall = () => Player.Generate(Guid.CreateVersion7(), Guid.CreateVersion7(), Identity() with { HeightCm = 0 }, Now);
        var heavy = () => Player.Generate(Guid.CreateVersion7(), Guid.CreateVersion7(), Identity() with { WeightKg = -1 }, Now);

        badDay.Should().Throw<ArgumentOutOfRangeException>();
        tall.Should().Throw<ArgumentOutOfRangeException>();
        heavy.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_hidden_value_outside_the_attribute_scale_is_a_programming_error()
    {
        var badPotential = () => Player.Generate(Guid.CreateVersion7(), Guid.CreateVersion7(), Identity() with { Potential = 21 }, Now);
        var badReputation = () => Player.Generate(Guid.CreateVersion7(), Guid.CreateVersion7(), Identity() with { Reputation = 0 }, Now);

        badPotential.Should().Throw<ArgumentOutOfRangeException>();
        badReputation.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_player_lifecycle_move_updates_the_version_and_status()
    {
        var player = GeneratedPlayer();

        player.Retire(Now.AddDays(1));

        player.Status.Should().Be(PlayerStatus.Retired);
        player.Version.Should().Be(2);
        player.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Attributes_are_checksummed_over_their_canonical_order()
    {
        var attributes = PlayerAttributes.Create(Guid.CreateVersion7(), Attributes());

        attributes.SchemaVersion.Should().Be(PlayerAttributes.CurrentSchemaVersion);
        attributes.ChecksumMatches().Should().BeTrue();
        attributes.Checksum.Should().HaveLength(64);
        attributes.ToSet().Should().Be(Attributes());
    }

    [Fact]
    public void An_attribute_outside_the_displayed_scale_is_rejected()
    {
        var act = () => PlayerAttributes.Create(
            Guid.CreateVersion7(),
            Attributes() with { Finishing = WorldRuleSet.AttributeMax + 1 });

        act.Should().Throw<ArgumentOutOfRangeException>("TRN-4");
    }

    [Fact]
    public void An_attribute_set_needs_exactly_the_canonical_number_of_values()
    {
        var act = () => PlayerAttributeSet.FromValues([1, 2, 3]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_attribute_is_addressable_by_its_canonical_name()
    {
        var set = Attributes();

        set.ValueOf(AttributeName.Finishing).Should().Be(set.Finishing);
        set.ValueOf(AttributeName.AerialAbility).Should().Be(set.AerialAbility);
        set.OfFamily(AttributeFamily.Goalkeeping).Should().HaveCount(set.Goalkeeping.Count);
    }

    [Fact]
    public void A_state_row_opens_rested_unloaded_and_neutral()
    {
        var state = PlayerState.Open(Guid.CreateVersion7());

        state.ConditionBp.Should().Be(WorldRuleSet.StateBasisPointsMax);
        state.FatigueBp.Should().Be(WorldRuleSet.StateBasisPointsMin);
        state.MoraleBp.Should().Be(PlayerState.NeutralBasisPoints);
        state.MatchSharpnessBp.Should().Be(PlayerState.NeutralBasisPoints);
        state.DevelopmentRemainder.Should().Be(0);
        state.LastProgressionDate.Should().BeNull();
    }

    [Fact]
    public void A_state_value_outside_its_scale_is_rejected()
    {
        var act = () => PlayerState.Create(Guid.CreateVersion7(), 10_001, 0, 0, 0, 0, null);

        act.Should().Throw<ArgumentOutOfRangeException>("TRN-5");
    }

    [Fact]
    public void A_negative_development_remainder_is_rejected()
    {
        var act = () => PlayerState.Create(Guid.CreateVersion7(), 0, 0, 0, 0, -1, null);

        act.Should().Throw<ArgumentOutOfRangeException>("TRN-10");
    }

    [Fact]
    public void A_contract_covers_one_to_three_game_seasons()
    {
        var contract = PlayerContract.Sign(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            startSeasonNumber: 1, endSeasonNumber: 3, weeklyWageMinor: 5_000,
            SquadStatus.FirstTeam, Now);

        contract.IsActive.Should().BeTrue("SQ-6");
        contract.CreatedAt.Should().Be(Now);

        var tooShort = () => PlayerContract.Sign(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 1, 0, SquadStatus.Rotation, Now);
        var tooLong = () => PlayerContract.Sign(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 5, 0, SquadStatus.Rotation, Now);

        tooShort.Should().Throw<ArgumentOutOfRangeException>("CON-1: a term never ends before it starts");
        tooLong.Should().Throw<ArgumentOutOfRangeException>("CON-1: a contract is at most three seasons");
    }

    [Fact]
    public void Closing_a_contract_records_a_known_reason_once()
    {
        var contract = PlayerContract.Sign(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, 1_000, SquadStatus.Prospect, Now);

        contract.Close(PlayerContractCloseReasons.Transferred, Now.AddDays(1));

        contract.Status.Should().Be(ContractStatus.Closed);
        contract.ClosedReason.Should().Be(PlayerContractCloseReasons.Transferred);
        contract.IsActive.Should().BeFalse();

        var again = () => contract.Close(PlayerContractCloseReasons.Released, Now.AddDays(2));
        var unknown = () => PlayerContract.Sign(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, 1_000, SquadStatus.Prospect, Now)
            .Close("vanished", Now);

        again.Should().Throw<InvalidOperationException>();
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_registration_records_its_fixture_boundary_and_ends_once()
    {
        var registration = PlayerRegistration.Register(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            effectiveFixtureBoundaryRound: 7, Now);

        registration.IsActive.Should().BeTrue("SQ-6");
        registration.EffectiveFixtureBoundaryRound.Should().Be(7);

        registration.End(Now.AddDays(1));
        registration.Status.Should().Be(RegistrationStatus.Ended);

        var again = () => registration.End(Now.AddDays(2));
        var negative = () => PlayerRegistration.Register(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), -1, Now);

        again.Should().Throw<InvalidOperationException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Player GeneratedPlayer() =>
        Player.Generate(Guid.CreateVersion7(), Guid.CreateVersion7(), Identity(), Now);

    private static PlayerIdentity Identity() => new(
        "Corin Alderwick",
        "C. Alderwick",
        "eng",
        "name-seed",
        BirthGameYear: 2000,
        BirthDayOfYear: 100,
        PreferredFoot.Right,
        HeightCm: 178,
        WeightKg: 74,
        PlayerPosition.RightBack,
        [PlayerPosition.CentreBack],
        Potential: 15,
        Reputation: 12);

    private static PlayerAttributeSet Attributes() => PlayerAttributeSet.FromValues(
        [.. Enumerable.Repeat(10, AttributeNames.Count)]);
}
