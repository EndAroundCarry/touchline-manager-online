using FluentAssertions;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Tests.Fans;

/// <summary>
/// How strong the fans think a team is (`FAN-3`): the mean ability of the eleven the AI ordering would field.
/// </summary>
public sealed class TeamStrengthTests
{
    /// <summary>Two goalkeepers and nineteen outfielders, enough for a 4-4-2 with a bench.</summary>
    private static readonly PlayerPosition[] Composition =
    [
        PlayerPosition.Goalkeeper,
        PlayerPosition.Goalkeeper,
        PlayerPosition.RightBack,
        PlayerPosition.CentreBack,
        PlayerPosition.CentreBack,
        PlayerPosition.LeftBack,
        PlayerPosition.RightMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.LeftMidfielder,
        PlayerPosition.Striker,
        PlayerPosition.Striker,
        PlayerPosition.CentreBack,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.Striker,
        PlayerPosition.RightBack,
        PlayerPosition.LeftBack,
        PlayerPosition.RightWinger,
        PlayerPosition.LeftWinger,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.AttackingMidfielder,
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    [InlineData(20)]
    public void A_side_whose_every_attribute_is_the_same_has_that_strength_whatever_the_shape(int value)
    {
        var squad = Squad(_ => Uniform(value));

        foreach (var preset in FormationPresets.All)
        {
            TeamStrength.OfSquad(squad, FormationLayouts.DefaultSlots(preset)).Should().Be(value * 100, preset.ToString());
        }
    }

    [Fact]
    public void A_goalkeeper_is_judged_by_his_goalkeeping_and_an_outfielder_by_everything_else()
    {
        var attributes = Uniform(5);

        attributes[(int)AttributeName.Handling] = 17;
        attributes[(int)AttributeName.Reflexes] = 18;
        attributes[(int)AttributeName.OneOnOnes] = 16;
        attributes[(int)AttributeName.AerialAbility] = 17;

        TeamStrength.AbilityOf(attributes, goalkeeper: true).Should().Be(1_700);
        TeamStrength.AbilityOf(attributes, goalkeeper: false).Should().Be(500);
    }

    [Fact]
    public void An_ability_is_rounded_to_the_nearest_hundredth()
    {
        var attributes = Uniform(10);

        // 24 outfield attributes: one 11 makes 241 / 24 = 10.0417 -> 1,004; three make 243 / 24 = 10.125 -> 1,012.5, which rounds up to 1,013.
        attributes[0] = 11;
        TeamStrength.AbilityOf(attributes, goalkeeper: false).Should().Be(1_004);

        attributes[1] = 11;
        attributes[2] = 11;
        TeamStrength.AbilityOf(attributes, goalkeeper: false).Should().Be(1_013);
    }

    [Fact]
    public void A_list_that_is_not_twenty_eight_attributes_has_no_ability()
    {
        TeamStrength.AbilityOf([], goalkeeper: false).Should().Be(0);
        TeamStrength.AbilityOf([.. Enumerable.Repeat(10, 27)], goalkeeper: true).Should().Be(0);
    }

    [Fact]
    public void An_eleven_is_the_mean_of_its_abilities_and_an_empty_one_is_nothing()
    {
        TeamStrength.OfEleven([1_000, 1_100, 1_201]).Should().Be(1_100);
        TeamStrength.OfEleven([]).Should().Be(0);
    }

    [Fact]
    public void The_squad_is_read_as_the_eleven_the_AI_ordering_would_field()
    {
        // Two right backs (ordinals 3 and 16) tie on suitability and condition, so the ordering takes the better one; the other nine
        // outfielders and the keeper are all 10. The reserve right back's 18 therefore plays and the others' do not matter.
        var squad = Squad(ordinal => ordinal == 16 ? Uniform(18) : Uniform(10));
        var layout = FormationLayouts.DefaultSlots(FormationPreset.FourFourTwo);

        TeamStrength.OfSquad(squad, layout).Should().Be(1_073, "(10 x 10.00 + 18.00) / 11 = 10.727");
        TeamStrength.OfSquad(squad).Should().Be(1_073, "the default shape is the 4-4-2");
    }

    [Fact]
    public void An_unavailable_player_does_not_count_and_the_order_of_the_list_does_not_matter()
    {
        var healthy = Squad(ordinal => ordinal == 6 ? Uniform(20) : Uniform(10));
        var injured = healthy.Select(player => player.PlayerId == PlayerId(6) ? player with { IsAvailable = false } : player).ToList();
        var reversed = Enumerable.Reverse(healthy).ToList();

        TeamStrength.OfSquad(injured).Should().BeLessThan(TeamStrength.OfSquad(healthy));
        TeamStrength.OfSquad(reversed).Should().Be(TeamStrength.OfSquad(healthy));
    }

    [Fact]
    public void A_squad_that_cannot_fill_the_shape_is_still_read_by_its_best_players()
    {
        var withoutKeepers = Squad(_ => Uniform(12)).Where(player => player.PrimaryPosition != PlayerPosition.Goalkeeper).ToList();

        // Ten outfielders and no keeper: the mean of the ten best, not zero and not an exception.
        TeamStrength.OfSquad(withoutKeepers).Should().Be(1_200);
        TeamStrength.OfSquad([]).Should().Be(0);
    }

    private static int[] Uniform(int value) => [.. Enumerable.Repeat(value, AttributeNames.Count)];

    private static Guid PlayerId(int ordinal) => Guid.Parse($"0192f300-0000-7000-8000-{ordinal:D12}");

    private static List<AiSquadPlayer> Squad(Func<int, int[]> attributesOf) =>
        [.. Composition.Select((position, index) => new AiSquadPlayer(PlayerId(index + 1), position, [], attributesOf(index + 1), 10_000, IsAvailable: true))];
}
