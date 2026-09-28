using System.Globalization;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad.Generation;

namespace TouchlineManager.Domain.World.Generation;

/// <summary>
/// The versioned inputs a provisioned tier's generation is reproducible from (`PYR-14`, ADR-0005).
/// </summary>
/// <remarks>
/// <para>
/// A provisioned tier is generated from the seed the provisioning request recorded, not from the world's
/// own seed, so two attempts at the same tier are attempts at the same tier — the property
/// <c>DivisionProvisioningRequest.Retry</c> depends on. Everything else the generation reads (the country,
/// the tier, the schedule generator) is folded in here so a differing rerun is explainable.
/// </para>
/// <para>
/// The schedule and tie-draw derivations match the world bootstrap's, so a tier's fixture list is
/// reproducible by the same rule however it was created — seeded or provisioned.
/// </para>
/// </remarks>
public static class DivisionProvisioningGenerator
{
    /// <summary>The generator version. A change to generation logic that alters output must bump this.</summary>
    public const string Version = "division-gen-v1";

    /// <summary>Derives a tier's schedule seed (`CAL-8`).</summary>
    /// <param name="seed">The provisioning request's recorded seed.</param>
    /// <param name="countryCode">The country's stable code.</param>
    /// <param name="tier">The tier being generated.</param>
    public static string ScheduleSeedFor(string seed, string countryCode, int tier) =>
        DeterministicDigest.Of(seed, countryCode, Tier(tier), "schedule");

    /// <summary>Derives a tier's tie-break draw seed (`TBL-11`).</summary>
    /// <param name="seed">The provisioning request's recorded seed.</param>
    /// <param name="countryCode">The country's stable code.</param>
    /// <param name="tier">The tier being generated.</param>
    public static string TieDrawSeedFor(string seed, string countryCode, int tier) =>
        DeterministicDigest.Of(seed, countryCode, Tier(tier), "tie-draw");

    /// <summary>Digests the tie-draw seed, so the draw a season was ordered by cannot be changed unnoticed.</summary>
    /// <param name="seed">The provisioning request's recorded seed.</param>
    /// <param name="countryCode">The country's stable code.</param>
    /// <param name="tier">The tier being generated.</param>
    public static string TieDrawHashFor(string seed, string countryCode, int tier) =>
        DeterministicDigest.Of(TieDrawSeedFor(seed, countryCode, tier));

    /// <summary>Digests the non-seed inputs that shape a generated tier (`PYR-14`).</summary>
    /// <param name="seed">The provisioning request's recorded seed.</param>
    /// <param name="countryCode">The country's stable code.</param>
    /// <param name="tier">The tier being generated.</param>
    /// <param name="clubsPerDivision">How many clubs the tier holds.</param>
    public static string InputHashFor(string seed, string countryCode, int tier, int clubsPerDivision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        return DeterministicDigest.Of(
            seed,
            Version,
            countryCode,
            Tier(tier),
            clubsPerDivision.ToString(CultureInfo.InvariantCulture),
            ClubIdentityGenerator.Version,
            ClubNamePools.Version,
            PlayerGenerator.Version,
            PlayerNamePools.Version,
            PlayerAttributeProfiles.Version,
            RoundRobinSchedule.Version,
            Rules.WorldRuleSet.Version);
    }

    private static string Tier(int tier) => tier.ToString(CultureInfo.InvariantCulture);
}
