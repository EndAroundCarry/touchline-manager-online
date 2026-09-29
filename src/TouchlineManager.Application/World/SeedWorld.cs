using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.World.Generation;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad.Generation;
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
/// <param name="PlayersCreated">How many players were created (`SQ-1`).</param>
/// <param name="AccountsCreated">How many club accounts were opened.</param>
public sealed record SeedWorldResult(
    SeedWorldOutcome Outcome,
    Guid WorldId,
    string Seed,
    int CountriesCreated,
    int ClubsCreated,
    int PlayersCreated,
    int AccountsCreated);

/// <summary>A seed request. Both parts are optional; the configuration supplies the defaults.</summary>
/// <param name="Seed">The generation seed, or null to use the configured default.</param>
/// <param name="FirstMatchday">The first season's start date, or null to use the configured default.</param>
public sealed record SeedWorldRequest(string? Seed = null, DateOnly? FirstMatchday = null);

/// <summary>
/// Creates the initial world: six countries, one tier of 18 clubs each, a legal senior squad per club,
/// the first season, and a funded account per club (`WORLD-2`, `WORLD-5`, `FIC-7`, `SQ-1`).
/// </summary>
/// <remarks>
/// <para>
/// This is the only place clubs and players are created as fiction rather than as a consequence of play.
/// It is idempotent by checking for an existing world rather than by a unique constraint, because "there
/// is exactly one world" is a product rule (`WORLD-1`) rather than a row identity, and a second world
/// would be a mistake worth reporting rather than a conflict worth resolving.
/// </para>
/// <para>
/// Everything the run creates is derived from one seed, and the run is recorded in
/// <c>world.generation_runs</c> with the seed, the generator version, and a digest of the non-seed
/// inputs. Re-running with the same seed therefore reproduces the same logical world, which is the
/// property the Stage 3 exit criteria test and which Stage 4 extends to the squads.
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
    private readonly WorldGenerator _generator;
    private readonly IGenerationRunRepository _generationRuns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly WorldOptions _options;
    private readonly ILogger<SeedWorld> _logger;

    /// <summary>Initializes the use case.</summary>
    public SeedWorld(
        IClock clock,
        IWorldRepository world,
        WorldGenerator generator,
        IGenerationRunRepository generationRuns,
        IUnitOfWork unitOfWork,
        IOptions<WorldOptions> options,
        ILogger<SeedWorld> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _world = world;
        _generator = generator;
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

            return new SeedWorldResult(SeedWorldOutcome.AlreadySeeded, existing.Id, string.Empty, 0, 0, 0, 0);
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
            WorldBootstrapGenerator.Version,
            InputHashFor(seed),
            now);

        var countries = 0;
        var clubsCreated = 0;
        var playersCreated = 0;
        var accountsCreated = 0;

        foreach (var definition in LaunchCountries.All)
        {
            var countryId = Guid.CreateVersion7();
            var country = Country.Create(countryId, worldId, definition, now);

            _world.AddCountry(country);

            // The one generation path, shared with the provisioning worker. Tier 1 is claimable the moment
            // it exists; a provisioned tier stays in provisioning until its backfill completes (PYR-8).
            var tier = _generator.BuildTier(
                new TierGenerationRequest(
                    Seed: seed,
                    WorldSeed: seed,
                    Country: country,
                    Tier: 1,
                    Season: season,
                    Activate: true,
                    BootstrapCutoff: null),
                now);

            countries++;
            clubsCreated += tier.Clubs;
            playersCreated += tier.Players;
            accountsCreated += tier.Accounts;
        }

        run.Succeed(new GenerationRunCounts(countries, clubsCreated, playersCreated, accountsCreated), now);
        _generationRuns.Add(run);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogSeeded(worldId, seed, countries, clubsCreated, playersCreated);

        return new SeedWorldResult(
            SeedWorldOutcome.Seeded,
            worldId,
            seed,
            countries,
            clubsCreated,
            playersCreated,
            accountsCreated);
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
            WorldBootstrapGenerator.Version,
            ClubIdentityGenerator.Version,
            ClubNamePools.Version,
            PlayerGenerator.Version,
            PlayerNamePools.Version,
            PlayerAttributeProfiles.Version,
            RoundRobinSchedule.Version,
            WorldRuleSet.Version,
            WorldRuleSet.ClubsPerDivision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WorldRuleSet.GeneratorSquadTarget.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WorldRuleSet.GeneratedGoalkeepers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WorldRuleSet.GeneratedDefenders.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WorldRuleSet.GeneratedMidfielders.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WorldRuleSet.GeneratedAttackers.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        parts.AddRange(LaunchCountries.All.Select(country => country.Code));

        return DeterministicDigest.Of([.. parts]);
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "World {WorldId} already exists; the seed run is a no-op (WORLD-1).")]
    private partial void LogAlreadySeeded(Guid worldId);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Seeded world {WorldId} from seed {Seed}: {Countries} countries, {Clubs} clubs, {Players} players.")]
    private partial void LogSeeded(Guid worldId, string seed, int countries, int clubs, int players);
}
