using System.Globalization;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Squad.Generation;

/// <summary>
/// Everything one club's squad generation needs, so the generator has no ambient state.
/// </summary>
/// <param name="Seed">The world's generation seed (`PYR-14`).</param>
/// <param name="NamePoolKey">The club's country name pool key.</param>
/// <param name="CountryCode">The club's country code, which is also the players' nationality at MVP.</param>
/// <param name="WorldId">The owning world. Identity only: it never affects a generated value.</param>
/// <param name="ClubId">The club the squad belongs to. Identity only: it never affects a generated value.</param>
/// <param name="ClubOrdinalInCountry">
/// The club's ordinal within its country, counted across every tier the country has generated. This —
/// not the club id — is what makes a squad reproducible, because ids are UUIDv7 and differ per run.
/// </param>
/// <param name="Tier">The tier the club plays in, which scales squad ability.</param>
/// <param name="SeasonId">The season the players are registered in.</param>
/// <param name="SeasonNumber">The season number contracts start from.</param>
/// <param name="GameYear">The season's game year, which fixes each player's age.</param>
/// <param name="Now">The current instant.</param>
public sealed record SquadGenerationRequest(
    string Seed,
    string NamePoolKey,
    string CountryCode,
    Guid WorldId,
    Guid ClubId,
    int ClubOrdinalInCountry,
    int Tier,
    Guid SeasonId,
    int SeasonNumber,
    int GameYear,
    DateTimeOffset Now);

/// <summary>
/// One generated player and the four rows that describe them.
/// </summary>
/// <param name="Player">The person.</param>
/// <param name="Attributes">Their attributes.</param>
/// <param name="State">Their opening condition, fatigue, morale, and sharpness.</param>
/// <param name="Contract">Their active contract (`SQ-6`).</param>
/// <param name="Registration">Their active registration (`SQ-6`).</param>
public sealed record GeneratedSquadMember(
    Player Player,
    PlayerAttributes Attributes,
    PlayerState State,
    PlayerContract Contract,
    PlayerRegistration Registration);

/// <summary>
/// Generates a club's senior squad deterministically from the world seed (`SQ-1`, `FIC-7`, `PYR-14`).
/// </summary>
/// <remarks>
/// <para>
/// Every value is a pure function of the seed, the club's ordinal within its country, its tier, and the
/// season's game year. Nothing reads the clock, a global random source, or the database, so the same
/// seed reproduces the same squads and a test can assert it without a database.
/// </para>
/// <para>
/// The positional composition is the split in <see cref="WorldRuleSet"/>, and the name pools are
/// per-locale and versioned like the club pools (`FIC-4`). A name that collides with the fictional-data
/// blocklist advances deterministically to the next candidate for the same seed (`FIC-6`), which is a
/// backstop for a future pool edit rather than something today's pools trigger.
/// </para>
/// <para>
/// Only identity is drawn from the pools; a player's id is a UUIDv7 minted here, exactly as the seeder
/// mints club ids. Ids are deliberately outside the reproducibility contract — the logical squad is
/// reproducible, the row identities are not.
/// </para>
/// </remarks>
public static class PlayerGenerator
{
    /// <summary>
    /// The generator version stamped onto every generation run (`FIC-8`, `PYR-14`).
    /// </summary>
    /// <remarks>
    /// Bump this whenever the composition, a profile, a pool, or the draw order changes. The same seed
    /// and version must reproduce the same squads; the same seed and a different version must not be
    /// expected to.
    /// </remarks>
    public const string Version = "player-gen-v1";

    /// <summary>
    /// The positional composition of a generated squad, ordered goalkeeper outwards. Its length is the
    /// squad target (`SQ-1`), and a test asserts the family counts match <see cref="WorldRuleSet"/>.
    /// </summary>
    private static readonly PlayerPosition[] Composition =
    [
        PlayerPosition.Goalkeeper,
        PlayerPosition.Goalkeeper,
        PlayerPosition.Goalkeeper,
        PlayerPosition.LeftBack,
        PlayerPosition.LeftBack,
        PlayerPosition.CentreBack,
        PlayerPosition.CentreBack,
        PlayerPosition.CentreBack,
        PlayerPosition.RightBack,
        PlayerPosition.RightBack,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.AttackingMidfielder,
        PlayerPosition.AttackingMidfielder,
        PlayerPosition.LeftWinger,
        PlayerPosition.RightWinger,
        PlayerPosition.Striker,
        PlayerPosition.Striker,
        PlayerPosition.Striker,
    ];

