using System.Globalization;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.Squad.Training;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.SimulationBenchmarks;

// The economy sandbox (plan toasty-scott, Milestones 0 and 1): one country of four tiers of clubs (WorldRuleSet.ClubsPerDivision), ten seasons, in matchday steps.
//
// It calls the game's own pure rule code (fans, stadium, structures, valuation, renewal quote, daily training, retirement, the AI contract, market and
// finance policies, the sponsor, standing and debt rules, the generators, promotion and relegation) and replaces only what is not pure: the match engine
// is replaced by the fans' own win/draw/loss curve (FanRuleSet, fitted to 1,800 tick-engine matches), the database by the classes in EconModel.cs, and
// the auction by a highest-bid-wins round. The money formulas go through EconRules.
//
// One simulated step = one matchday, and the season is the game's own cycle (plan luminous-cosmos): WorldRuleSet.MatchdaysPerSeason matchday steps, three to
// a week, and then the rollover week as one more step without a match. A step has three market rounds (a listing is open for at least one round, about
// the game's 48-hour minimum), then the match, then the matchday's money (league money, sponsor, wages). The weekly run is the Sunday after every third
// matchday and once more in the rollover week, WorldRuleSet.SeasonCycleWeeks times a season: the shop, the operating cost, the upkeep, the free-agent
// batch and the fans' review.
internal sealed partial class EconSim
{
    public const int Tiers = 4;
    public const int ClubsPerTier = WorldRuleSet.ClubsPerDivision;
    public const int Matchdays = WorldRuleSet.MatchdaysPerSeason;

    /// <summary>The weekly finance runs, free-agent batches and fan reviews of a season: one a week of the cycle, the last in the rollover week.</summary>
    public const int RunsPerSeason = WorldRuleSet.SeasonCycleWeeks;

    /// <summary>The matchdays between two weekly runs.</summary>
    public const int MatchdaysPerRun = EconomyRuleSet.MatchdaysPerWeeklyRun;

    /// <summary>The steps of a season: its matchdays, then the rollover week.</summary>
    public const int Steps = Matchdays + 1;

    public const int Rounds = 3;
    public const int GameYearBase = 2030;
    public const int ReferenceTier = Tiers;

    private const string CountryCode = "XX";
    private const string NamePool = "england";

    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TrainingBase = new(2030, 1, 1);

    private readonly string _seedText;
    private readonly Guid _worldId;
    private readonly List<(SimClub Home, SimClub Away)>[][] _fixtures = new List<(SimClub, SimClub)>[Tiers + 1][];
    private readonly List<SimPlayer> _releasedWaiting = [];
    private int _nextPlayerId = 1;
    private int _replacementOrdinal;
    private int _listingOrdinal;

    public EconSim(ulong seed, int seasons, int youthPerClub = EconSim.DefaultYouthPerAiClub, int trainingPercent = 100)
    {
        if (Matchdays != (RunsPerSeason - 1) * MatchdaysPerRun)
        {
            throw new InvalidOperationException($"{Matchdays} matchdays do not fill {RunsPerSeason - 1} weeks of {MatchdaysPerRun}: the sandbox's weekly cadence needs a season cycle that does.");
        }

        YouthPerClub = youthPerClub;
        TrainingPercent = trainingPercent;
        Seed = seed;
        Seasons = seasons;
        _seedText = string.Create(CultureInfo.InvariantCulture, $"econ-{seed}");
        _worldId = MakeGuid(9, (int)(seed & 0x7FFFFFFF));

        for (var ordinal = 0; ordinal < Tiers * ClubsPerTier; ordinal++)
        {
            var tier = (ordinal / ClubsPerTier) + 1;
            var club = new SimClub
            {
                Idx = ordinal,
                Guid = MakeGuid(1, ordinal),
                Name = string.Create(CultureInfo.InvariantCulture, $"T{tier}-{(ordinal % ClubsPerTier) + 1:00}"),
                Tier = tier,
                Cash = EconRules.OpeningCash(tier),
                StandingCentis = ClubStanding.OpeningCentis(tier),
                Temperament = TemperamentOf(seed, ordinal),
                Fans = FanRuleSet.OpeningFor(tier),
                Seats = new StadiumSeats(
                    StadiumRuleSet.OpeningStanding,
                    StadiumRuleSet.OpeningSeating,
                    StadiumRuleSet.OpeningCoveredSeating,
                    StadiumRuleSet.OpeningVip),
            };

            Clubs.Add(club);

            foreach (var player in SizedSquad(
                club,
                draw => new SquadGenerationRequest(_seedText, NamePool, CountryCode, _worldId, club.Guid, ordinal + (draw * 1_000), tier, MakeGuid(2, 1), 1, GameYearBase, Now),
                EconomyRuleSet.SeededWageBandLowBp,
                EconomyRuleSet.SeededWageBandHighBp))
            {
                club.Squad.Add(player);
                Opening.Add((player.AgeIn(GameYearBase), player.Ability, player.Potential, player.OwnSkill));
            }
        }
    }

