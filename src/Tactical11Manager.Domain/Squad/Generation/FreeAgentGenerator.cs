using System.Globalization;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Squad.Generation;

/// <summary>
/// One country's share of the players under contract, which is what its share of a weekly batch follows.
/// </summary>
/// <param name="CountryCode">The country code, which is also the generated players' nationality.</param>
/// <param name="NamePoolKey">The country's name pool key.</param>
/// <param name="ContractedPlayers">The players under active contract in the country's clubs.</param>
public sealed record FreeAgentCountry(string CountryCode, string NamePoolKey, int ContractedPlayers);

/// <summary>Everything one weekly batch is generated from, so the generator has no ambient state.</summary>
/// <param name="WorldSeed">The world's generation seed (`PYR-14`).</param>
/// <param name="WorldId">The owning world. Identity only: it never affects a generated value.</param>
/// <param name="WeekStart">The Monday the batch opens on, which is part of every player's seed.</param>
/// <param name="GameYear">The season's game year, which fixes each player's age.</param>
/// <param name="Countries">The countries, with the players they have under contract.</param>
/// <param name="Now">The current instant.</param>
public sealed record FreeAgentBatchRequest(
    string WorldSeed,
    Guid WorldId,
    DateOnly WeekStart,
    int GameYear,
    IReadOnlyList<FreeAgentCountry> Countries,
    DateTimeOffset Now);

/// <summary>The positions one country contributes to a batch, in the order its players are drawn.</summary>
/// <param name="Country">The country.</param>
/// <param name="Positions">One position per player. Its length is the country's share of the batch.</param>
public sealed record FreeAgentCountryPlan(FreeAgentCountry Country, IReadOnlyList<PlayerPosition> Positions);

/// <summary>
/// One generated free agent and the three rows that describe him. There is no contract or registration: he
/// belongs to no club until somebody signs him.
/// </summary>
/// <param name="Player">The person, already in <see cref="PlayerStatus.FreeAgent"/>.</param>
/// <param name="Attributes">His attributes.</param>
/// <param name="State">His opening condition, fatigue, morale, sharpness and experience.</param>
/// <param name="Ability">The ability he shows: the rounded mean of his attributes.</param>
/// <param name="IsGem">
/// Whether he is a hidden gem. Server-only (class C2): it is counted on the batch row and never leaves the
/// server, and nothing about his price or terms reads it.
/// </param>
public sealed record GeneratedFreeAgent(
    Player Player,
    PlayerAttributes Attributes,
    PlayerState State,
    int Ability,
    bool IsGem);

/// <summary>
/// Generates the players of one weekly free-agent batch deterministically from the world seed (`FA-1`…`FA-4`).
/// </summary>
/// <remarks>
/// <para>
/// A batch is a pure function of the world seed, the Monday it opens on, and the country shares, so a retried
/// batch reproduces the same players. Every player has his own stream, seeded from the seed, the week, his
/// country and his ordinal, so one player's draws cannot move another's.
/// </para>
/// <para>
/// The profile is deliberately plain: ability 6 to 10, most often 8, as squad depth. One in a hundred is a
/// hidden gem, aged 17 to 21, who shows the same ability as everyone else and has a high ceiling. Skills come
/// from the squad generator's own draw (<see cref="PlayerGenerator.Draft"/>), so a free agent is shaped by his
/// position like any other player, then every attribute is moved together so the ability he shows is the one
/// drawn.
/// </para>
/// </remarks>
public static class FreeAgentGenerator
{
    /// <summary>The generator version stamped onto every batch (`FIC-8`).</summary>
    /// <remarks>Bump this whenever the profile, a share, or the draw order changes.</remarks>
    public const string Version = "free-agent-gen-v1";