    /// <summary>The positions a player of each position can also cover, as secondary positions.</summary>
    private static readonly Dictionary<PlayerPosition, PlayerPosition[]> Adjacent =
        new Dictionary<PlayerPosition, PlayerPosition[]>
        {
            [PlayerPosition.Goalkeeper] = [],
            [PlayerPosition.RightBack] = [PlayerPosition.CentreBack, PlayerPosition.RightWinger],
            [PlayerPosition.LeftBack] = [PlayerPosition.CentreBack, PlayerPosition.LeftWinger],
            [PlayerPosition.CentreBack] = [PlayerPosition.DefensiveMidfielder, PlayerPosition.RightBack],
            [PlayerPosition.DefensiveMidfielder] = [PlayerPosition.CentralMidfielder, PlayerPosition.CentreBack],
            [PlayerPosition.CentralMidfielder] = [PlayerPosition.DefensiveMidfielder, PlayerPosition.AttackingMidfielder],
            [PlayerPosition.AttackingMidfielder] = [PlayerPosition.CentralMidfielder, PlayerPosition.Striker],
            [PlayerPosition.RightWinger] = [PlayerPosition.AttackingMidfielder, PlayerPosition.RightBack],
            [PlayerPosition.LeftWinger] = [PlayerPosition.AttackingMidfielder, PlayerPosition.LeftBack],
            [PlayerPosition.Striker] = [PlayerPosition.AttackingMidfielder, PlayerPosition.RightWinger],
        };