    /// <summary>
    /// A squad for a club whose wages come to a band of a fresh club's income forecast: the generator's draws are tried in turn and the first inside
    /// the band is kept, or the nearest (plan section 8; the world seeder does the same for every club).
    /// </summary>
    private List<SimPlayer> SizedSquad(SimClub club, Func<int, SquadGenerationRequest> request, int lowBp, int highBp)
    {
        List<SimPlayer>? best = null;
        var bestDistance = long.MaxValue;
        var draws = TemplateSquads ? 1 : EconomyRuleSet.SquadSizingDraws;

        for (var draw = 0; draw < draws; draw++)
        {
            var squad = new List<SimPlayer>();

            // Plan cheeky-dongarra: the world's squads are the tier's fair template, with fixed wages, so there is nothing to size. ECON_SQUADS=sized
            // draws the old random squads sized to the wage band, for a comparison.
            var generated = TemplateSquads ? PlayerGenerator.GenerateTemplateSquad(request(draw)) : PlayerGenerator.GenerateSquad(request(draw));

            foreach (var member in generated)
            {
                var player = FromGenerated(member.Player, member.Attributes, member.State, gem: false);

                player.Club = club;
                player.Wage = EconRules.GeneratedWage(player.Ability);
                player.ContractEnd = member.Contract.EndSeasonNumber;
                player.Status = member.Contract.SquadStatus;
                squad.Add(player);
            }

            var distance = EconomyRuleSet.WageBandDistanceMinor(club.Tier, squad.Sum(player => player.Wage), lowBp, highBp);

            if (distance < bestDistance)
            {
                best = squad;
                bestDistance = distance;
            }

            if (distance == 0)
            {
                break;
            }
        }

        return best!;
    }

    /// <summary>The world draws the tier's fair template squads (the game today); ECON_SQUADS=sized turns it back to the random sized squads.</summary>
    public static bool TemplateSquads { get; } = !string.Equals(Environment.GetEnvironmentVariable("ECON_SQUADS"), "sized", StringComparison.OrdinalIgnoreCase);

    /// <summary>Each player's age, skill and ceiling when the world opened, for the age profile in the report.</summary>
    public List<(int Age, int Ability, int Potential, double Own)> Opening { get; } = [];

    public ulong Seed { get; }

    public int Seasons { get; }

    /// <summary>The intake the sandbox gives each computer club by default: 2 holds a squad at the AI's target of 23 (milestone 0, with training v4).</summary>
    public const int DefaultYouthPerAiClub = 2;

    /// <summary>The young players each computer club takes in at every rollover (0 is the game today).</summary>
    public int YouthPerClub { get; }

    /// <summary>How fast players develop, as a percentage of today's (100). A what-if: it scales the aptitude the daily progression reads, so decline is untouched.</summary>
    public int TrainingPercent { get; }

    public int Season { get; private set; }

    /// <summary>The step of the season, from 0: matchday Step + 1, and the last step is the rollover week.</summary>
    public int Step { get; private set; }

    /// <summary>The week of the season, from 0, as the weekly run counts it.</summary>
    public int WeekOfSeason => Step / MatchdaysPerRun;

    /// <summary>The step has a match.</summary>
    private bool HasMatchday => Step < Matchdays;

    /// <summary>The step opens a week: the free-agent batch is drawn.</summary>
    private bool OpensWeek => Step % MatchdaysPerRun == 0;

    /// <summary>The step ends with the Sunday weekly run: after every third matchday, and in the rollover week.</summary>
    private bool RunsWeekly => !HasMatchday || (Step + 1) % MatchdaysPerRun == 0;

    public int RoundInWeek { get; private set; }

