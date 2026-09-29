using System.Globalization;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Application.World.Generation;

/// <summary>The seeds a division-season's schedule and tie-break draw are reproducible from (`CAL-8`, `TBL-11`).</summary>
/// <param name="ScheduleSeed">The seed the fixture list is drawn from.</param>
/// <param name="TieDrawSeed">The seed the tie-break draw is derived from.</param>
/// <param name="TieDrawHash">The published digest of the tie-break seed.</param>
public sealed record SeasonScheduleSeeds(string ScheduleSeed, string TieDrawSeed, string TieDrawHash);

/// <summary>
/// Generates a division-season's fixture list and opening table (`CAL-8`, `CAL-9`, `TBL-10`).
/// </summary>
/// <remarks>
/// <para>
/// One generation path, shared by the world seeder, the provisioning worker, and the season rollover. It was
/// extracted from <see cref="WorldGenerator"/> for the same reason <c>WorldGenerator.BuildTier</c> was
/// extracted for the seeder and the provisioning worker (ADR-0030): a second copy of the schedule generation
/// would be a second place the round-robin properties, the matchday calendar, and the opening table could
/// drift apart, and the rollover is exactly where a drift would be invisible until a season could not be
/// played.
/// </para>
/// <para>
/// Nothing here saves. The caller stages a whole division-season into its unit of work and commits once, so a
/// crash mid-generation leaves no half-built schedule.
/// </para>
/// </remarks>
public sealed class DivisionScheduleGenerator
{
    private readonly ICompetitionRepository _competition;

    /// <summary>Initializes the generator.</summary>
    public DivisionScheduleGenerator(ICompetitionRepository competition) => _competition = competition;

    /// <summary>
    /// Derives a season's schedule and tie-draw seeds for one tier.
    /// </summary>
    /// <remarks>
    /// The season number is folded into every seed, so a division's fixture list is different each season
    /// while still being reproducible from the same inputs. The inputs are the world's generation seed, the
    /// country code, the season number, and the tier, so the same seed and season reproduce the same list.
    /// </remarks>
    /// <param name="seed">The world's generation seed (`PYR-14`).</param>
    /// <param name="countryCode">The country's stable code.</param>
    /// <param name="seasonNumber">The season the schedule is for.</param>
    /// <param name="tier">The tier.</param>
    public static SeasonScheduleSeeds SeedsFor(string seed, string countryCode, int seasonNumber, int tier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        var season = seasonNumber.ToString(CultureInfo.InvariantCulture);
        var tierText = tier.ToString(CultureInfo.InvariantCulture);

        var scheduleSeed = DeterministicDigest.Of(seed, countryCode, season, tierText, "schedule");
        var tieDrawSeed = DeterministicDigest.Of(seed, countryCode, season, tierText, "tie-draw");

        return new SeasonScheduleSeeds(scheduleSeed, tieDrawSeed, DeterministicDigest.Of(tieDrawSeed));
    }

    /// <summary>
    /// Generates and stages a division-season's whole fixture list and opening table.
    /// </summary>
    /// <param name="divisionSeason">The division-season the schedule and table belong to.</param>
    /// <param name="clubIds">The clubs, in a stable order (identity-generation order for a new tier).</param>
    /// <param name="season">The season, whose window supplies the matchday dates.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="bootstrapCutoff">
    /// When set, a fixture whose kickoff is at or before it is marked bootstrap — a passed matchday a new tier
    /// is backfilling (`PYR-7`). Null for a seeded tier or a new season, which have no passed matchdays.
    /// </param>
    public void Generate(
        DivisionSeason divisionSeason,
        IReadOnlyList<Guid> clubIds,
        Season season,
        DateTimeOffset now,
        DateTimeOffset? bootstrapCutoff)
    {
        ArgumentNullException.ThrowIfNull(divisionSeason);
        ArgumentNullException.ThrowIfNull(clubIds);
        ArgumentNullException.ThrowIfNull(season);

        AddSchedule(divisionSeason, clubIds, season, now, bootstrapCutoff);
        AddTable(divisionSeason, clubIds, now);
    }

    /// <summary>
    /// Opens a division's table at nil-nil, ranked by the draw the division recorded before the season
    /// (`TBL-10`, `TBL-11`).
    /// </summary>
    private void AddTable(DivisionSeason divisionSeason, IReadOnlyList<Guid> clubIds, DateTimeOffset now)
    {
        var lines = StandingsCalculator.Rank(
            clubIds,
            [],
            clubId => StandingsCalculator.DrawKeyOf(divisionSeason.TieDrawSeed, clubId));

        foreach (var line in lines)
        {
            _competition.AddStanding(Standing.Create(Guid.CreateVersion7(), divisionSeason.Id, line, now));
        }
    }

    /// <summary>Generates and stages a division's whole fixture list: its matchdays and the fixtures within them.</summary>
    private void AddSchedule(
        DivisionSeason divisionSeason,
        IReadOnlyList<Guid> clubIds,
        Season season,
        DateTimeOffset now,
        DateTimeOffset? bootstrapCutoff)
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

            var isBootstrap = bootstrapCutoff is not null && kickoff <= bootstrapCutoff;

            foreach (var pairing in round.Pairings)
            {
                _competition.AddFixture(Fixture.Schedule(
                    Guid.CreateVersion7(),
                    matchdayId,
                    pairing.HomeClubId,
                    pairing.AwayClubId,
                    kickoff,
                    now,
                    isBootstrap));
            }
        }
    }
}
