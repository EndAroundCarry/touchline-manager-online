using System.Globalization;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.Squad.Training;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.SimulationBenchmarks;

// The economy sandbox (plan toasty-scott, Milestone 0): one country of four tiers of 18 clubs, ten seasons, in weekly steps.
//
// It calls the game's own pure rule code (fans, stadium, structures, valuation, renewal quote, daily training, retirement, the AI contract and
// market policies, the generators, promotion and relegation) and replaces only what is not pure: the match engine is replaced by the fans' own
// win/draw/loss curve (FanRuleSet, fitted to 1,800 tick-engine matches), the database by the classes in EconModel.cs, and the auction by a
// highest-bid-wins round. The money formulas go through IEconRules so the same world can be run on the v2 rules in Milestone 1.
//
// One simulated week = one matchday. A week has three market rounds (a listing is open for at least one round, about two and a half days, like the
// game's 48-hour minimum), then the matchday, then the Sunday finance run and the fans' weekly review.
internal sealed partial class EconSim
{
    public const int Tiers = 4;
    public const int ClubsPerTier = 18;
    public const int Weeks = 34;
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

    public EconSim(IEconRules rules, ulong seed, int seasons, int youthPerClub = EconSim.DefaultYouthPerAiClub, int trainingPercent = 100)
    {
        Rules = rules;
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
                Cash = rules.OpeningCash(tier),
                Fans = FanRuleSet.OpeningFor(tier),
                Seats = new StadiumSeats(
                    StadiumRuleSet.OpeningStanding,
                    StadiumRuleSet.OpeningSeating,
                    StadiumRuleSet.OpeningCoveredSeating,
                    StadiumRuleSet.OpeningVip),
            };

            Clubs.Add(club);

            var request = new SquadGenerationRequest(
                _seedText, NamePool, CountryCode, _worldId, club.Guid, ordinal, tier, MakeGuid(2, 1), 1, GameYearBase, Now);

