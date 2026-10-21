using FluentAssertions;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Tests.Squad.Generation;

/// <summary>
/// The potential draw (`TRN-18`): a ceiling around the tier's mean, with a tail of talents and gems.
/// </summary>
public sealed class GeneratedPotentialTests
{
    private const int Draws = 10_000;

    [Fact]
    public void Gems_and_talents_make_up_their_shares_of_a_tier()
    {
        var offsets = Offsets(tier: 1);

        Share(offsets, offset => offset >= 6).Should().BeApproximately(5.0, 1.5, "gems are 5% (TRN-18)");
        Share(offsets, offset => offset is >= 3 and <= 5).Should().BeApproximately(15.0, 2.5, "talents are 15% (TRN-18)");
        Share(offsets, offset => offset <= 2).Should().BeApproximately(80.0, 3.0, "the rest are normal (TRN-18)");
    }

    [Fact]
    public void Each_band_draws_inside_its_own_range_and_the_bands_do_not_overlap()
    {
        var offsets = Offsets(tier: 1);

        offsets.Min().Should().Be(-4);
        offsets.Max().Should().Be(7);

        // 80% + 15% + 5% is the whole draw, so a value in a gap would be a value in no band.
        offsets.Should().OnlyContain(offset => offset <= 2 || offset >= 3, "no offset sits between the bands");
        offsets.Where(offset => offset is >= 3 and <= 5).Distinct().Order().Should().Equal(3, 4, 5);
        offsets.Where(offset => offset >= 6).Distinct().Order().Should().Equal(6, 7);
        offsets.Where(offset => offset <= 2).Distinct().Order().Should().Equal(-4, -3, -2, -1, 0, 1, 2);
    }

    [Fact]
    public void The_most_common_normal_offset_is_one_below_the_tier_mean()
    {
        var mode = Offsets(tier: 1)
            .Where(offset => offset <= 2)
            .GroupBy(offset => offset)
            .OrderByDescending(group => group.Count())
            .First()
            .Key;

        mode.Should().Be(-1, "the normal band sits one point low to pay for the tail");
    }

    [Fact]
    public void The_mean_offset_is_close_to_the_tier_mean_so_squad_strength_is_kept()
    {
        Offsets(tier: 1).Average().Should().BeApproximately(0.1, 0.2);
    }

    [Fact]
    public void A_deeper_tier_lowers_potential_by_the_ability_drop_per_tier()
    {
        double MeanPotential(int tier) => Potentials(tier).Average();

        (MeanPotential(1) - MeanPotential(2)).Should().Be(WorldRuleSet.GeneratedAbilityDropPerTier);
        (MeanPotential(2) - MeanPotential(3)).Should().Be(WorldRuleSet.GeneratedAbilityDropPerTier);
    }

    [Fact]
    public void Every_potential_is_on_the_displayed_scale_even_where_the_floor_clamps()
    {
        for (var tier = 1; tier <= 10; tier++)
        {
            Potentials(tier).Should().OnlyContain(
                potential => potential >= WorldRuleSet.AttributeMin && potential <= WorldRuleSet.AttributeMax,
                $"tier {tier}");
        }
    }

    [Fact]
    public void A_youth_draws_the_same_bands_around_the_youth_mean()
    {
        var offsets = YouthPotentials(tier: 1)
            .Select(potential => potential - WorldRuleSet.YouthPotentialMeanForTier(1))
            .ToList();

        Share(offsets, offset => offset >= 6).Should().BeApproximately(5.0, 1.5, "gems are 5% (TRN-18)");
        Share(offsets, offset => offset is >= 3 and <= 5).Should().BeApproximately(15.0, 2.5, "talents are 15% (TRN-18)");
        Share(offsets, offset => offset <= 2).Should().BeApproximately(80.0, 3.0, "the rest are normal (TRN-18)");
        offsets.Min().Should().Be(-4);
        offsets.Max().Should().Be(7);
    }

    [Fact]
    public void A_tier_one_youth_gem_can_reach_the_top_of_the_scale()
    {
        YouthPotentials(tier: 1).Max().Should().Be(WorldRuleSet.AttributeMax);
    }

    [Fact]
    public void A_youth_intakes_mean_falls_by_the_youth_drop_per_tier_and_stays_on_the_scale()
    {
        (YouthPotentials(1).Average() - YouthPotentials(2).Average())
            .Should().Be(WorldRuleSet.YouthPotentialDropPerTier);

        for (var tier = 1; tier <= 10; tier++)
        {
            YouthPotentials(tier).Should().OnlyContain(
                potential => potential >= WorldRuleSet.AttributeMin && potential <= WorldRuleSet.AttributeMax,
                $"tier {tier}");
        }
    }

    private static List<int> YouthPotentials(int tier)
    {
        var rng = new Pcg32(DeterministicDigest.SeedOf("youth-potential-draw"));

        return [.. Enumerable.Range(0, Draws).Select(_ => GeneratedPotential.DrawYouth(tier, rng))];
    }

    private static List<int> Potentials(int tier)
    {
        var rng = new Pcg32(DeterministicDigest.SeedOf("potential-draw"));

        return [.. Enumerable.Range(0, Draws).Select(_ => GeneratedPotential.Draw(tier, rng))];
    }

    private static List<int> Offsets(int tier)
    {
        var tierMean = WorldRuleSet.GeneratedAbilityMeanForTier(tier);

        return [.. Potentials(tier).Select(potential => potential - tierMean)];
    }

    private static double Share(List<int> offsets, Func<int, bool> inBand) =>
        100.0 * offsets.Count(inBand) / offsets.Count;
}
