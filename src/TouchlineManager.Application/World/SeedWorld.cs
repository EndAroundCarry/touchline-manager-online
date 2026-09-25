using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Application.World;

/// <summary>What happened when the world was seeded.</summary>
public enum SeedWorldOutcome
{
    /// <summary>The world was created.</summary>
    Seeded = 0,

    /// <summary>A world already exists. Seeding is idempotent, so this is a success, not a failure.</summary>
    AlreadySeeded = 1,
}

/// <summary>The result of a seed run.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="WorldId">The world's identity, whether it was just created or already existed.</param>
/// <param name="Seed">The generation seed the world was built from (`PYR-14`).</param>
/// <param name="CountriesCreated">How many countries were created.</param>
/// <param name="ClubsCreated">How many clubs were created.</param>
/// <param name="AccountsCreated">How many club accounts were opened.</param>
public sealed record SeedWorldResult(
    SeedWorldOutcome Outcome,
    Guid WorldId,
    string Seed,
    int CountriesCreated,
    int ClubsCreated,
    int AccountsCreated);

/// <summary>A seed request. Both parts are optional; the configuration supplies the defaults.</summary>
/// <param name="Seed">The generation seed, or null to use the configured default.</param>
/// <param name="FirstMatchday">The first season's start date, or null to use the configured default.</param>
public sealed record SeedWorldRequest(string? Seed = null, DateOnly? FirstMatchday = null);

/// <summary>
/// Creates the initial world: six countries, one tier of 18 clubs each, the first season, and a funded
/// account per club (`WORLD-2`, `WORLD-5`, `FIC-7`).
/// </summary>
/// <remarks>
/// <para>
/// This is the only place clubs are created as fiction rather than as a consequence of play. It is
/// idempotent by checking for an existing world rather than by a unique constraint, because "there is
/// exactly one world" is a product rule (`WORLD-1`) rather than a row identity, and a second world would
/// be a mistake worth reporting rather than a conflict worth resolving.
/// </para>
/// <para>
/// Everything the run creates is derived from one seed, and the run is recorded in
/// <c>world.generation_runs</c> with the seed, the generator version, and a digest of the non-seed
/// inputs. Re-running with the same seed therefore reproduces the same logical world, which is the
/// property the Stage 3 exit criteria test.
/// </para>
/// <para>
/// Generation is deliberately synchronous here. It happens once, before launch, from an operator-run
/// tool — there is no request waiting on it and no reason to hand it to the durable queue, which exists
/// for deadlines rather than for setup.
/// </para>
/// </remarks>
public sealed partial class SeedWorld
{
    private readonly IClock _clock;
    private readonly IWorldRepository _world;
    private readonly IClubRepository _clubs;
    private readonly IClubAccountRepository _accounts;
    private readonly IGenerationRunRepository _generationRuns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly WorldOptions _options;
    private readonly ILogger<SeedWorld> _logger;