    /// <summary>Each family's positions in the order a country's players are dealt them (`FA-2`).</summary>
    /// <remarks>
    /// The squad generator's own mix (defence 2 left backs, 3 centre backs, 2 right backs; midfield 2 defensive,
    /// 3 central, 2 attacking, 1 left, 1 right; attack 1 left winger, 1 right winger, 3 strikers), interleaved so
    /// a short run still has full-backs and wingers.
    /// </remarks>
    private static readonly Dictionary<PositionFamily, PlayerPosition[]> FamilyCycle =
        new Dictionary<PositionFamily, PlayerPosition[]>
        {
            [PositionFamily.Goalkeeper] = [PlayerPosition.Goalkeeper],
            [PositionFamily.Defence] =
            [
                PlayerPosition.CentreBack,
                PlayerPosition.LeftBack,
                PlayerPosition.RightBack,
                PlayerPosition.CentreBack,
                PlayerPosition.LeftBack,
                PlayerPosition.RightBack,
                PlayerPosition.CentreBack,
            ],
            [PositionFamily.Midfield] =
            [
                PlayerPosition.CentralMidfielder,
                PlayerPosition.DefensiveMidfielder,
                PlayerPosition.AttackingMidfielder,
                PlayerPosition.CentralMidfielder,
                PlayerPosition.LeftMidfielder,
                PlayerPosition.RightMidfielder,
                PlayerPosition.DefensiveMidfielder,
                PlayerPosition.AttackingMidfielder,
                PlayerPosition.CentralMidfielder,
            ],
            [PositionFamily.Attack] =
            [
                PlayerPosition.Striker,
                PlayerPosition.LeftWinger,
                PlayerPosition.RightWinger,
                PlayerPosition.Striker,
                PlayerPosition.Striker,
            ],
        };

    private static readonly PositionFamily[] Families =
    [
        PositionFamily.Goalkeeper,
        PositionFamily.Defence,
        PositionFamily.Midfield,
        PositionFamily.Attack,
    ];

    /// <summary>The number of generated players in a batch for a given number of players under contract (`FA-1`).</summary>
    /// <param name="contractedPlayers">The players under active contract.</param>
    public static int BatchSize(int contractedPlayers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contractedPlayers);