    /// <summary>Generates one club's senior squad.</summary>
    /// <param name="request">The generation inputs.</param>
    /// <returns>The generated players, goalkeepers first, in composition order.</returns>
    public static IReadOnlyList<GeneratedSquadMember> GenerateSquad(SquadGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CountryCode);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ClubOrdinalInCountry);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Tier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.SeasonNumber, 1);

        var pool = PlayerNamePools.For(request.NamePoolKey);
        var rng = new Pcg32(DeterministicDigest.SeedOf(
            request.Seed,
            request.CountryCode,
            "squad",
            request.ClubOrdinalInCountry.ToString(CultureInfo.InvariantCulture)));

        var drafts = new List<SquadDraft>(Composition.Length);

        for (var index = 0; index < Composition.Length; index++)
        {
            drafts.Add(Draft(request, pool, rng, Composition[index], index));
        }

        var statuses = SquadStatuses(drafts);
        var squad = new List<GeneratedSquadMember>(drafts.Count);

        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];
            var playerId = Guid.CreateVersion7();

            squad.Add(new GeneratedSquadMember(
                Player.Generate(playerId, request.WorldId, draft.Identity, request.Now),
                PlayerAttributes.Create(playerId, draft.Attributes),
                PlayerState.Open(playerId),
                PlayerContract.Sign(
                    Guid.CreateVersion7(),
                    playerId,
                    request.ClubId,
                    request.SeasonNumber,
                    request.SeasonNumber + draft.ContractSeasons - 1,
                    WorldRuleSet.GeneratedWeeklyWageMinorFor(draft.Mean, request.Tier),
                    statuses[index],
                    request.Now),
                PlayerRegistration.Register(
                    Guid.CreateVersion7(),
                    playerId,
                    request.ClubId,
                    request.SeasonId,
                    effectiveFixtureBoundaryRound: 0,
                    request.Now)));
        }

        return squad;
    }

    private static SquadDraft Draft(
        SquadGenerationRequest request,
        PlayerNamePool pool,
        Pcg32 rng,
        PlayerPosition position,
        int index)
    {
        var ordinal = (request.ClubOrdinalInCountry * WorldRuleSet.GeneratorSquadTarget) + index;
        var ageSpan = WorldRuleSet.PlayerMaximumAge - WorldRuleSet.PlayerMinimumAge + 1;
        var age = WorldRuleSet.PlayerMinimumAge + ((rng.NextInt(ageSpan) + rng.NextInt(ageSpan)) / 2);

        var birthDayOfYear = 1 + rng.NextInt(366);

        var preferredFoot = rng.NextInt(10) switch
        {
            < 6 => PreferredFoot.Right,
            < 9 => PreferredFoot.Left,
            _ => PreferredFoot.Both,
        };

        var physique = PlayerAttributeProfiles.PhysicalRangeFor(position);
        var heightCm = physique.MinHeightCm + rng.NextInt(physique.MaxHeightCm - physique.MinHeightCm + 1);
        var weightKg = physique.MinWeightKg + rng.NextInt(physique.MaxWeightKg - physique.MinWeightKg + 1);

        var spread = rng.NextInt((2 * PlayerAttributeProfiles.PlayerSpread) + 1)
            - PlayerAttributeProfiles.PlayerSpread;
        var baseAbility = WorldRuleSet.GeneratedAbilityMeanForTier(request.Tier)
            + PlayerAttributeProfiles.AgeAbilityAdjustment(age)
            + spread;

        var values = new int[AttributeNames.Count];

        foreach (var name in AttributeNames.All)
        {
            values[(int)name] = Math.Clamp(
                baseAbility
                    + PlayerAttributeProfiles.BonusFor(position, name)
                    + rng.NextInt((2 * PlayerAttributeProfiles.AttributeJitter) + 1)
                    - PlayerAttributeProfiles.AttributeJitter,
                WorldRuleSet.AttributeMin,
                WorldRuleSet.AttributeMax);
        }

        var attributes = PlayerAttributeSet.FromValues(values);
        var mean = (int)Math.Round(attributes.Values.Average(), MidpointRounding.AwayFromZero);

        var secondaryPositions = SecondaryPositionsFor(Adjacent[position], position, rng);

        var potential = Math.Clamp(
            mean + PlayerAttributeProfiles.PotentialUpsideForAge(age) + rng.NextInt(3),
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);
        var reputation = Math.Clamp(
            mean + rng.NextInt(5) - 2,
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);

        var contractSeasons = WorldRuleSet.ContractMinSeasons
            + rng.NextInt(WorldRuleSet.ContractMaxSeasons - WorldRuleSet.ContractMinSeasons + 1);

        var (fullName, shortName, nameSeed) = NameFor(request, pool, ordinal);

        return new SquadDraft(
            new PlayerIdentity(
                fullName,
                shortName,
                request.CountryCode,
                nameSeed,
                request.GameYear - age,
                birthDayOfYear,
                preferredFoot,
                heightCm,
                weightKg,
                position,
                secondaryPositions,
                potential,
                reputation),
            attributes,
            mean,
            contractSeasons);
    }

    private static List<PlayerPosition> SecondaryPositionsFor(
        PlayerPosition[] adjacent,
        PlayerPosition position,
        Pcg32 rng)
    {
        var count = rng.NextInt(3);
        var secondaries = new List<PlayerPosition>(count);

        for (var pick = 0; pick < count && pick < adjacent.Length; pick++)
        {
            var candidate = adjacent[rng.NextInt(adjacent.Length)];

            if (candidate != position && !secondaries.Contains(candidate))
            {
                secondaries.Add(candidate);
            }
        }

        return secondaries;
    }

    /// <summary>
    /// Draws a full name from the pool, advancing to the next candidate while the blocklist rejects one
    /// (`FIC-5`, `FIC-6`).
    /// </summary>
    private static (string FullName, string ShortName, string NameSeed) NameFor(
        SquadGenerationRequest request,
        PlayerNamePool pool,
        int ordinal)
    {
        var combinations = (long)pool.GivenNames.Count * pool.Surnames.Count;

        for (var candidate = 0L; candidate < combinations; candidate++)
        {
            var index = ordinal + candidate;
            var given = pool.GivenNames[(int)(index % pool.GivenNames.Count)];
            var surname = pool.Surnames[(int)(index % pool.Surnames.Count)];
            var fullName = string.Create(CultureInfo.InvariantCulture, $"{given} {surname}");

            if (FictionalIdentityBlocklist.ContainsBlockedIdentity(fullName))
            {
                continue;
            }

            return (
                fullName,
                string.Create(CultureInfo.InvariantCulture, $"{given[0]}. {surname}"),
                DeterministicDigest.Of(request.Seed, request.CountryCode, "player-name", ordinal.ToString(CultureInfo.InvariantCulture)));
        }

        throw new InvalidOperationException(
            "The player name pool could not produce a name that clears the blocklist (FIC-6).");
    }

    /// <summary>
    /// Ranks the squad by mean ability and bands it, so squad status reflects where a player sits rather
    /// than the order the generator happened to draw them in.
    /// </summary>
    private static SquadStatus[] SquadStatuses(IReadOnlyList<SquadDraft> drafts)
    {
        var order = drafts
            .Select((draft, index) => (Index: index, draft.Mean))
            .OrderByDescending(entry => entry.Mean)
            .ThenBy(entry => entry.Index)
            .ToList();

        var statuses = new SquadStatus[drafts.Count];

        for (var rank = 0; rank < order.Count; rank++)
        {
            statuses[order[rank].Index] = rank switch
            {
                < 2 => SquadStatus.KeyPlayer,
                < 12 => SquadStatus.FirstTeam,
                < 18 => SquadStatus.Rotation,
                _ => SquadStatus.Prospect,
            };
        }

        return statuses;
    }

    private sealed record SquadDraft(
        PlayerIdentity Identity,
        PlayerAttributeSet Attributes,
        int Mean,
        int ContractSeasons);
}