    /// <summary>Initializes the use case.</summary>
    public SeedWorld(
        IClock clock,
        IWorldRepository world,
        IClubRepository clubs,
        IClubAccountRepository accounts,
        IGenerationRunRepository generationRuns,
        IUnitOfWork unitOfWork,
        IOptions<WorldOptions> options,
        ILogger<SeedWorld> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _world = world;
        _clubs = clubs;
        _accounts = accounts;
        _generationRuns = generationRuns;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Seeds the world, or reports that one already exists.</summary>
    public async Task<SeedWorldResult> ExecuteAsync(
        SeedWorldRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;

        var existing = await _world.FindWorldAsync(cancellationToken);

        if (existing is not null)
        {
            LogAlreadySeeded(existing.Id);

            return new SeedWorldResult(SeedWorldOutcome.AlreadySeeded, existing.Id, string.Empty, 0, 0, 0);
        }

        var seed = string.IsNullOrWhiteSpace(request.Seed) ? _options.GenerationSeed : request.Seed.Trim();
        var firstMatchday = SeasonCalendar.FirstMatchdayOnOrAfter(
            request.FirstMatchday ?? _options.FirstSeasonStartDate);

        var worldId = Guid.CreateVersion7();
        _world.AddWorld(GameWorld.Create(worldId, _options.Name, now));

        var seasonId = Guid.CreateVersion7();
        var season = Season.Create(
            seasonId,
            worldId,
            sequenceNumber: 1,
            gameYear: firstMatchday.Year,
            WorldRuleSet.Version,
            firstMatchday,
            now);

        season.Activate(now);
        _world.AddSeason(season);

        var run = GenerationRun.Start(
            Guid.CreateVersion7(),
            GenerationRunKind.WorldBootstrap,
            seed,
            ClubIdentityGenerator.Version,
            InputHashFor(seed),
            now);

        var countries = 0;
        var clubsCreated = 0;
        var accountsCreated = 0;

        foreach (var definition in LaunchCountries.All)
        {
            var (clubs, accounts) = AddCountry(seed, definition, worldId, season, now);

            countries++;
            clubsCreated += clubs;
            accountsCreated += accounts;
        }

        run.Succeed(new GenerationRunCounts(countries, clubsCreated, Players: 0, accountsCreated), now);
        _generationRuns.Add(run);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogSeeded(worldId, seed, countries, clubsCreated);

        return new SeedWorldResult(SeedWorldOutcome.Seeded, worldId, seed, countries, clubsCreated, accountsCreated);
    }

    /// <summary>Creates one country, its tier-1 division, its clubs, and their accounts.</summary>
    private (int Clubs, int Accounts) AddCountry(
        string seed,
        LaunchCountry definition,
        Guid worldId,
        Season season,
        DateTimeOffset now)
    {
        var countryId = Guid.CreateVersion7();
        _world.AddCountry(Country.Create(countryId, worldId, definition, now));

        var divisionId = Guid.CreateVersion7();
        var division = Division.Provision(
            divisionId,
            countryId,
            tierNumber: 1,
            definition.DisplayName,
            season.Id,
            now);

        // A seeded tier is immediately claimable, unlike a provisioned one, which stays in
        // provisioning until its generation, validation, and backfill all complete (PYR-8).
        division.Activate(now);
        _world.AddDivision(division);

        var divisionSeasonId = Guid.CreateVersion7();
        var divisionSeason = DivisionSeason.Create(
            divisionSeasonId,
            divisionId,
            season.Id,
            ScheduleSeedFor(seed, definition),
            TieDrawSeedFor(seed, definition),
            TieDrawHashFor(seed, definition),
            now);

        divisionSeason.Activate(now);
        _world.AddDivisionSeason(divisionSeason);

        var identities = ClubIdentityGenerator.GenerateDivision(
            seed,
            definition.NamePoolKey,
            definition.Code,
            tierNumber: 1,
            WorldRuleSet.ClubsPerDivision);

        var clubs = 0;
        var accounts = 0;

        foreach (var identity in identities)
        {
            var clubId = Guid.CreateVersion7();

            _clubs.Add(Club.Generate(
                clubId,
                worldId,
                countryId,
                identity,
                tier: 1,
                foundingGameYear: season.GameYear,
                now));

            _world.AddClubSeasonEntry(ClubSeasonEntry.Enter(
                Guid.CreateVersion7(),
                divisionSeasonId,
                season.Id,
                clubId,
                ClubControlType.Ai,
                now));

            _accounts.Add(ClubAccount.Open(
                Guid.CreateVersion7(),
                clubId,
                WorldRuleSet.OpeningCashMinorForTier(1),
                now));

            clubs++;
            accounts++;
        }

        return (clubs, accounts);
    }

    /// <summary>
    /// Digests the inputs that are not the seed, so two runs that differ only in their starting
    /// conditions are distinguishable from a reproducibility bug (`PYR-14`).
    /// </summary>
    private static string InputHashFor(string seed)
    {
        var parts = new List<string>
        {
            seed,
            ClubIdentityGenerator.Version,
            ClubNamePools.Version,
            WorldRuleSet.Version,
            WorldRuleSet.ClubsPerDivision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        parts.AddRange(LaunchCountries.All.Select(country => country.Code));

        return DeterministicDigest.Of([.. parts]);
    }

    private static string ScheduleSeedFor(string seed, LaunchCountry country) =>
        DeterministicDigest.Of(seed, country.Code, "1", "schedule");

    private static string TieDrawSeedFor(string seed, LaunchCountry country) =>
        DeterministicDigest.Of(seed, country.Code, "1", "tie-draw");

    /// <summary>
    /// Digests the tie-break draw seed, so the draw a season was ordered by cannot be changed unnoticed
    /// (`TBL-11`). Stage 6 generates the draw itself; what is stored here is the commitment to its seed.
    /// </summary>
    private static string TieDrawHashFor(string seed, LaunchCountry country) =>
        DeterministicDigest.Of(TieDrawSeedFor(seed, country));

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "World {WorldId} already exists; the seed run is a no-op (WORLD-1).")]
    private partial void LogAlreadySeeded(Guid worldId);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Seeded world {WorldId} from seed {Seed}: {Countries} countries, {Clubs} clubs.")]
    private partial void LogSeeded(Guid worldId, string seed, int countries, int clubs);
}