            foreach (var member in PlayerGenerator.GenerateSquad(request))
            {
                var player = FromGenerated(member.Player, member.Attributes, member.State, gem: false);

                player.Club = club;
                player.Wage = Rules.GeneratedWagePerWeek(player.Ability, tier);
                player.ContractEnd = member.Contract.EndSeasonNumber;
                player.Status = member.Contract.SquadStatus;
                club.Squad.Add(player);
                Opening.Add((player.AgeIn(GameYearBase), player.Ability, player.Potential, player.OwnSkill));
            }
        }
    }

    /// <summary>Each player's age, skill and ceiling when the world opened, for the age profile in the report.</summary>
    public List<(int Age, int Ability, int Potential, double Own)> Opening { get; } = [];

    public IEconRules Rules { get; }

    public ulong Seed { get; }

    public int Seasons { get; }

    /// <summary>The intake the sandbox gives each computer club by default: 2 holds a squad at the AI's target of 23 (milestone 0, with training v4).</summary>
    public const int DefaultYouthPerAiClub = 2;

    /// <summary>The young players each computer club takes in at every rollover (0 is the game today).</summary>
    public int YouthPerClub { get; }

    /// <summary>How fast players develop, as a percentage of today's (100). A what-if: it scales the aptitude the daily progression reads, so decline is untouched.</summary>
    public int TrainingPercent { get; }

    public int Season { get; private set; }

    public int Week { get; private set; }

    public int RoundInWeek { get; private set; }

    public int GameYear => GameYearBase + Season - 1;

    public int Round => ((((Season - 1) * Weeks) + Week) * Rounds) + RoundInWeek;

    public List<SimClub> Clubs { get; } = [];

    public IEnumerable<(int Age, int Ability, int Potential, double Own)> Closing =>
        Clubs.SelectMany(club => club.Squad).Select(player => (player.AgeIn(GameYear), player.Ability, player.Potential, player.OwnSkill));

    public List<SimListing> Listings { get; } = [];

    public List<ClubSeasonRow> Rows { get; } = [];

    public List<TransferRecord> Transfers { get; } = [];

    public List<TradeRecord> Trades { get; } = [];

    public List<string> Log { get; } = [];

    /// <summary>The cleaned-up record of a clean start, for the report.</summary>
    public List<string> CleanStarts { get; } = [];

    public static Guid MakeGuid(int kind, int a, int b = 0)
    {
        var bytes = new byte[16];

        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), kind);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), a);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), b);

        return new Guid(bytes);
    }

    public void Note(SimClub club, string text) =>
        Log.Add(string.Create(CultureInfo.InvariantCulture, $"s{Season:00} w{Week + 1:00} {club.Name}: {text}"));

    public IEnumerable<SimClub> Tier(int tier) => Clubs.Where(club => club.Tier == tier);

    public void Run()
    {
        for (Season = 1; Season <= Seasons; Season++)
        {
            StartSeason();

            for (Week = 0; Week < Weeks; Week++)
            {
                RunWeek();
            }

            EndSeason();
        }

        Season = Seasons;
        Week = Weeks - 1;
    }

    // ---- the season -------------------------------------------------------------------------------------------------------------------

    private void StartSeason()
    {
        foreach (var club in Clubs)
        {
            club.ResetSeason();
            club.ShadowMin = club.ShadowCash;
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

        foreach (var club in Clubs.Where(club => club.Manager is not null))
        {
            club.Manager!.OnSeasonStart(this, club);
        }
    }

    private void RunWeek()
    {
        for (RoundInWeek = 0; RoundInWeek < Rounds; RoundInWeek++)
        {
            if (RoundInWeek == 0)
            {
                TrainWeek();
                ResolvePool();
                OpenPool();
            }

            ResolveListings();

            foreach (var club in Clubs.Where(club => club.Manager is not null))
            {
                club.Manager!.Act(this, club, Week, RoundInWeek);
            }

            AiMarketRound();
        }

        RoundInWeek = Rounds - 1;
        ComputeStrengths();
        PlayMatchday();

        foreach (var club in Clubs)
        {
            SettleWeek(club);
        }

        FanReview();

        foreach (var club in Clubs.Where(club => club.Manager is not null))
        {
            club.Manager!.OnWeekEnd(this, club, Week);
        }
    }

    // ---- training ---------------------------------------------------------------------------------------------------------------------

    private void TrainWeek()
    {
        var from = TrainingAgeCurve.TrainingDaysPerSeason * Week / Weeks;
        var to = TrainingAgeCurve.TrainingDaysPerSeason * (Week + 1) / Weeks;

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
            club.Strength = TeamStrength.OfSquad([.. club.Squad.Select(player => player.ToAi())]);
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

            var weeks = new List<(SimClub, SimClub)>[Weeks];
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
            foreach (var (home, away) in _fixtures[tier][Week])
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
        var gate = Rules.Gate(home.Seats, home.Tier, home.Fans.Fans, home.Fans.MoodBp);
        var spectators = Rules.Attendance(home.Seats, home.Fans.Fans, home.Fans.MoodBp);
        var bonus = StructureRuleSet.ResultBonusFor(goalsHome, goalsAway, expectedHome);
        var snack = Rules.SnackTakings(home.SnackLevel, spectators, bonus.Bp);

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

        var fixtureId = MakeGuid(3, Season, (Week * 1000) + home.Idx);

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
            Week.ToString(CultureInfo.InvariantCulture),
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

    // ---- the Sunday finance run and the fans' review -----------------------------------------------------------------------------------

    private void SettleWeek(SimClub club)
    {
        var books = club.Books;
        var fixedIncome = Rules.FixedIncomePerWeek(club.Tier);
        var merch = Rules.MerchTakings(club.MerchLevel, club.Fans.Fans, FanMoods.WordFor(club.Fans.MoodBp));

        club.Cash += fixedIncome + merch;
        books.Fixed += fixedIncome;
        books.Merch += merch;

        var wages = club.WageBill;
        var operating = Rules.OperatingPerWeek(club.Tier);
        var upkeep = Rules.Upkeep(club.SnackLevel, club.Tier) + Rules.Upkeep(club.MerchLevel, club.Tier);
        var outgoings = wages + operating + upkeep;

        // The week's credit is in the account before the guard reads it; what cannot be paid is granted (FIN-16).
        var limit = Rules.OverdraftLimit(ForecastSeasonIncome(club));
        var shortfall = outgoings - (club.Available + limit);

        if (shortfall > 0)
        {
            club.Cash += shortfall;
            club.GrantsTotal += shortfall;
            books.Grants += shortfall;
        }

        club.Cash -= outgoings;
        books.Wages += wages;
        books.Operating += operating;
        books.Upkeep += upkeep;

        foreach (var player in club.Squad.Where(player => player.BoughtFee > 0))
        {
            player.WagesSinceBought += player.Wage;
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
                club.Fans = FanRuleSet.AfterWeek(club.Fans, places[club] - club.TargetRank, club.Played, Weeks);
            }
        }
    }

    public List<SimClub> Table(int tier) => [.. Tier(tier)
        .OrderByDescending(club => club.Points)
        .ThenByDescending(club => club.GoalsFor - club.GoalsAgainst)
        .ThenByDescending(club => club.GoalsFor)
        .ThenBy(club => club.Idx)];

    // ---- what a club can expect -------------------------------------------------------------------------------------------------------

    /// <summary>A season's income from now on the club's present fans and ground: the league and sponsor money, the gate and snack bar of 17 home matches and the shop.</summary>
    public long ForecastSeasonIncome(SimClub club)
    {
        var fans = club.Fans.Fans;
        var mood = club.Fans.MoodBp;
        var spectators = Rules.Attendance(club.Seats, fans, mood);

        return (Rules.FixedIncomePerWeek(club.Tier) * Weeks)
            + (Rules.Gate(club.Seats, club.Tier, fans, mood) * (Weeks / 2))
            + (Rules.SnackTakings(club.SnackLevel, spectators, 0) * (Weeks / 2))
            + (Rules.MerchTakings(club.MerchLevel, fans, FanMoods.WordFor(mood)) * Weeks);
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

    public static int AppearancesFor(int rank) => rank < 11 ? 30 : rank < 18 ? 12 : 2;
}
