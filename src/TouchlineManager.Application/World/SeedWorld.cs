using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Finance;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Finance;
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
    private readonly IClubRepository _clubs;
    private readonly ISquadRepository _squad;
    private readonly IClubAccountRepository _accounts;
    private readonly ICompetitionRepository _competition;
    private readonly IGenerationRunRepository _generationRuns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly WorldOptions _options;
    private readonly ILogger<SeedWorld> _logger;

    /// <summary>Initializes the use case.</summary>
    public SeedWorld(
        IClock clock,
        IWorldRepository world,
        IClubRepository clubs,
        ISquadRepository squad,
        IClubAccountRepository accounts,
        ICompetitionRepository competition,
        IGenerationRunRepository generationRuns,
        IUnitOfWork unitOfWork,
        IOptions<WorldOptions> options,
        ILogger<SeedWorld> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _world = world;
        _clubs = clubs;
        _squad = squad;
        _accounts = accounts;
        _competition = competition;
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
            var (clubs, players, accounts) = AddCountry(seed, definition, worldId, season, now);

            countries++;
            clubsCreated += clubs;
            playersCreated += players;
            accountsCreated += accounts;
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

    /// <summary>Creates one country, its tier-1 division, its clubs, their squads, and their accounts.</summary>
    private (int Clubs, int Players, int Accounts) AddCountry(
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
        var players = 0;
        var accounts = 0;

        // The club ids are collected in the order their identities were generated, which is the *only*
        // stable order for the schedule to be reproducible from: ids are UUIDv7 and differ per run, so the
        // fixture list is keyed on this order plus the stored schedule seed (CAL-8).
        var clubIds = new List<Guid>(identities.Count);

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

            players += AddSquad(seed, definition, worldId, clubId, clubs, season, now);

            clubIds.Add(clubId);

            clubs++;
            accounts++;
        }

        AddSchedule(divisionSeason, clubIds, season, now);

        return (clubs, players, accounts);
    }

    /// <summary>
    /// Generates and stages a division's whole fixture list: its matchdays and the fixtures within them
    /// (`CAL-8`, `CAL-9`).
    /// </summary>
    /// <param name="divisionSeason">The division-season the schedule belongs to.</param>
    /// <param name="clubIds">The clubs, in a stable order (identity-generation order).</param>
    /// <param name="season">The season, whose window supplies the matchday dates.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// The schedule is validated before it is written, because a fixture list that broke a rule would be a
    /// season that could not be played correctly and would be far harder to notice once it was in the
    /// database than a failed generation (CAL-9, §17.9).
    /// </remarks>
    private void AddSchedule(
        DivisionSeason divisionSeason,
        IReadOnlyList<Guid> clubIds,
        Season season,
        DateTimeOffset now)
    {
        var schedule = RoundRobinSchedule.Generate(
            clubIds,
            DeterministicDigest.SeedOf(divisionSeason.ScheduleSeed));

        var issues = ScheduleValidator.Validate(clubIds, schedule);

        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"The generated schedule for division-season {divisionSeason.Id} is invalid: "
                + string.Join("; ", issues.Select(issue => issue.Detail)));
        }

        var dates = SeasonCalendar.MatchdayDates(
            DateOnly.FromDateTime(season.StartsAt.UtcDateTime),
            WorldRuleSet.MatchdaysPerSeason);

        foreach (var round in schedule)
        {
            var kickoff = SeasonCalendar.KickoffAt(dates[round.RoundNumber - 1]);
            var matchdayId = Guid.CreateVersion7();

            _competition.AddMatchday(Matchday.Schedule(
                matchdayId,
                divisionSeason.Id,
                round.RoundNumber,
                kickoff,
                now));

            foreach (var pairing in round.Pairings)
            {
                _competition.AddFixture(Fixture.Schedule(
                    Guid.CreateVersion7(),
                    matchdayId,
                    pairing.HomeClubId,
                    pairing.AwayClubId,
                    kickoff,
                    now));
            }
        }
    }

    /// <summary>
    /// Generates and stages one club's squad (`SQ-1`).
    /// </summary>
    /// <param name="seed">The world seed.</param>
    /// <param name="definition">The country the club belongs to.</param>
    /// <param name="worldId">The owning world.</param>
    /// <param name="clubId">The club the squad belongs to.</param>
    /// <param name="clubOrdinalInCountry">
    /// The club's ordinal within its country, which is what makes the squad reproducible: ids are UUIDv7
    /// and differ per run, so nothing may key generation on a club id.
    /// </param>
    /// <param name="season">The season the players are registered in.</param>
    /// <param name="now">The current instant.</param>
    private int AddSquad(
        string seed,
        LaunchCountry definition,
        Guid worldId,
        Guid clubId,
        int clubOrdinalInCountry,
        Season season,
        DateTimeOffset now)
    {
        var squad = PlayerGenerator.GenerateSquad(new SquadGenerationRequest(
            seed,
            definition.NamePoolKey,
            definition.Code,
            worldId,
            clubId,
            clubOrdinalInCountry,
            Tier: 1,
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
        Message = "Seeded world {WorldId} from seed {Seed}: {Countries} countries, {Clubs} clubs, {Players} players.")]
    private partial void LogSeeded(Guid worldId, string seed, int countries, int clubs, int players);
}