    public int GameYear => GameYearBase + Season - 1;

    public int Round => ((((Season - 1) * Steps) + Step) * Rounds) + RoundInWeek;

    public List<SimClub> Clubs { get; } = [];

    public IEnumerable<(int Age, int Ability, int Potential, double Own)> Closing =>
        Clubs.SelectMany(club => club.Squad).Select(player => (player.AgeIn(GameYear), player.Ability, player.Potential, player.OwnSkill));

    public List<SimListing> Listings { get; } = [];

    public List<ClubSeasonRow> Rows { get; } = [];

    /// <summary>Squad and free-agent counts by season; the rollover at the end of a season counts in that season.</summary>
    public Dictionary<int, SeasonCounters> Counters { get; } = [];

    private SeasonCounters Counter => Counters.TryGetValue(Season, out var counter) ? counter : Counters[Season] = new SeasonCounters();

    public List<TransferRecord> Transfers { get; } = [];

    public List<TradeRecord> Trades { get; } = [];

    public List<string> Log { get; } = [];

    /// <summary>The cleaned-up record of a clean start, for the report.</summary>
    public List<string> CleanStarts { get; } = [];

    /// <summary>The wage bill of each clean start as a share of a fresh club's season income forecast.</summary>
    public List<double> CleanStartWageShares { get; } = [];

