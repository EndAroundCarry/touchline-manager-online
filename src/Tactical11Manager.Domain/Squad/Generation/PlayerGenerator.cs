using System.Globalization;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Domain.Squad.Generation;

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
    public const string Version = "player-gen-v3";

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
        PlayerPosition.LeftMidfielder,
        PlayerPosition.RightMidfielder,
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
            [PlayerPosition.RightBack] = [PlayerPosition.CentreBack, PlayerPosition.RightMidfielder],
            [PlayerPosition.LeftBack] = [PlayerPosition.CentreBack, PlayerPosition.LeftMidfielder],
            [PlayerPosition.CentreBack] = [PlayerPosition.DefensiveMidfielder, PlayerPosition.RightBack],
            [PlayerPosition.DefensiveMidfielder] = [PlayerPosition.CentralMidfielder, PlayerPosition.CentreBack],
            [PlayerPosition.CentralMidfielder] = [PlayerPosition.DefensiveMidfielder, PlayerPosition.AttackingMidfielder],
            [PlayerPosition.AttackingMidfielder] = [PlayerPosition.CentralMidfielder, PlayerPosition.Striker],
            [PlayerPosition.RightMidfielder] = [PlayerPosition.RightWinger, PlayerPosition.RightBack],
            [PlayerPosition.LeftMidfielder] = [PlayerPosition.LeftWinger, PlayerPosition.LeftBack],
            [PlayerPosition.RightWinger] = [PlayerPosition.RightMidfielder, PlayerPosition.AttackingMidfielder],
            [PlayerPosition.LeftWinger] = [PlayerPosition.LeftMidfielder, PlayerPosition.AttackingMidfielder],
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
            var nameOrdinal = (request.ClubOrdinalInCountry * WorldRuleSet.GeneratorSquadTarget) + index;

            drafts.Add(Draft(request, pool, rng, Composition[index], nameOrdinal));
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
                PlayerState.Open(playerId, draft.ExperienceBp),
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

    /// <summary>
    /// Generates one emergency replacement player on a minimum contract (`SQ-8`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The safety net the rules describe: when expiry or a repair would leave a club below the minimum
    /// registered squad, a replacement is generated rather than the club being left illegal. It is
    /// deliberately a one-season contract on the generated wage, because it is a repair and not a way to
    /// build a squad.
    /// </para>
    /// <para>
    /// Deterministic like the squad generator, but on its own seed stream tagged <c>replacement</c> and keyed
    /// by the replacement ordinal, so a repair reproduces exactly and cannot collide with the club's own
    /// generated squad.
    /// </para>
    /// </remarks>
    /// <param name="seed">The world's generation seed (`PYR-14`).</param>
    /// <param name="namePoolKey">The club's country name pool key.</param>
    /// <param name="countryCode">The club's country code, which is the player's nationality at MVP.</param>
    /// <param name="worldId">The owning world. Identity only.</param>
    /// <param name="clubId">The club the replacement joins.</param>
    /// <param name="tier">The tier the club plays in, which scales the wage.</param>
    /// <param name="seasonId">The season the replacement is registered in.</param>
    /// <param name="seasonNumber">The season number the contract is signed for.</param>
    /// <param name="gameYear">The season's game year, which fixes the player's age.</param>
    /// <param name="position">The position to generate, which the club is short of.</param>
    /// <param name="replacementOrdinal">The ordinal of this replacement within the world's rollover.</param>
    /// <param name="now">The current instant.</param>
    public static GeneratedSquadMember GenerateEmergencyReplacement(
        string seed,
        string namePoolKey,
        string countryCode,
        Guid worldId,
        Guid clubId,
        int tier,
        Guid seasonId,
        int seasonNumber,
        int gameYear,
        PlayerPosition position,
        int replacementOrdinal,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentOutOfRangeException.ThrowIfNegative(replacementOrdinal);
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);

        var request = new SquadGenerationRequest(
            seed,
            namePoolKey,
            countryCode,
            worldId,
            clubId,
            replacementOrdinal,
            tier,
            seasonId,
            seasonNumber,
            gameYear,
            now);

        var pool = PlayerNamePools.For(namePoolKey);

        var rng = new Pcg32(DeterministicDigest.SeedOf(
            seed,
            countryCode,
            "replacement",
            replacementOrdinal.ToString(CultureInfo.InvariantCulture)));

        // A name ordinal distinct from any generated squad's, derived from the club and the ordinal so a
        // repair does not reuse a squad member's name.
        var nameOrdinal = (int)(DeterministicDigest.SeedOf(
            seed,
            countryCode,
            "replacement-name",
            clubId.ToString("D"),
            replacementOrdinal.ToString(CultureInfo.InvariantCulture)) % 100_000);

        var draft = Draft(request, pool, rng, position, nameOrdinal);
        var playerId = Guid.CreateVersion7();

        return new GeneratedSquadMember(
            Player.Generate(playerId, worldId, draft.Identity, now),
            PlayerAttributes.Create(playerId, draft.Attributes),
            PlayerState.Open(playerId, draft.ExperienceBp),
            PlayerContract.Sign(
                Guid.CreateVersion7(),
                playerId,
                clubId,
                seasonNumber,
                seasonNumber,
                WorldRuleSet.GeneratedWeeklyWageMinorFor(draft.Mean, tier),
                SquadStatus.Prospect,
                now),
            PlayerRegistration.Register(
                Guid.CreateVersion7(),
                playerId,
                clubId,
                seasonId,
                effectiveFixtureBoundaryRound: 0,
                now));
    }

    /// <summary>
    /// Generates one young player for a club's yearly intake: 17 or 18 years old, a ceiling drawn like any generated player's (`TRN-18`), and
    /// skills that start below it by the usual age adjustment. Deterministic from the seed, the club and the ordinal.
    /// </summary>
    /// <param name="seed">The world seed.</param>
    /// <param name="namePoolKey">The name pool key of the club's country.</param>
    /// <param name="countryCode">The club's country.</param>
    /// <param name="worldId">The world.</param>
    /// <param name="clubId">The club taking him in.</param>
    /// <param name="tier">The tier the club plays in, which sets the ceiling's mean.</param>
    /// <param name="seasonId">The season he joins for.</param>
    /// <param name="seasonNumber">The season number he joins for.</param>
    /// <param name="gameYear">The game year he joins in, which fixes his age.</param>
    /// <param name="position">The position he is generated for.</param>
    /// <param name="ordinal">The ordinal of this player within the world's rollover, unique per club and season.</param>
    /// <param name="now">The current instant.</param>
    public static GeneratedSquadMember GenerateYouth(
        string seed,
        string namePoolKey,
        string countryCode,
        Guid worldId,
        Guid clubId,
        int tier,
        Guid seasonId,
        int seasonNumber,
        int gameYear,
        PlayerPosition position,
        int ordinal,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);

        var request = new SquadGenerationRequest(
            seed,
            namePoolKey,
            countryCode,
            worldId,
            clubId,
            ordinal,
            tier,
            seasonId,
            seasonNumber,
            gameYear,
            now);

        var pool = PlayerNamePools.For(namePoolKey);
        var ordinalText = ordinal.ToString(CultureInfo.InvariantCulture);
        var rng = new Pcg32(DeterministicDigest.SeedOf(seed, countryCode, "youth", clubId.ToString("D"), ordinalText));

        var nameOrdinal = (int)(DeterministicDigest.SeedOf(seed, countryCode, "youth-name", clubId.ToString("D"), ordinalText) % 1_000_000);
        var age = WorldRuleSet.PlayerMinimumAge + rng.NextInt(2);
        var potential = GeneratedPotential.Draw(tier, rng);
        var ability = Math.Clamp(
            potential + PlayerAttributeProfiles.AgeAbilityAdjustment(age),
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);

        var draft = Draft(request, pool, rng, position, nameOrdinal, new DraftOverride(age, potential, ability));
        var playerId = Guid.CreateVersion7();

        return new GeneratedSquadMember(
            Player.Generate(playerId, worldId, draft.Identity, now),
            PlayerAttributes.Create(playerId, draft.Attributes),
            PlayerState.Open(playerId, draft.ExperienceBp),
            PlayerContract.Sign(
                Guid.CreateVersion7(),
                playerId,
                clubId,
                seasonNumber,
                seasonNumber + WorldRuleSet.ContractMaxSeasons - 1,
                WorldRuleSet.GeneratedWeeklyWageMinorFor(draft.Mean, tier),
                SquadStatus.Prospect,
                now),
            PlayerRegistration.Register(
                Guid.CreateVersion7(),
                playerId,
                clubId,
                seasonId,
                effectiveFixtureBoundaryRound: 0,
                now));
    }

    /// <summary>
    /// Draws one player. With no <paramref name="over"/> the draws are exactly the squad generator's, in the
    /// same order, so a squad's golden digest cannot move; a free agent passes an override instead.
    /// </summary>
    internal static SquadDraft Draft(
        SquadGenerationRequest request,
        PlayerNamePool pool,
        Pcg32 rng,
        PlayerPosition position,
        int nameOrdinal,
        DraftOverride? over = null)
    {
        int age;

        if (over is null)
        {
            var ageSpan = WorldRuleSet.PlayerMaximumAge - WorldRuleSet.PlayerMinimumAge + 1;
            age = WorldRuleSet.PlayerMinimumAge + ((rng.NextInt(ageSpan) + rng.NextInt(ageSpan)) / 2);
        }
        else
        {
            age = over.Age;
        }

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

        // The ceiling is drawn first and the skills follow from it (`TRN-18`), so a young player starts
        // below a potential that is already fixed rather than having it derived from his skills.
        int potential;
        int baseAbility;

        if (over is null)
        {
            potential = GeneratedPotential.Draw(request.Tier, rng);
            baseAbility = potential + PlayerAttributeProfiles.AgeAbilityAdjustment(age);
        }
        else
        {
            potential = over.Potential;
            baseAbility = over.TargetAbility;
        }

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

        if (over is not null)
        {
            ShiftToAbility(values, over.TargetAbility);
        }

        var attributes = PlayerAttributeSet.FromValues(values);
        var mean = (int)Math.Round(attributes.Values.Average(), MidpointRounding.AwayFromZero);

        var secondaryPositions = SecondaryPositionsFor(Adjacent[position], position, rng);

        var reputation = Math.Clamp(
            mean + rng.NextInt(5) - 2,
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);

        var contractSeasons = WorldRuleSet.ContractMinSeasons
            + rng.NextInt(WorldRuleSet.ContractMaxSeasons - WorldRuleSet.ContractMinSeasons + 1);

        var (fullName, shortName, nameSeed) = NameFor(request, pool, nameOrdinal);

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
            contractSeasons,
            PlayerExperience.StartingFor(age));
    }

    /// <summary>
    /// Moves every attribute by the same amount until the rounded mean is the target, so a position's bonuses
    /// shape the player without moving the ability he shows. Clamping at the scale's ends can leave a
    /// remainder, so it repeats; at the abilities a free agent has it settles in a step or two.
    /// </summary>
    private static void ShiftToAbility(int[] values, int target)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var mean = (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);

            if (mean == target)
            {
                return;
            }

            var delta = target - mean;

            for (var index = 0; index < values.Length; index++)
            {
                values[index] = Math.Clamp(values[index] + delta, WorldRuleSet.AttributeMin, WorldRuleSet.AttributeMax);
            }
        }
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
    private static SquadStatus[] SquadStatuses(List<SquadDraft> drafts)
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

    /// <summary>One drawn player, before his rows exist.</summary>
    internal sealed record SquadDraft(
        PlayerIdentity Identity,
        PlayerAttributeSet Attributes,
        int Mean,
        int ContractSeasons,
        int ExperienceBp);

    /// <summary>
    /// The values a free agent fixes instead of drawing them the way a squad member does: his age, his
    /// hidden potential, and the ability he shows (the rounded mean of his attributes).
    /// </summary>
    internal sealed record DraftOverride(int Age, int Potential, int TargetAbility);
}
