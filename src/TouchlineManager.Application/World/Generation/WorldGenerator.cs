using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Finance;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad.Generation;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Application.World.Generation;

/// <summary>What to generate for one tier.</summary>
/// <param name="Seed">The generation seed (`PYR-14`).</param>
/// <param name="Country">The country the tier belongs to; supplies its code, name pool, and display name.</param>
/// <param name="Tier">The tier number, starting at 1.</param>
/// <param name="Season">The season the tier plays in.</param>
/// <param name="Activate">
/// Whether the division is claimable immediately. True for the seeded tier 1; false for a provisioned tier,
/// which stays in provisioning until its backfill and validation complete (`PYR-8`).
/// </param>
/// <param name="BootstrapCutoff">
/// When set, fixtures whose kickoff is at or before it are marked bootstrap, i.e. generated history rather
/// than played history (`PYR-7`). Null for a seeded tier, which has no passed matchdays to backfill.
/// </param>
public sealed record TierGenerationRequest(
    string Seed,
    Country Country,
    int Tier,
    Season Season,
    bool Activate,
    DateTimeOffset? BootstrapCutoff);

/// <summary>What one generated tier produced.</summary>
/// <param name="DivisionId">The division (the durable tier).</param>
/// <param name="DivisionSeasonId">The tier's instance in the season.</param>
/// <param name="ClubIds">The clubs, in identity-generation order, which is the only stable order for a schedule.</param>
/// <param name="Clubs">How many clubs were created.</param>
/// <param name="Players">How many players were created.</param>
/// <param name="Accounts">How many club accounts were opened.</param>
public sealed record GeneratedTier(
    Guid DivisionId,
    Guid DivisionSeasonId,
    IReadOnlyList<Guid> ClubIds,
    int Clubs,
    int Players,
    int Accounts);

/// <summary>
/// Generates one tier of a country's pyramid: its division, clubs, squads, accounts, schedule, and table
/// (`PYR-4`, `PYR-14`).
/// </summary>
/// <remarks>
/// <para>
/// One generation path, used by both the world seeder (tier 1) and the provisioning worker (tier N+1). The
/// two differ only in whether the tier is immediately claimable and whether passed matchdays are marked
/// bootstrap; everything else — the deterministic seed derivations, the club and player generators, the
/// schedule validator — is shared, so a provisioned tier cannot drift from a seeded one.
/// </para>
/// <para>
/// Nothing here saves. The caller stages a whole tier into its unit of work and commits once, which is what
/// makes generation all-or-nothing: a crash mid-tier leaves no half-built division.
/// </para>
/// </remarks>
public sealed class WorldGenerator
{
    private readonly IWorldRepository _world;
    private readonly IClubRepository _clubs;
    private readonly ISquadRepository _squad;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly DivisionScheduleGenerator _schedules;

    /// <summary>Initializes the generator.</summary>
    public WorldGenerator(
        IWorldRepository world,
        IClubRepository clubs,
        ISquadRepository squad,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        DivisionScheduleGenerator schedules)
    {
        _world = world;
        _clubs = clubs;
        _squad = squad;
        _accounts = accounts;
        _ledger = ledger;
        _schedules = schedules;
    }

    /// <summary>Generates one tier and stages it into the current unit of work.</summary>
    /// <param name="request">What to generate.</param>
    /// <param name="now">The current instant.</param>
    public GeneratedTier BuildTier(TierGenerationRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var country = request.Country;
        var tier = request.Tier;
        var season = request.Season;
        var seed = request.Seed;

        var division = Division.Provision(
            Guid.CreateVersion7(),
            country.Id,
            tier,
            country.DisplayName,
            season.Id,
            now);

        // A seeded tier is immediately claimable; a provisioned one stays in provisioning until its
        // generation, validation, and backfill all complete (PYR-8).
        if (request.Activate)
        {
            division.Activate(now);
        }

        _world.AddDivision(division);

        var divisionSeason = DivisionSeason.Create(
            Guid.CreateVersion7(),
            division.Id,
            season.Id,
            DivisionProvisioningGenerator.ScheduleSeedFor(seed, country.Code, tier),
            DivisionProvisioningGenerator.TieDrawSeedFor(seed, country.Code, tier),
            DivisionProvisioningGenerator.TieDrawHashFor(seed, country.Code, tier),
            now);

        divisionSeason.Activate(now);
        _world.AddDivisionSeason(divisionSeason);

        var identities = ClubIdentityGenerator.GenerateDivision(
            seed,
            country.NamePoolKey,
            country.Code,
            tier,
            WorldRuleSet.ClubsPerDivision);

        // The club ids are collected in the order their identities were generated, which is the only stable
        // order for the schedule to be reproducible from: ids are UUIDv7 and differ per run, so the fixture
        // list is keyed on this order plus the stored schedule seed (CAL-8).
        var clubIds = new List<Guid>(identities.Count);
        var players = 0;
        var accounts = 0;

        foreach (var identity in identities)
        {
            var clubId = Guid.CreateVersion7();

            _clubs.Add(Club.Generate(
                clubId,
                country.WorldId,
                country.Id,
                identity,
                tier,
                season.GameYear,
                now));

            _world.AddClubSeasonEntry(ClubSeasonEntry.Enter(
                Guid.CreateVersion7(),
                divisionSeason.Id,
                season.Id,
                clubId,
                ClubControlType.Ai,
                now));

            var account = ClubAccount.Open(Guid.CreateVersion7(), clubId, now);

            // The account opens empty and is funded by its first ledger entry, so the ledger — not the row —
            // is where a club's money comes from, and the balance is one a replay reproduces (FIN-18).
            _accounts.Add(account);
            _ledger.Add(account.Post(
                LedgerPostings.OpeningBalance(
                    Guid.CreateVersion7(),
                    clubId,
                    WorldRuleSet.OpeningCashMinorForTier(tier)),
                now));

            // The ordinal is the club's position within its country, tier-adjusted, matching the ordinal its
            // identity was generated with, so a provisioned tier's squads are distinct from tier 1's.
            players += AddSquad(
                seed,
                country,
                clubId,
                ((tier - 1) * WorldRuleSet.ClubsPerDivision) + clubIds.Count,
                tier,
                season,
                now);

            clubIds.Add(clubId);
            accounts++;
        }

        _schedules.Generate(divisionSeason, clubIds, season, now, request.BootstrapCutoff);

        return new GeneratedTier(
            division.Id,
            divisionSeason.Id,
            clubIds,
            clubIds.Count,
            players,
            accounts);
    }

    /// <summary>Generates and stages one club's squad (`SQ-1`).</summary>
    private int AddSquad(
        string seed,
        Country country,
        Guid clubId,
        int clubOrdinalInCountry,
        int tier,
        Season season,
        DateTimeOffset now)
    {
        var squad = PlayerGenerator.GenerateSquad(new SquadGenerationRequest(
            seed,
            country.NamePoolKey,
            country.Code,
            country.WorldId,
            clubId,
            clubOrdinalInCountry,
            tier,
            season.Id,
            season.SequenceNumber,
            season.GameYear,
            now));

        foreach (var member in squad)
        {
            _squad.AddPlayer(member.Player);
            _squad.AddPlayerAttributes(member.Attributes);
            _squad.AddPlayerState(member.State);
            _squad.AddPlayerContract(member.Contract);
            _squad.AddPlayerRegistration(member.Registration);
        }

        return squad.Count;
    }
}