    public static Guid MakeGuid(int kind, int a, int b = 0)
    {
        var bytes = new byte[16];

        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), kind);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), a);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), b);

        return new Guid(bytes);
    }

    public void Note(SimClub club, string text) =>
        Log.Add(string.Create(CultureInfo.InvariantCulture, $"s{Season:00} w{WeekOfSeason + 1:00} {club.Name}: {text}"));

    public IEnumerable<SimClub> Tier(int tier) => Clubs.Where(club => club.Tier == tier);

    public void Run()
    {
        for (Season = 1; Season <= Seasons; Season++)
        {
            StartSeason();

            for (Step = 0; Step < Steps; Step++)
            {
                RunStep();
            }

            EndSeason();
        }

        Season = Seasons;
        Step = Steps - 1;
    }

    // ---- the season -------------------------------------------------------------------------------------------------------------------

    private void StartSeason()
    {
        foreach (var club in Clubs)
        {
            club.ResetSeason();
            club.ShadowMin = club.ShadowCash;

            if (club.PendingParachute > 0)
            {
                club.Cash += club.PendingParachute;
                club.Books.Parachute += club.PendingParachute;
                club.PendingParachute = 0;
            }
        }

        if (Season == CleanStartSeason)
        {
            TakeOverWithCleanStart();
        }

        ComputeStrengths();
        BuildSchedule();

        foreach (var tier in Enumerable.Range(1, Tiers))
        {
            var ranks = FanBaseKeeper.RankByStrength(Tier(tier).ToDictionary(club => club.Guid, club => club.Strength));

            foreach (var club in Tier(tier))
            {
                club.TargetRank = ranks[club.Guid];
            }
        }

        if (Season == 1)
        {
            PickManagers();
        }

        foreach (var club in Clubs.Where(club => club.Managed))
        {
            club.Manager!.OnSeasonStart(this, club);
        }

        SignSponsors();

        if (Environment.GetEnvironmentVariable("ECON_DEBUG") == "1" && Season == 1)
        {
            foreach (var tier in Enumerable.Range(1, Tiers))
            {
                var club = Tier(tier).First(candidate => !candidate.Managed);

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"debug tier {tier} {club.Name}: bill {club.WageBill / 100:N0}, forecast {club.Forecast / 100:N0}, squad {string.Join(" ", club.Squad.OrderByDescending(player => player.AbilityTenths).Select(player => $"{player.AbilityTenths / 10.0:0.0}/{player.Wage / 100000.0:0}k"))}"));
            }
        }
    }

    private void RunStep()
    {
        for (RoundInWeek = 0; RoundInWeek < Rounds; RoundInWeek++)
        {
            if (RoundInWeek == 0)
            {
                TrainStep();

                if (OpensWeek)
                {
                    ResolvePool();
                    OpenPool();
                }
            }

            ResolveListings();

            foreach (var club in Clubs.Where(club => club.Managed))
            {
                club.Manager!.Act(this, club, Step, RoundInWeek);
            }

            AiMarketRound();
        }

        RoundInWeek = Rounds - 1;

        if (HasMatchday)
        {
            ComputeStrengths();
            PlayMatchday();
        }

        foreach (var club in Clubs)
        {
            SettleStep(club);
        }

        if (RunsWeekly)
        {
            FanReview();
        }

        if (!HasMatchday)
        {
            return;
        }

        foreach (var club in Clubs)
        {
            if (club.Managed)
            {
                club.Manager!.OnWeekEnd(this, club, Step);
            }
            else
            {
                AiGrowth(club);
            }
        }
    }

    // ---- training ---------------------------------------------------------------------------------------------------------------------

    private void TrainStep()
    {
        // The season's training days are spread over its matchdays; the rollover week has none left.
        var from = HasMatchday ? TrainingAgeCurve.TrainingDaysPerSeason * Step / Matchdays : 0;
        var to = HasMatchday ? TrainingAgeCurve.TrainingDaysPerSeason * (Step + 1) / Matchdays : 0;

        for (var day = from; day < to; day++)
        {
            var date = TrainingBase.AddDays(((Season - 1) * 100) + day);

            foreach (var club in Clubs)
            {
                foreach (var player in club.Squad)
                {
                    EconTraining.Day(player, player.AgeIn(GameYear), date, TrainingPercent);
                }
            }
        }
    }

    // ---- the matchday -----------------------------------------------------------------------------------------------------------------

    public void ComputeStrengths()
    {
        foreach (var club in Clubs)
        {
            club.Strength = TeamStrength.OfSquad([.. club.Squad.Select(player => player.ToAi())]) + (club.Managed ? club.Manager!.StrengthBonus : 0);
        }
    }

    private void BuildSchedule()
    {
        for (var tier = 1; tier <= Tiers; tier++)
        {
            var order = new Pcg32(DeterministicDigest.SeedOf(_seedText, "schedule", Season.ToString(CultureInfo.InvariantCulture), tier.ToString(CultureInfo.InvariantCulture)));
            var arr = Tier(tier).OrderBy(club => club.Idx).ToList();

            for (var i = arr.Count - 1; i > 0; i--)
            {
                var j = order.NextInt(i + 1);

                (arr[i], arr[j]) = (arr[j], arr[i]);
            }

            var weeks = new List<(SimClub, SimClub)>[Matchdays];
            var half = arr.Count / 2;

            for (var round = 0; round < arr.Count - 1; round++)
            {
                var first = new List<(SimClub, SimClub)>(half);
                var second = new List<(SimClub, SimClub)>(half);

                for (var i = 0; i < half; i++)
                {
                    var a = arr[i];
                    var b = arr[arr.Count - 1 - i];
                    var (home, away) = (i + round) % 2 == 0 ? (a, b) : (b, a);

                    first.Add((home, away));
                    second.Add((away, home));
                }

                weeks[round] = first;
                weeks[round + arr.Count - 1] = second;

                var last = arr[^1];

                arr.RemoveAt(arr.Count - 1);
                arr.Insert(1, last);
            }

            _fixtures[tier] = weeks;
        }
    }

    private void PlayMatchday()
    {
        for (var tier = 1; tier <= Tiers; tier++)
        {
            foreach (var (home, away) in _fixtures[tier][Step])
            {
                Play(home, away);
            }
        }
    }

    private void Play(SimClub home, SimClub away)
    {
        var gap = FanRuleSet.GapCentis(home.Strength, home.Form, away.Strength, away.Form);
        var expectedHome = FanRuleSet.ExpectationForGap(gap, atHome: true).ExpectedPointsCentis;
        var expectedAway = FanRuleSet.ExpectationForGap(gap, atHome: false).ExpectedPointsCentis;

        var (goalsHome, goalsAway) = DrawScore(home, away);

        // The gate and the snack bar are posted before the fans move, from the fans and the mood the match was played in front of.
        var gate = EconRules.Gate(home.Seats, home.Tier, home.Fans.Fans, home.Fans.MoodBp);
        var spectators = EconRules.Attendance(home.Seats, home.Fans.Fans, home.Fans.MoodBp);
        var bonus = StructureRuleSet.ResultBonusFor(goalsHome, goalsAway, expectedHome);
        var snack = EconRules.SnackTakings(home.SnackLevel, spectators, bonus.Bp, home.Tier);

        home.Cash += gate + snack;
        home.Books.Gate += gate;
        home.Books.Snack += snack;
        home.Books.HomeMatches++;
        home.Books.Attendance += spectators;

        var capacity = Math.Max(1, home.Seats.Total);
        var wanted = StadiumRuleSet.MatchdayDemandFor(home.Fans.Fans, home.Fans.MoodBp);

        if (wanted >= capacity)
        {
            home.Books.SoldOut++;
        }

        home.RecentFill.Enqueue((double)wanted / capacity);

        while (home.RecentFill.Count > 5)
        {
            home.RecentFill.Dequeue();
        }

        var fixtureId = MakeGuid(3, Season, (Step * 1000) + home.Idx);

        home.Fans = FanRuleSet.AfterMatch(home.Fans, home.Tier, expectedHome, goalsHome, goalsAway, fixtureId, home.Guid, home.FirstSeason);
        away.Fans = FanRuleSet.AfterMatch(away.Fans, away.Tier, expectedAway, goalsAway, goalsHome, fixtureId, away.Guid, away.FirstSeason);

        Settle(home, goalsHome, goalsAway);
        Settle(away, goalsAway, goalsHome);
    }

    private static void Settle(SimClub club, int goalsFor, int goalsAgainst)
    {
        var points = goalsFor > goalsAgainst ? 3 : goalsFor == goalsAgainst ? 1 : 0;

        club.Points += points;
        club.GoalsFor += goalsFor;
        club.GoalsAgainst += goalsAgainst;
        club.Played++;
        club.Form = new FanForm(club.Form.Games + 1, club.Form.Points + points);
    }

    /// <summary>A score drawn from the curve the fans' expectation is fitted to: the result first, then goals that fit it.</summary>
    private (int Home, int Away) DrawScore(SimClub home, SimClub away)
    {
        var rng = new Pcg32(DeterministicDigest.SeedOf(
            _seedText,
            "match",
            Season.ToString(CultureInfo.InvariantCulture),
            Step.ToString(CultureInfo.InvariantCulture),
            home.Idx.ToString(CultureInfo.InvariantCulture)));

        var trueGap = home.Strength - away.Strength;
        var expectation = FanRuleSet.ExpectationForGap(trueGap, atHome: true);
        var (homeMean, awayMean) = FanRuleSet.ExpectedGoalsForGap(trueGap);
        var roll = rng.NextInt(10_000);
        var result = roll < expectation.WinBp ? 1 : roll < expectation.WinBp + expectation.DrawBp ? 0 : -1;

        for (var attempt = 0; attempt < 60; attempt++)
        {
            var goalsHome = Poisson(rng, homeMean / 100.0);
            var goalsAway = Poisson(rng, awayMean / 100.0);

            if (Math.Sign(goalsHome - goalsAway) == result)
            {
                return (goalsHome, goalsAway);
            }
        }

        return result switch { 1 => (1, 0), 0 => (0, 0), _ => (0, 1) };
    }

    private static int Poisson(Pcg32 rng, double mean)
    {
        var limit = Math.Exp(-mean);
        var product = 1.0;
        var count = 0;

        do
        {
            count++;
            product *= rng.NextDouble();
        }
        while (product > limit);

        return count - 1;
    }

    // ---- the matchday's money, the Sunday finance run and the fans' review ----------------------------------------------------------

    /// <summary>
    /// One step's settlement of a club's money (plan section 2). A matchday pays the league money, the sponsor's fixed part and the wages, each a
    /// matchday's part of the season. The weekly run, the Sunday after every third matchday and the one in the rollover week, pays the shop, the
    /// operating cost and the upkeep, each a run's part of the season.
    /// </summary>
    private void SettleStep(SimClub club)
    {
        var books = club.Books;

        if (HasMatchday)
        {
            var tv = EconRules.TvPerMatchday(club.Tier);
            var sponsor = club.HasSponsor ? EconomyRuleSet.PerMatchday(club.SponsorFixedSeason) : 0;
            var wages = club.WageBill / Matchdays;

            club.Cash += tv + sponsor;
            books.Tv += tv;
            books.Sponsor += sponsor;
            club.Cash -= wages;
            books.Wages += wages;

            foreach (var player in club.Squad.Where(player => player.BoughtFee > 0))
            {
                player.WagesSinceBought += player.Wage / Matchdays;
            }
        }

        if (RunsWeekly)
        {
            var merch = EconRules.MerchTakings(club.MerchLevel, club.Fans.Fans, FanMoods.WordFor(club.Fans.MoodBp), club.Tier);
            var operating = EconRules.OperatingPerRun(club.Tier);
            var upkeep = EconRules.Upkeep(club.SnackLevel, club.Tier) + EconRules.Upkeep(club.MerchLevel, club.Tier) + EconRules.GroundUpkeepPerRun(club.Seats, club.Tier);

            club.Cash += merch;
            books.Merch += merch;

            // Only the recurring costs may take a club below zero (plan section 6); the state machine reads the result.
            club.Cash -= operating + upkeep;
            books.Operating += operating;
            books.Upkeep += upkeep;
        }

        if (HasMatchday)
        {
            if (Step == (Matchdays / 2) - 1)
            {
                club.Forecast = ForecastSeasonIncome(club);
            }

            ReadFinancialState(club);
        }

        club.ShadowMin = Math.Min(club.ShadowMin, club.ShadowCash);
    }

    private void FanReview()
    {
        for (var tier = 1; tier <= Tiers; tier++)
        {
            var clubs = Tier(tier).Where(club => club.Played > 0).ToList();
            var table = Table(tier);
            var places = clubs
                .OrderByDescending(club => club.Points * 1_000 / club.Played)
                .ThenBy(club => table.IndexOf(club))
                .Select((club, index) => (club, place: index + 1))
                .ToDictionary(entry => entry.club, entry => entry.place);

            foreach (var club in clubs)
            {
                club.Fans = FanRuleSet.AfterWeek(club.Fans, places[club] - club.TargetRank, club.Played, Matchdays);
            }
        }
    }

    public List<SimClub> Table(int tier) => [.. Tier(tier)
        .OrderByDescending(club => club.Points)
        .ThenByDescending(club => club.GoalsFor - club.GoalsAgainst)
        .ThenByDescending(club => club.GoalsFor)
        .ThenBy(club => club.Idx)];

    // ---- what a club can expect -------------------------------------------------------------------------------------------------------

    /// <summary>A season's income on the club's present fans and ground: the league and sponsor money, the gate and snack bar of the home matches and the shop (plan section 5).</summary>
    public static long ForecastSeasonIncome(SimClub club) => EconomyRuleSet.SeasonIncomeForecastMinor(
        club.Tier,
        club.HasSponsor ? club.SponsorFixedSeason : 0,
        club.Seats,
        club.Fans.Fans,
        club.Fans.MoodBp,
        club.SnackLevel,
        club.MerchLevel);

    private static AiTemperament TemperamentOf(ulong seed, int ordinal)
    {
        var draws = new Pcg32(DeterministicDigest.SeedOf(
            "econ-temperament",
            seed.ToString(CultureInfo.InvariantCulture),
            ordinal.ToString(CultureInfo.InvariantCulture)));

        return Enum.GetValues<AiTemperament>()[draws.NextInt(Enum.GetValues<AiTemperament>().Length)];
    }

    // ---- players ----------------------------------------------------------------------------------------------------------------------

    private SimPlayer FromGenerated(Player player, PlayerAttributes attributes, PlayerState state, bool gem)
    {
        var id = _nextPlayerId++;
        var guid = MakeGuid(4, id);

        return new SimPlayer
        {
            Id = id,
            Guid = guid,
            Position = player.PrimaryPosition,
            Secondary = [.. player.SecondaryPositions],
            Attr = [.. attributes.ToSet().Values],
            Potential = player.Potential,
            BirthYear = player.BirthGameYear,
            Aptitude = TrainingAptitude.For(guid),
            ConditionBp = state.ConditionBp,
            FatigueBp = state.FatigueBp,
            MoraleBp = state.MoraleBp,
            SharpnessBp = state.MatchSharpnessBp,
            IsGem = gem,
        };
    }

    /// <summary>The matchdays the first sandbox counted appearances in (a season of 18 clubs).</summary>
    private const int ReferenceMatchdays = 34;

    /// <summary>The appearances of the squad's rank-th best player: 30, 12 and 2 of 34 for a starter, the bench and the rest, kept as shares of the season.</summary>
    public static int AppearancesFor(int rank) => ((Matchdays * (rank < 11 ? 30 : rank < 18 ? 12 : 2)) + (ReferenceMatchdays / 2)) / ReferenceMatchdays;
}