        return (int)(((long)contractedPlayers * WorldRuleSet.FreeAgentShareBp + 5_000) / 10_000);
    }

    /// <summary>
    /// Splits a batch across the countries, and each country's share across the four position families, so
    /// every count is whole and the counts sum exactly (`FA-2`).
    /// </summary>
    /// <param name="batchSize">The number of players to generate.</param>
    /// <param name="countries">The countries, in a caller-fixed order.</param>
    public static IReadOnlyList<FreeAgentCountryPlan> Plan(int batchSize, IReadOnlyList<FreeAgentCountry> countries)
    {
        ArgumentNullException.ThrowIfNull(countries);
        ArgumentOutOfRangeException.ThrowIfNegative(batchSize);

        var perCountry = LargestRemainder(batchSize, [.. countries.Select(country => (long)country.ContractedPlayers)]);
        var familyShares = Families.Select(family => (long)WorldRuleSet.FreeAgentFamilyShareBp(family)).ToArray();
        var plans = new List<FreeAgentCountryPlan>(countries.Count);

        for (var index = 0; index < countries.Count; index++)
        {
            var perFamily = LargestRemainder(perCountry[index], familyShares);
            var positions = new List<PlayerPosition>(perCountry[index]);

            for (var family = 0; family < Families.Length; family++)
            {
                var cycle = FamilyCycle[Families[family]];

                for (var dealt = 0; dealt < perFamily[family]; dealt++)
                {
                    positions.Add(cycle[dealt % cycle.Length]);
                }
            }

            plans.Add(new FreeAgentCountryPlan(countries[index], positions));
        }

        return plans;
    }

    /// <summary>Generates one weekly batch (`FA-1`…`FA-4`).</summary>
    /// <param name="request">The generation inputs.</param>
    public static IReadOnlyList<GeneratedFreeAgent> Generate(FreeAgentBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorldSeed);
        ArgumentNullException.ThrowIfNull(request.Countries);

        var size = BatchSize(request.Countries.Sum(country => country.ContractedPlayers));
        var week = request.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var generated = new List<GeneratedFreeAgent>(size);

        foreach (var plan in Plan(size, request.Countries))
        {
            var pool = PlayerNamePools.For(plan.Country.NamePoolKey);

            for (var ordinal = 0; ordinal < plan.Positions.Count; ordinal++)
            {
                generated.Add(DrawOne(request, plan.Country, pool, week, ordinal, plan.Positions[ordinal]));
            }
        }

        return generated;
    }

    private static GeneratedFreeAgent DrawOne(
        FreeAgentBatchRequest request,
        FreeAgentCountry country,
        PlayerNamePool pool,
        string week,
        int ordinal,
        PlayerPosition position)
    {
        var ordinalText = ordinal.ToString(CultureInfo.InvariantCulture);

        var rng = new Pcg32(DeterministicDigest.SeedOf(
            request.WorldSeed,
            "free-agents",
            week,
            country.CountryCode,
            ordinalText));

        var isGem = rng.NextInt(1_000) < WorldRuleSet.FreeAgentGemPermille;

        // Two three-faced dice: 6 to 10, with 8 the commonest.
        var ability = WorldRuleSet.FreeAgentAbilityMin + rng.NextInt(3) + rng.NextInt(3);

        int age;
        int potential;

        if (isGem)
        {
            age = WorldRuleSet.PlayerMinimumAge + rng.NextInt(WorldRuleSet.FreeAgentGemAgeMax - WorldRuleSet.PlayerMinimumAge + 1);
            potential = WorldRuleSet.FreeAgentGemPotentialMin
                + rng.NextInt(WorldRuleSet.FreeAgentGemPotentialMax - WorldRuleSet.FreeAgentGemPotentialMin + 1);
        }
        else
        {
            var ageSpan = WorldRuleSet.FreeAgentAgeMax - WorldRuleSet.FreeAgentAgeMin + 1;
            age = WorldRuleSet.FreeAgentAgeMin + ((rng.NextInt(ageSpan) + rng.NextInt(ageSpan)) / 2);

            var headroom = age <= WorldRuleSet.FreeAgentHeadroomAgeMax
                ? rng.NextInt(WorldRuleSet.FreeAgentHeadroomMax + 1)
                : 0;

            potential = Math.Min(WorldRuleSet.FreeAgentPotentialCap, ability + headroom);
        }

        var nameOrdinal = (int)(DeterministicDigest.SeedOf(
            request.WorldSeed,
            "free-agent-name",
            week,
            country.CountryCode,
            ordinalText) % 1_000_000);

        // The squad generator reads the club-shaped fields of the request for a squad only: the seed folds into
        // the stored name seed, the country is the nationality, and the game year fixes the age.
        var draftRequest = new SquadGenerationRequest(
            string.Join('|', request.WorldSeed, "free-agents", week),
            country.NamePoolKey,
            country.CountryCode,
            request.WorldId,
            ClubId: Guid.Empty,
            ClubOrdinalInCountry: ordinal,
            Tier: 1,
            SeasonId: Guid.Empty,
            SeasonNumber: 1,
            request.GameYear,
            request.Now);

        var draft = PlayerGenerator.Draft(
            draftRequest,
            pool,
            rng,
            position,
            nameOrdinal,
            new PlayerGenerator.DraftOverride(age, potential, ability));

        var playerId = Guid.CreateVersion7();
        var player = Player.Generate(playerId, request.WorldId, draft.Identity, request.Now);

        player.ReleaseToFreeAgency(request.Now);

        return new GeneratedFreeAgent(
            player,
            PlayerAttributes.Create(playerId, draft.Attributes),
            PlayerState.Open(playerId, draft.ExperienceBp),
            draft.Mean,
            isGem);
    }

    /// <summary>
    /// Shares a total across weights in whole numbers that sum to the total exactly: each gets its floor, and
    /// the leftovers go to the largest remainders, the earlier index first on a tie.
    /// </summary>
    internal static int[] LargestRemainder(int total, IReadOnlyList<long> weights)
    {
        var counts = new int[weights.Count];
        var weightSum = weights.Sum();

        if (total == 0 || weightSum == 0)
        {
            return counts;
        }

        var remainders = new long[weights.Count];
        var assigned = 0;

        for (var index = 0; index < weights.Count; index++)
        {
            var scaled = (long)total * weights[index];

            counts[index] = (int)(scaled / weightSum);
            remainders[index] = scaled % weightSum;
            assigned += counts[index];
        }

        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(index => remainders[index])
            .ThenBy(index => index)
            .ToList();

        for (var leftover = 0; leftover < total - assigned; leftover++)
        {
            counts[order[leftover]]++;
        }

        return counts;
    }
}
