using System.Globalization;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Squad.Generation;

/// <summary>How a template squad's player is labelled by his hidden potential.</summary>
public enum PotentialKind
{
    /// <summary>A player with the room to grow his age gives him and no more.</summary>
    Normal,

    /// <summary>One of the three players per squad who can climb well above his rating.</summary>
    Talent,

    /// <summary>The one player per squad who can climb furthest.</summary>
    Gem,
}

/// <summary>
/// One slot of a club's template squad, fully decided before any attribute is drawn.
/// </summary>
/// <param name="Position">The slot's primary position.</param>
/// <param name="Rating">The ability the player shows: the rounded mean of his attributes.</param>
/// <param name="Age">His age in game years.</param>
/// <param name="Potential">His hidden potential, on the 1–20 scale.</param>
/// <param name="Kind">Whether he is one of the squad's talents or its gem.</param>
public sealed record TemplateSlot(PlayerPosition Position, int Rating, int Age, int Potential, PotentialKind Kind);

/// <summary>
/// The rating of every slot of a fair seeded squad (plan cheeky-dongarra): every club of a tier opens with the same
/// mix of abilities, so no club starts the world noticeably stronger than another.
/// </summary>
/// <remarks>
/// <para>
/// A rating is the displayed ability, the rounded mean of a player's 28 attributes. Tier 1 is rated 8 to 12 (three
/// 12s, four 11s, five 10s, six 9s and six 8s) and tier 2 is rated 8 to 10 (three 10s, nine 9s and twelve 8s). The
/// best players sit across the spine of the team, so every first eleven is comparable. Tier 3 is tier 2's mix shifted
/// down one rating (7 to 9) and tier 4 shifted down two (three 8s, nine 7s and twelve 6s). Each tier below that has one
/// 8 fewer than the one above, the best slot in composition order dropping to 7; once no 8s are left, one 7 a tier
/// drops to 6, until every slot is <see cref="RatingFloor"/> (`PYR-15`).
/// </para>
/// <para>
/// A little diversity is seeded per club: a coin decides whether the left and right sides are mirrored, and
/// <see cref="NudgeCount"/> slots move one rating, half of them up and half down, always inside the tier's band. In tier 4
/// and below the nudges only trade a 6 for a 7, so a club never makes or loses an 8 and keeps exactly its tier's count.
/// Each club also gets
/// exactly <see cref="GemsPerSquad"/> gem and <see cref="TalentsPerSquad"/> talents, picked among its youngest players.
/// Everything is a pure function of the seed, the country, the club's ordinal and the tier.
/// </para>
/// </remarks>
public static class SquadTemplate
{
    /// <summary>The template version, recorded with the generator version when a world is seeded (`PYR-14`).</summary>
    public const string Version = "squad-template-v2";

    /// <summary>How many slots of a club move one rating up or down.</summary>
    public const int NudgeCount = 4;

    /// <summary>How many talents each squad has.</summary>
    public const int TalentsPerSquad = 3;

    /// <summary>How many gems each squad has.</summary>
    public const int GemsPerSquad = 1;

    /// <summary>The lowest rating a template gives anywhere.</summary>
    public const int RatingFloor = 6;

    /// <summary>The first tier whose mix is built by dropping 8s and then 7s one a tier, rather than shifted whole.</summary>
    private const int FirstThinningTier = 4;

    /// <summary>The highest rating a nudge may give in the thinning tiers, so none of them gains an 8.</summary>
    private const int ThinningNudgeCeiling = 7;

    /// <summary>The oldest age a player can be and still be picked as a talent or a gem when enough younger players exist.</summary>
    public const int YoungestPickAge = 23;

    /// <summary>The age to which a player is still expected to grow, one potential point for two years short of it.</summary>
    private const int GrowthAge = 24;

    private const int TierOneBandLow = 8;
    private const int TierOneBandHigh = 12;
    private const int TierTwoBandLow = 8;
    private const int TierTwoBandHigh = 10;

    /// <summary>Tier 1 ratings, in the order of <see cref="PlayerGenerator.Composition"/>.</summary>
    private static readonly int[] TierOne =
    [
        11, 9, 8,       // goalkeepers
        10, 8,          // left backs
        12, 10, 9,      // centre backs
        11, 8,          // right backs
        10, 9,          // defensive midfielders
        12, 10, 8,      // central midfielders
        11, 8,          // attacking midfielders
        9,              // left midfielder
        9,              // right midfielder
        9,              // left winger
        10,             // right winger
        12, 11, 8,      // strikers
    ];

    /// <summary>Tier 2 ratings, in the order of <see cref="PlayerGenerator.Composition"/>.</summary>
    private static readonly int[] TierTwo =
    [
        9, 8, 8,
        9, 8,
        10, 9, 8,
        9, 8,
        9, 8,
        10, 9, 8,
        9, 8,
        8,
        8,
        8,
        9,
        10, 9, 8,
    ];

    /// <summary>The slot pairs a mirrored club swaps, left side first.</summary>
    private static readonly (PlayerPosition Left, PlayerPosition Right)[] Flanks =
    [
        (PlayerPosition.LeftBack, PlayerPosition.RightBack),
        (PlayerPosition.LeftMidfielder, PlayerPosition.RightMidfielder),
        (PlayerPosition.LeftWinger, PlayerPosition.RightWinger),
    ];

    /// <summary>The lowest and highest rating a club of a tier can have in any slot.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static (int Low, int High) BandFor(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        if (tier == 1)
        {
            return (TierOneBandLow, TierOneBandHigh);
        }

        if (tier >= FirstThinningTier)
        {
            return (RatingFloor, BaseRatingsFor(tier).Max());
        }

        var shift = tier - 2;

        return (TierTwoBandLow - shift, TierTwoBandHigh - shift);
    }

    /// <summary>The template ratings of a tier before any club's diversity, in composition order.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static IReadOnlyList<int> BaseRatingsFor(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        if (tier == 1)
        {
            return TierOne;
        }

        if (tier < FirstThinningTier)
        {
            return [.. TierTwo.Select(rating => rating - (tier - 2))];
        }

        var ratings = TierTwo.Select(rating => rating - 2).ToArray();

        // One step per tier below the fourth: the best slot still rated 8 drops to 7, and once none is left the best
        // slot still rated 7 drops to 6. "Best" is the first in composition order, which lists each line's strongest
        // player first, so every tier drops the same slots.
        for (var step = FirstThinningTier; step < tier; step++)
        {
            var slot = Array.IndexOf(ratings, 8);

            if (slot < 0)
            {
                slot = Array.IndexOf(ratings, 7);
            }

            if (slot < 0)
            {
                break;
            }

            ratings[slot]--;
        }

        return ratings;
    }

    /// <summary>
    /// The ratings an emergency replacement is drawn from: the bottom of the tier's band (8 or 9 in tier 1, the lowest in a deeper
    /// tier), so a repair never hands a club a better player than its worst template slots.
    /// </summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static IReadOnlyList<int> ReplacementRatingsFor(int tier)
    {
        var (low, _) = BandFor(tier);

        return tier == 1 ? [low, low + 1] : [low];
    }

    /// <summary>The potential a player of a given rating and age has when he is neither a talent nor a gem.</summary>
    /// <param name="rating">The ability he shows.</param>
    /// <param name="age">His age in game years.</param>
    public static int NormalPotential(int rating, int age) =>
        Math.Min(WorldRuleSet.AttributeMax, rating + ((Math.Max(0, GrowthAge - age) + 1) / 2));

    /// <summary>Decides every slot of one club's squad: its rating, age, potential and whether it is a talent or the gem.</summary>
    /// <param name="seed">The world's generation seed (`PYR-14`).</param>
    /// <param name="countryCode">The club's country code.</param>
    /// <param name="clubOrdinal">The club's ordinal within its country.</param>
    /// <param name="tier">The tier the club plays in.</param>
    /// <returns>One slot per entry of the generator's composition, in the same order.</returns>
    public static IReadOnlyList<TemplateSlot> PlanFor(string seed, string countryCode, int clubOrdinal, int tier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentOutOfRangeException.ThrowIfNegative(clubOrdinal);

        var rng = new Pcg32(DeterministicDigest.SeedOf(
            seed,
            countryCode,
            "squad-template",
            clubOrdinal.ToString(CultureInfo.InvariantCulture)));

        var composition = PlayerGenerator.Composition;
        var ratings = BaseRatingsFor(tier).ToArray();
        var (low, high) = BandFor(tier);

        if (rng.NextInt(2) == 1)
        {
            Mirror(composition, ratings);
        }

        // Half the nudges raise a slot and half lower another, so a club's total ability stays the template's and only its
        // shape moves. From tier 4 down the nudges stay between 6 and 7, so a club never makes or loses an 8 and keeps
        // exactly its tier's count of them.
        var ceiling = tier >= FirstThinningTier ? Math.Min(high, ThinningNudgeCeiling) : high;
        var order = Shuffled(composition.Length, rng);
        var raised = order.Where(slot => ratings[slot] < ceiling).Take(NudgeCount / 2).ToList();
        var lowered = order.Where(slot => ratings[slot] > low && ratings[slot] <= ceiling && !raised.Contains(slot)).Take(NudgeCount / 2).ToList();
        var pairs = Math.Min(raised.Count, lowered.Count);

        raised = raised[..pairs];
        lowered = lowered[..pairs];

        foreach (var slot in raised)
        {
            ratings[slot]++;
        }

        foreach (var slot in lowered)
        {
            ratings[slot]--;
        }

        var ageSpan = WorldRuleSet.PlayerMaximumAge - WorldRuleSet.PlayerMinimumAge + 1;
        var ages = new int[composition.Length];

        for (var index = 0; index < ages.Length; index++)
        {
            ages[index] = WorldRuleSet.PlayerMinimumAge + ((rng.NextInt(ageSpan) + rng.NextInt(ageSpan)) / 2);
        }

        var potentials = ratings.Select((rating, index) => NormalPotential(rating, ages[index])).ToArray();
        var kinds = new PotentialKind[composition.Length];
        var chosen = Shuffled(composition.Length, rng).ToList();
        var young = chosen.Where(index => ages[index] <= YoungestPickAge).ToList();

        if (young.Count < GemsPerSquad + TalentsPerSquad)
        {
            young = [.. Enumerable.Range(0, composition.Length)
                .OrderBy(index => ages[index])
                .ThenBy(index => index)
                .Take(GemsPerSquad + TalentsPerSquad)
                .OrderBy(index => chosen.IndexOf(index))];
        }

        // A talent or a gem always climbs above what his age alone would give him, so the pick is never a no-op.
        for (var pick = 0; pick < GemsPerSquad + TalentsPerSquad; pick++)
        {
            var index = young[pick];
            var normal = potentials[index];

            if (pick < GemsPerSquad)
            {
                kinds[index] = PotentialKind.Gem;
                potentials[index] = Math.Min(
                    WorldRuleSet.AttributeMax,
                    Math.Max(
                        ratings[index] + WorldRuleSet.GeneratedPotentialGemOffsetMin + rng.NextInt(WorldRuleSet.GeneratedPotentialGemOffsetCount),
                        normal + 2));
            }
            else
            {
                kinds[index] = PotentialKind.Talent;
                potentials[index] = Math.Min(
                    WorldRuleSet.AttributeMax,
                    Math.Max(
                        ratings[index] + WorldRuleSet.GeneratedPotentialTalentOffsetMin + rng.NextInt(WorldRuleSet.GeneratedPotentialTalentOffsetCount),
                        normal + 1));
            }
        }

        return [.. composition.Select((position, index) => new TemplateSlot(position, ratings[index], ages[index], potentials[index], kinds[index]))];
    }

    private static void Mirror(PlayerPosition[] composition, int[] ratings)
    {
        foreach (var (left, right) in Flanks)
        {
            var leftSlots = IndexesOf(composition, left);
            var rightSlots = IndexesOf(composition, right);

            for (var pair = 0; pair < Math.Min(leftSlots.Count, rightSlots.Count); pair++)
            {
                (ratings[leftSlots[pair]], ratings[rightSlots[pair]]) = (ratings[rightSlots[pair]], ratings[leftSlots[pair]]);
            }
        }
    }

    private static List<int> IndexesOf(PlayerPosition[] composition, PlayerPosition position) =>
        [.. Enumerable.Range(0, composition.Length).Where(index => composition[index] == position)];

    private static int[] Shuffled(int count, Pcg32 rng)
    {
        var order = Enumerable.Range(0, count).ToArray();

        for (var index = order.Length - 1; index > 0; index--)
        {
            var other = rng.NextInt(index + 1);

            (order[index], order[other]) = (order[other], order[index]);
        }

        return order;
    }
}
