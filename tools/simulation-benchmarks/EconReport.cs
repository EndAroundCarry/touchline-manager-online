using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.SimulationBenchmarks;

// Runs the economy sandbox for several seeds and writes the season tables (CSV), and a report that reads the 13 targets of plan toasty-scott
// against what the run found. Amounts in the report are the amounts a manager would read: minor units divided by 100.
internal static class EconReport
{
    /// <summary>"The best player there is": the plan's round figure for the fee that marks financial independence, in minor units.</summary>
    private const long BestPlayerFeeMinor = 10_000_000_000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly (int Low, int High, string Label)[] FeeBands = [(1, 8, "up to 8"), (9, 11, "9-11"), (12, 14, "12-14"), (15, 17, "15-17"), (18, 20, "18-20")];

    private static readonly (int Low, int High, string Label)[] AgeBands = [(17, 18, "17-18"), (19, 20, "19-20"), (21, 22, "21-22"), (23, 24, "23-24"), (25, 27, "25-27"), (28, 30, "28-30"), (31, 40, "31+")];

    private sealed record Result(ulong Seed, EconSim Sim, double Seconds);

    public static void Run(int seeds, ulong firstSeed, int seasons, string outDir, int youthPerClub = EconSim.DefaultYouthPerAiClub, int trainingPercent = 100)
    {
        Directory.CreateDirectory(outDir);

        var results = new List<Result>();
        var total = Stopwatch.StartNew();

        for (var index = 0; index < seeds; index++)
        {
            var seed = firstSeed + (ulong)index;
            var watch = Stopwatch.StartNew();
            var sim = new EconSim(seed, seasons, youthPerClub, trainingPercent);

            sim.Run();
            results.Add(new Result(seed, sim, watch.Elapsed.TotalSeconds));
            Console.WriteLine(string.Create(Inv, $"econ seed {seed}: {seasons} seasons in {watch.Elapsed.TotalSeconds:0.0}s, {sim.Transfers.Count} transfers, {sim.Trades.Count} trades"));
        }

        WriteClubsCsv(Path.Combine(outDir, "econ-clubs.csv"), results);
        WriteTiersCsv(Path.Combine(outDir, "econ-tiers.csv"), results, seasons);

        var markdown = Markdown(results, seasons, total.Elapsed.TotalSeconds, youthPerClub, trainingPercent);

        File.WriteAllText(Path.Combine(outDir, "econ-report.md"), markdown, new UTF8Encoding(false));
        Console.WriteLine(string.Create(Inv, $"econ: {seeds} seeds x {seasons} seasons in {total.Elapsed.TotalSeconds:0.0}s -> {Path.GetFullPath(outDir)}"));
    }

    // ---- CSV --------------------------------------------------------------------------------------------------------------------------

    private static void WriteClubsCsv(string path, List<Result> results)
    {
        var text = new StringBuilder();

        text.AppendLine("seed,season,club,manager,tier,rank,points,fans,capacity,snack_level,merch_level,tv,sponsor,bonus,parachute,gate,snack,merch,prize,fees_received,wages,operating,upkeep,fees_paid,building,upgrades,grants,gifts,cash_end,lowest_cash,squad,mean_ability,players_15,players_17,players_19,wage_bill_season,forecast_income,wage_share,wage_share_before_prize,sold_out_share,cash_start,standing,sponsor_kind,objective,outcome,entered_red,entered_admin,red_matchdays,admin_matchdays,dismissals,board_sales,board_releases,reconciled");

        foreach (var row in results.SelectMany(result => result.Sim.Rows))
        {
            var books = row.Books;

            text.AppendLine(string.Create(
                Inv,
                $"{row.Seed},{row.Season},{row.Club},{row.Manager},{row.Tier},{row.Rank},{row.Points},{row.Fans},{row.Capacity},{row.SnackLevel},{row.MerchLevel},{books.Tv},{books.Sponsor},{books.Bonus},{books.Parachute},{books.Gate},{books.Snack},{books.Merch},{books.Prize},{books.FeesReceived},{books.Wages},{books.Operating},{books.Upkeep},{books.FeesPaid},{books.Building},{books.Upgrades},{books.Grants},{books.Gifts},{row.CashEnd},{row.ShadowMin},{row.SquadSize},{row.MeanAbility:0.00},{row.Players15},{row.Players17},{row.Players19},{row.WageBill},{row.ForecastIncome},{row.WageShare:0.000},{row.WageShareBeforePrize:0.000},{(books.HomeMatches == 0 ? 0 : (double)books.SoldOut / books.HomeMatches):0.00},{row.CashStart},{row.StandingEnd},{row.Sponsor},{row.Objective},{row.Outcome},{books.EnteredRed},{books.EnteredAdmin},{books.RedMatchdays},{books.AdminMatchdays},{books.Dismissals},{books.BoardSales},{books.BoardReleases},{(row.Reconciled ? 1 : 0)}"));
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static void WriteTiersCsv(string path, List<Result> results, int seasons)
    {
        var text = new StringBuilder();

        text.AppendLine("season,tier,clubs,income_median,tv_median,sponsor_median,gate_median,snack_median,merch_median,prize_median,wages_median,wage_share_median,share_over_100,share_over_70,cash_p10,cash_p50,cash_p90,lowest_cash_p10,share_red,share_admin,clubs_with_grants,players_15_mean,players_17_mean,players_19_mean,top5_income_over_median,top5_income_over_median_ai,sold_out_share,capacity_median,squad_mean,skill_mean,standing_mean");

        var rows = results.SelectMany(result => result.Sim.Rows).ToList();

        for (var season = 1; season <= seasons; season++)
        {
            for (var tier = 1; tier <= EconSim.Tiers; tier++)
            {
                var group = rows.Where(row => row.Season == season && row.Tier == tier).ToList();

                if (group.Count == 0)
                {
                    continue;
                }

                text.AppendLine(string.Create(
                    Inv,
                    $"{season},{tier},{group.Count},{Median(group, row => row.Books.Income):0},{Median(group, row => row.Books.Tv):0},{Median(group, row => row.Books.Sponsor):0},{Median(group, row => row.Books.Gate):0},{Median(group, row => row.Books.Snack):0},{Median(group, row => row.Books.Merch):0},{Median(group, row => row.Books.Prize):0},{Median(group, row => row.Books.Wages):0},{Median(group, row => row.WageShare):0.000},{Share(group, row => row.WageShare > 1.0):0.000},{Share(group, row => row.WageShare > 0.7):0.000},{Percentile(group, row => row.CashEnd, 0.1):0},{Percentile(group, row => row.CashEnd, 0.5):0},{Percentile(group, row => row.CashEnd, 0.9):0},{Percentile(group, row => row.ShadowMin, 0.1):0},{Share(group, row => row.Books.EnteredRed > 0 || row.Books.RedMatchdays > 0):0.000},{Share(group, row => row.Books.AdminMatchdays > 0):0.000},{Share(group, row => row.Books.Grants > 0):0.000},{group.Average(row => row.Players15):0.00},{group.Average(row => row.Players17):0.00},{group.Average(row => row.Players19):0.00},{TopOverMedian(results, season, tier):0.00},{TopOverMedian(results, season, tier, aiOnly: true):0.00},{SoldOut(group):0.000},{Median(group, row => row.Capacity):0},{group.Average(row => row.SquadSize):0.0},{group.Average(row => row.MeanAbility):0.00},{group.Average(row => row.StandingEnd):0.0}"));
            }
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    // ---- the report -------------------------------------------------------------------------------------------------------------------

    private static string Markdown(List<Result> results, int seasons, double seconds, int youthPerClub, int trainingPercent)
    {
        var rows = results.SelectMany(result => result.Sim.Rows).ToList();
        var text = new StringBuilder();

        text.AppendLine(string.Create(Inv, $"# Economy sandbox: {EconRules.Name}"));
        text.AppendLine();
        text.AppendLine(string.Create(
            Inv,
            $"{results.Count} seed(s) ({string.Join(", ", results.Select(result => result.Seed))}), {seasons} seasons, one country of {EconSim.Tiers} tiers of {EconSim.ClubsPerTier} clubs, {seconds:0} s in all. Amounts are what a manager reads (minor units divided by 100); a season is {EconSim.Matchdays} matchdays and {EconSim.RunsPerSeason} weekly runs. Youth intake: {(youthPerClub == 0 ? "none" : $"{youthPerClub} players of 17-18 per computer club at each rollover")}. Player development speed: {trainingPercent}% of the rules'."));
        text.AppendLine();

        var unbalanced = rows.Count(row => !row.Reconciled);

        if (unbalanced > 0)
        {
            Environment.ExitCode = 1;
        }

        text.AppendLine(unbalanced == 0
            ? string.Create(Inv, $"Ledger check: the books of all {rows.Count} club-seasons explain every movement of cash.")
            : string.Create(Inv, $"**Ledger check failed: {unbalanced} of {rows.Count} club-seasons do not reconcile.**"));
        text.AppendLine();

        var targets = Targets(results, rows, seasons);

        Summary(text, targets);
        Prices(text);
        SeasonTable(text, results, rows, seasons);
        FeeTable(text, results, seasons);
        AgeProfile(text, results);
        Archetypes(text, results, rows, seasons);
        Assumptions(text);

        Console.WriteLine();

        foreach (var target in targets)
        {
            Console.WriteLine(string.Create(Inv, $"{(target.Hit ? "hit   " : "MISSED")} {target.Number,2}. {target.Short}: {target.Console}"));
        }

        return text.ToString();
    }

    private sealed record TargetResult(int Number, string Text, string Short, string Measured, string Console, bool Hit);

    private static void Summary(StringBuilder text, List<TargetResult> targets)
    {
        text.AppendLine("## 1. The 13 targets");
        text.AppendLine();
        text.AppendLine("| # | Target | What the run measured | Verdict |");
        text.AppendLine("|---|---|---|---|");

        foreach (var target in targets)
        {
            text.AppendLine($"| {target.Number} | {target.Text} | {target.Measured} | {(target.Hit ? "hit" : "missed")} |");
        }

        text.AppendLine();
    }

    private static void Prices(StringBuilder text)
    {
        text.AppendLine("## 2. What things cost");
        text.AppendLine();
        text.AppendLine("A player's season wage and his value at 26 (the peak), by the mean of his attributes. The same in every tier.");
        text.AppendLine();
        text.AppendLine("| Skill | Season wage | Value at 26 | Value at 19 | Value at 31 |");
        text.AppendLine("|---|---|---|---|---|");

        foreach (var skill in new[] { 5, 8, 10, 11.5, 13, 15, 17, 18, 19, 20 })
        {
            var tenths = (int)Math.Round(skill * 10);

            text.AppendLine(string.Create(
                Inv,
                $"| {skill:0.#} | {Money(EconomyRuleSet.SeasonWageMinorForSkill(tenths))} | {Money(EconRules.Value(tenths, 26))} | {Money(EconRules.Value(tenths, 19))} | {Money(EconRules.Value(tenths, 31))} |"));
        }

        text.AppendLine();
        text.AppendLine("A fresh club of each tier: its season income at the opening fans and ground, the league and sponsor money, the prize for a mid-table place, and its opening cash.");
        text.AppendLine();
        text.AppendLine("| Tier | Forecast income | League TV | Steady sponsor | Champion | {EconSim.ClubsPerTier / 2}th place | {EconSim.ClubsPerTier}th place | Opening cash | Operating cost |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|");

        for (var tier = 1; tier <= 5; tier++)
        {
            text.AppendLine(string.Create(
                Inv,
                $"| {tier} | {Money(EconomyRuleSet.FreshClubForecastMinor(tier))} | {Money(EconomyRuleSet.LeagueTvSeasonMinorFor(tier))} | {Money(EconomyRuleSet.SponsorBaseSeasonMinorFor(tier))} | {Money(EconRules.Prize(tier, 1))} | {Money(EconRules.Prize(tier, EconSim.ClubsPerTier / 2))} | {Money(EconRules.Prize(tier, EconSim.ClubsPerTier))} | {Money(EconRules.OpeningCash(tier))} | {Money(EconomyRuleSet.OperatingCostSeasonMinorFor(tier))} |"));
        }

        text.AppendLine();
    }

    private static void SeasonTable(StringBuilder text, List<Result> results, List<ClubSeasonRow> rows, int seasons)
    {
        text.AppendLine("## 3. Season by season, tier by tier (computer clubs only)");
        text.AppendLine();
        text.AppendLine("Median over clubs and seeds; the cash columns are the 10th, 50th and 90th percentile at the season's end, after the prize money. \"Lowest cash\" is the 10th percentile of the lowest cash a club reached in the season. \"Red\" and \"Admin\" are the shares of clubs that were in the red or in administration on any matchday of the season; \"Bailed out\" the share the emergency grant (the integrity net) had to help. \"Sold out\" is the share of home matches where more people wanted to come than the ground held.");
        text.AppendLine();
        text.AppendLine("| Season | Tier | Income | Wages / income | Over 100% | Over 70% | Cash p10 / p50 / p90 | Lowest cash p10 | Red | Admin | Bailed out | 15+ per club | 17+ per club | Squad / mean skill | Ground | Sold out | Standing | Top 5 income / median |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        for (var season = 1; season <= seasons; season++)
        {
            for (var tier = 1; tier <= EconSim.Tiers; tier++)
            {
                var group = rows.Where(row => row.Season == season && row.Tier == tier && row.Manager == "AI").ToList();

                if (group.Count == 0)
                {
                    continue;
                }

                text.AppendLine(string.Create(
                    Inv,
                    $"| {season} | {tier} | {Money(Median(group, row => row.Books.Income))} | {Median(group, row => row.WageShare):P0} | {Share(group, row => row.WageShare > 1.0):P0} | {Share(group, row => row.WageShare > 0.7):P0} | {Money(Percentile(group, row => row.CashEnd, 0.1))} / {Money(Percentile(group, row => row.CashEnd, 0.5))} / {Money(Percentile(group, row => row.CashEnd, 0.9))} | {Money(Percentile(group, row => row.ShadowMin, 0.1))} | {Share(group, row => row.Books.RedMatchdays > 0):P0} | {Share(group, row => row.Books.AdminMatchdays > 0):P0} | {Share(group, row => row.Books.Grants > 0):P0} | {group.Average(row => row.Players15):0.0} | {group.Average(row => row.Players17):0.00} | {group.Average(row => row.SquadSize):0.0} / {group.Average(row => row.MeanAbility):0.0} | {Median(group, row => row.Capacity):N0} | {SoldOut(group):P0} | {group.Average(row => row.StandingEnd):0} | {TopOverMedian(results, season, tier, aiOnly: true):0.0}x |"));
            }
        }

        text.AppendLine();
    }

    // ---- the targets ------------------------------------------------------------------------------------------------------------------

    private static List<TargetResult> Targets(List<Result> results, List<ClubSeasonRow> rows, int seasons)
    {
        var targets = new List<TargetResult>();
        var ai = rows.Where(row => row.Manager == "AI").ToList();

        // 1. wages matter.
        var shares = Enumerable.Range(1, EconSim.Tiers)
            .Select(tier => (tier, share: Median(ai.Where(row => row.Season == 1 && row.Tier == tier).ToList(), row => row.WageShare)))
            .ToList();
        var worstOver = Enumerable.Range(1, seasons)
            .SelectMany(season => Enumerable.Range(1, EconSim.Tiers).Select(tier => Share(ai.Where(row => row.Season == season && row.Tier == tier).ToList(), row => row.WageShare > 1.0)))
            .Max();
        var wagesHit = shares.All(entry => entry.share is >= 0.55 and <= 0.75) && worstOver < 0.05;
        var shareText = string.Join(", ", shares.Select(entry => string.Create(Inv, $"tier {entry.tier} {entry.share:P0}")));

        targets.Add(new TargetResult(
            1,
            "Wages are 55-75% of income in every tier in season 1, and under 5% of computer clubs are over 100% in any season",
            "wages",
            $"season 1 median: {shareText}; worst share of clubs over 100% in any season and tier: {worstOver:P0}",
            $"{shareText}; worst over-100% share {worstOver:P0}",
            wagesHit));

        // 2 and 3. a big spender.
        var spender3 = rows.Where(row => row.Manager == "Tier-3 big spender" && row.Season == 1).ToList();
        var spender4 = rows.Where(row => row.Manager == "Tier-4 spender" && row.Season == 1).ToList();
        var red3 = Share(spender3, row => row.Books.RedMatchdays > 0);
        var admin3 = Share(spender3, row => row.Books.AdminMatchdays > 0 || row.Books.Dismissals > 0);
        var stars3 = spender3.Count == 0 ? 0 : spender3.Average(row => row.Players15);

        targets.Add(new TargetResult(
            2,
            "Tier 3 signing three players of 15+ goes into the red within the season, with administration likely",
            "tier-3 spender",
            string.Create(Inv, $"season 1 with {stars3:0.0} players of 15+ on average: in the red in {red3:P0} of {spender3.Count} seeds, in administration in {admin3:P0}; wages / income {(spender3.Count == 0 ? 0 : spender3.Average(row => row.WageShare)):P0}"),
            string.Create(Inv, $"red {red3:P0}, administration {admin3:P0}, wages/income {(spender3.Count == 0 ? 0 : spender3.Average(row => row.WageShare)):P0}"),
            spender3.Count > 0 && red3 >= 0.8 && admin3 >= 0.5));

        var wage4 = spender4.Count == 0 ? 0 : spender4.Average(row => row.WageShare);

        targets.Add(new TargetResult(
            3,
            "Tier 4: one 15+ player pushes wages above 100% of income",
            "tier-4 spender",
            string.Create(Inv, $"season 1 with {(spender4.Count == 0 ? 0 : spender4.Average(row => row.Players15)):0.0} players of 15+: wages / income {wage4:P0}"),
            string.Create(Inv, $"wages/income {wage4:P0}"),
            wage4 > 1.0));

        // 4. tier 2 and 5-6 stars: how many players of 15+ the club could carry at 100% of income.
        var star = EconomyRuleSet.SeasonWageMinorForSkill(150);
        var tier2 = new List<string>();
        var season1Median = 0.0;
        var grownTop = 0.0;

        foreach (var season in Enumerable.Range(1, seasons).Where(season => season is 1 or 3 or 6 or 10))
        {
            var group = ai.Where(row => row.Season == season && row.Tier == 2).ToList();

            if (group.Count == 0)
            {
                continue;
            }

            var median = StarsWithin(Median(group, row => row.Books.Income), group, star);
            var top = StarsWithin(Percentile(group, row => row.Books.Income, 0.75), group, star);

            season1Median = season == 1 ? median : season1Median;
            grownTop = Math.Max(grownTop, season == 1 ? 0 : top);
            tier2.Add(string.Create(Inv, $"season {season}: median club {median:0.0} stars, top quartile {top:0.0}"));
        }

        targets.Add(new TargetResult(
            4,
            "Tier 2 sustains 5-6 players of 15+ only as a grown club (top-quartile income), never in season 1",
            "tier-2 stars",
            $"how many more players of 15+ a club could carry before wages pass 100% of income, beside a median squad: {string.Join("; ", tier2)}. A 15-rated player costs {Money(star)} a season",
            string.Join("; ", tier2),
            season1Median < 5 && grownTop >= 5));

        // 5. a mature tier-1 club.
        var mature = ai.Where(row => row.Tier == 1 && row.Season >= 3 && row.Capacity >= 25_000).ToList();
        var wage19 = EconomyRuleSet.SeasonWageMinorForSkill(190);
        var room = mature.Count == 0 ? 0 : mature.Average(row => Math.Max(0, ((0.75 * row.Books.Income) - row.Books.Wages) / wage19));

        targets.Add(new TargetResult(
            5,
            "A mature tier-1 club (ground 25-30k or more, season 3 or later) carries 3-4 players of 19-20 at 75% wages or less",
            "mature club",
            string.Create(Inv, $"{mature.Count} computer club-seasons with a ground of 25,000 or more: income {(mature.Count == 0 ? "-" : Money(mature.Average(row => (double)row.Books.Income)))}, wages {(mature.Count == 0 ? 0 : mature.Average(row => row.WageShare)):P0}, room for {room:0.0} more players of 19 at 75%. A 19-rated player costs {Money(wage19)} a season"),
            string.Create(Inv, $"{mature.Count} club-seasons, room for {room:0.0} players of 19"),
            mature.Count > 0 && room is >= 3 and <= 12));

        // 6. elite.
        var eliteLines = new List<string>();
        var firstRich = 0;
        var perLeague = new List<int>();
        var multiples = new List<double>();

        foreach (var season in Enumerable.Range(1, seasons))
        {
            foreach (var result in results)
            {
                var tier1 = result.Sim.Rows.Where(row => row.Season == season && row.Tier == 1).ToList();
                var rich = tier1.Count(row => CanPay(row, BestPlayerFeeMinor));

                if (rich > 0 && firstRich == 0)
                {
                    firstRich = season;
                }

                if (season == seasons)
                {
                    perLeague.Add(rich);
                }
            }
        }

        foreach (var season in new[] { 5, 8, seasons }.Distinct().Where(season => season <= seasons))
        {
            var tier1 = rows.Where(row => row.Season == season && row.Tier == 1).ToList();
            var rich = tier1.Count(row => CanPay(row, BestPlayerFeeMinor));

            eliteLines.Add(string.Create(Inv, $"season {season}: {rich / (double)results.Count:0.0} tier-1 clubs a league could pay 100M and keep half a season of wages"));
            multiples.Add(TopOverMedian(results, season, 1));
        }

        var finalCash = results.Select(result => result.Sim.Rows.Where(row => row.Season == seasons && row.Tier == 1).OrderByDescending(row => row.CashEnd).Take(5).Select(row => row.CashEnd).ToList()).ToList();
        var topCash = finalCash.Count == 0 ? 0 : finalCash.Average(cash => cash.Count == 0 ? 0 : cash.Average());

        targets.Add(new TargetResult(
            6,
            "From season 10 the 3-5 dominant tier-1 clubs of a league can pay 100M and keep a reserve; no club can before season 8; their income is 2.5x the tier median or more",
            "elite",
            $"{string.Join("; ", eliteLines)}. First season any club could: {(firstRich == 0 ? "never" : firstRich.ToString(Inv))}. Clubs that could in the last season, per league: {string.Join("/", perLeague)}. Top 5 income / median in the last season: {multiples[^1]:0.0}x. The five richest hold {Money(topCash)} on average",
            $"first {(firstRich == 0 ? "never" : firstRich.ToString(Inv))}, last season per league {string.Join("/", perLeague)}, income {multiples[^1]:0.0}x median, richest five {Money(topCash)}",
            (firstRich == 0 || firstRich >= 8) && perLeague.Count > 0 && perLeague.Average() is >= 3 and <= 5.5 && multiples[^1] >= 2.5));

        // 7. mobility.
        var entered = new List<double>();

        foreach (var result in results)
        {
            for (var tier = 1; tier <= EconSim.Tiers; tier++)
            {
                entered.Add(EnteredTop5(result.Sim.Rows, tier, seasons));
            }
        }

        targets.Add(new TargetResult(
            7,
            "At least one club enters a league's top 5 by income between seasons 5 and 10",
            "mobility",
            string.Create(Inv, $"per league and seed, on average {entered.Average():0.0} clubs enter the top 5 by income after season 4"),
            string.Create(Inv, $"{entered.Average():0.0} clubs enter a top 5"),
            entered.Average() >= 1));

        // 8. trading.
        var trades = results.SelectMany(result => result.Sim.Trades).ToList();
        var gems = trades.Where(trade => trade.Gem).ToList();
        var returnLine = trades.Count == 0 ? "no completed trade" : string.Create(Inv, $"{trades.Count} completed trades ({trades.Count(trade => trade.FromPool)} bought from the free-agent pool), median return {Median(trades, trade => trade.ReturnPercent):0}% of the fee after wages; best {trades.Max(trade => trade.ReturnPercent):0}%");
        var gemLine = gems.Count == 0 ? "no gem (ceiling 16+) was bought and sold" : string.Create(Inv, $"{gems.Count} gems, median fee out / fee in {Median(gems, trade => trade.Multiple):0.0}x");

        targets.Add(new TargetResult(
            8,
            "The trader's median completed trade returns 50% or more after wages; a gem bought young returns 5x his fee",
            "trading",
            $"{returnLine}; {gemLine}",
            $"{returnLine}; {gemLine}",
            trades.Count > 0 && Median(trades, trade => trade.ReturnPercent) >= 50 && gems.Count > 0 && Median(gems, trade => trade.Multiple) >= 5));

        // 9. inflation, administration, grants.
        var transfers = results.SelectMany(result => result.Sim.Transfers).Where(transfer => !transfer.Pool).ToList();
        var drifts = new List<string>();
        var drifted = false;

        // The fee is read against the value the rules give the player: the mix of ages and skills that change hands moves from the first seasons to the
        // last (older players are listed first), and that is not inflation. The raw drift of the fee is kept beside it.
        var rawDrifts = new List<string>();

        foreach (var (low, high, label) in new[] { (9, 11, "skill 9-11"), (12, 14, "skill 12-14"), (15, 17, "skill 15-17"), (18, 20, "skill 18-20") })
        {
            var early = transfers.Where(transfer => transfer.Season <= 3 && transfer.Ability >= low && transfer.Ability <= high).ToList();
            var late = transfers.Where(transfer => transfer.Season >= seasons - 2 && transfer.Ability >= low && transfer.Ability <= high).ToList();

            if (early.Count >= 10 && late.Count >= 10)
            {
                var drift = (Median(late, transfer => (double)transfer.Fee / Math.Max(1, transfer.Value)) / Median(early, transfer => (double)transfer.Fee / Math.Max(1, transfer.Value))) - 1;

                drifted |= Math.Abs(drift) > 0.25;
                drifts.Add(string.Create(Inv, $"{label} {drift:+0%;-0%}"));
                rawDrifts.Add(string.Create(Inv, $"{label} {(Median(late, transfer => (double)transfer.Fee) / Median(early, transfer => (double)transfer.Fee)) - 1:+0%;-0%}"));
            }
        }

        var adminRate = Share(ai, row => row.Books.AdminMatchdays > 0);
        var grantRate = Share(ai, row => row.Books.Grants > 0);

        targets.Add(new TargetResult(
            9,
            "Median fee per skill band drifts less than 25% over the run; under 2% of computer clubs a season go into administration; the emergency grant never fires",
            "inflation",
            $"fee as a share of the player's value, drift between the first three and last three seasons: {(drifts.Count == 0 ? "too few transfers" : string.Join(", ", drifts))} (the raw median fee moved {(rawDrifts.Count == 0 ? "n/a" : string.Join(", ", rawDrifts))}, a change in who is sold); computer club-seasons with a matchday in administration: {adminRate:P1}; that needed the emergency grant: {grantRate:P1}",
            $"drift {(drifts.Count == 0 ? "n/a" : string.Join(", ", drifts))}; administration {adminRate:P1}; grants {grantRate:P1}",
            !drifted && adminRate < 0.02 && grantRate == 0));

        // 10. relegation.
        var relegated = new List<ClubSeasonRow>();

        foreach (var result in results)
        {
            foreach (var row in result.Sim.Rows.Where(row => row.Season < seasons && row.Tier < EconSim.Tiers && row.Rank > EconSim.ClubsPerTier - WorldRuleSet.RelegatedPerTier))
            {
                var next = result.Sim.Rows.FirstOrDefault(other => other.Season == row.Season + 1 && other.ClubIdx == row.ClubIdx);

                if (next is not null && next.Tier == row.Tier + 1 && row.Manager == "AI")
                {
                    relegated.Add(next);
                }
            }
        }

        var survive = relegated.Count == 0 ? 1.0 : relegated.Count(row => row.Books.AdminMatchdays == 0) / (double)relegated.Count;

        targets.Add(new TargetResult(
            10,
            "A relegated club with a normal squad does not reach administration in its first season down",
            "relegation",
            string.Create(Inv, $"{relegated.Count} relegated computer clubs: median wages / income in their first season down {(relegated.Count == 0 ? 0 : Median(relegated, row => row.WageShare)):P0}; {survive:P0} stayed out of administration, {(relegated.Count == 0 ? 0 : Share(relegated, row => row.Books.RedMatchdays > 0)):P0} were in the red at some point"),
            string.Create(Inv, $"{survive:P0} of {relegated.Count} stay out of administration"),
            relegated.Count > 0 && survive >= 0.95));

        // 11. buildings.
        var shares2 = Enumerable.Range(1, EconSim.Tiers)
            .Select(tier => Median(ai.Where(row => row.Season == 1 && row.Tier == tier).ToList(), row => row.Books.Income <= 0 ? 0 : (double)(row.Books.Snack + row.Books.Merch) / row.Books.Income))
            .ToList();
        var relative = shares2[0] <= 0 ? 0 : shares2[EconSim.Tiers - 1] / shares2[0];
        var shareText2 = string.Join(", ", shares2.Select((share, index) => string.Create(Inv, $"tier {index + 1} {share:P1}")));

        targets.Add(new TargetResult(
            11,
            "The tier-4 to tier-1 relative gain from the snack bar and shop is no longer above 2x",
            "buildings",
            $"share of income from snack bar and shop in season 1: {shareText2}; tier 4 / tier 1 = {relative:0.0}x",
            $"{shareText2}; tier4/tier1 {relative:0.0}x",
            relative <= 2.0));

        // 12. clean start.
        var cleanShares = results.SelectMany(result => result.Sim.CleanStartWageShares).ToList();
        var clean = results.SelectMany(result => result.Sim.CleanStarts.Select(line => $"seed {result.Seed}, {line}")).ToList();
        var cleanRows = rows.Where(row => row.Manager == "Clean-start taker").OrderBy(row => row.Season).ToList();
        var cleanTrack = string.Join(" -> ", Enumerable.Range(EconSim.CleanStartSeason, Math.Max(0, seasons - EconSim.CleanStartSeason + 1))
            .Select(season => cleanRows.Where(row => row.Season == season).ToList())
            .Where(group => group.Count > 0)
            .Select(group => string.Create(Inv, $"tier {group.Average(row => row.Tier):0.0} place {group.Average(row => row.Rank):0}")));

        targets.Add(new TargetResult(
            12,
            "A clean-start squad's wages are 55-65% of the forecast; a clean-start tier-1 club after season 3 is usually relegated, then regrows",
            "clean start",
            cleanShares.Count == 0
                ? "no clean start ran"
                : string.Create(Inv, $"{cleanShares.Count} clean starts: squad wages {cleanShares.Min():P0} to {cleanShares.Max():P0} of a fresh club's forecast; the club's seasons from season {EconSim.CleanStartSeason}: {cleanTrack}"),
            cleanShares.Count == 0 ? "none" : string.Create(Inv, $"wages {cleanShares.Min():P0}-{cleanShares.Max():P0} of forecast; {cleanTrack}"),
            cleanShares.Count > 0 && cleanShares.All(share => share is >= 0.55 and <= 0.65)));

        // 13. prize weight.
        var prize = Enumerable.Range(1, EconSim.Tiers)
            .Select(tier => (tier, share: Median(ai.Where(row => row.Season == 1 && row.Tier == tier).ToList(), row => row.Books.Income <= 0 ? 0 : (double)row.Books.Prize / row.Books.Income)))
            .ToList();
        var eliteRows = rows.Where(row => row.Season == seasons && row.Tier == 1).OrderByDescending(row => row.CashEnd).Take(5).ToList();
        var eliteShare = eliteRows.Count == 0 ? 0 : eliteRows.Average(row => row.Books.Income <= 0 ? 0 : (double)row.Books.Prize / row.Books.Income);
        var prizeText = string.Join(", ", prize.Select(entry => string.Create(Inv, $"tier {entry.tier} {entry.share:P0}")));

        targets.Add(new TargetResult(
            13,
            "Prize money is 15-25% of a fresh club's season income in every tier, and under 10% for the elite by season 10",
            "prize weight",
            $"season 1 median prize / income: {prizeText}; five richest tier-1 clubs in season {seasons}: {eliteShare:P0}",
            $"{prizeText}; elite {eliteShare:P0}",
            prize.All(entry => entry.share is >= 0.15 and <= 0.25) && eliteShare < 0.10));

        _ = clean;

        return targets;
    }

    private static void Archetypes(StringBuilder text, List<Result> results, List<ClubSeasonRow> rows, int seasons)
    {
        text.AppendLine("## 4. The scripted managers");
        text.AppendLine();
        text.AppendLine("Each plays the median club of its tier. One line per season, averaged over the seeds. \"Lowest cash\" is the lowest the club's cash reached; the state columns count the seeds in which the club was in the red or in administration on some matchday of the season, or had its manager dismissed.");
        text.AppendLine();

        foreach (var manager in new[] { "Prudent builder", "Trader", "Over-ambitious", "Tier-3 big spender", "Tier-4 spender", "Clean-start taker" })
        {
            var mine = rows.Where(row => row.Manager == manager).ToList();

            if (mine.Count == 0)
            {
                continue;
            }

            text.AppendLine($"### {manager}");
            text.AppendLine();
            text.AppendLine("| Season | Tier | Place | Income | Wages | Wages / income | Cash at the end | Lowest cash | Red | Admin | Dismissed | Bailed out | 15+ | 19+ | Ground | Snack / shop | Standing | Objective |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            for (var season = 1; season <= seasons; season++)
            {
                var group = mine.Where(row => row.Season == season).ToList();

                if (group.Count == 0)
                {
                    continue;
                }

                text.AppendLine(string.Create(
                    Inv,
                    $"| {season} | {group.Average(row => row.Tier):0.0} | {group.Average(row => row.Rank):0.0} | {Money((long)group.Average(row => row.Books.Income))} | {Money((long)group.Average(row => row.Books.Wages))} | {group.Average(row => row.WageShare):P0} | {Money((long)group.Average(row => row.CashEnd))} | {Money((long)group.Average(row => row.ShadowMin))} | {group.Count(row => row.Books.RedMatchdays > 0)} of {group.Count} | {group.Count(row => row.Books.AdminMatchdays > 0)} | {group.Count(row => row.Books.Dismissals > 0)} | {group.Count(row => row.Books.Grants > 0)} | {group.Average(row => row.Players15):0.0} | {group.Average(row => row.Players19):0.0} | {group.Average(row => row.Capacity):N0} | {group.Average(row => row.SnackLevel):0.0} / {group.Average(row => row.MerchLevel):0.0} | {group.Average(row => row.StandingEnd):0} | {string.Join("/", group.Select(row => row.Outcome).Distinct())} |"));
            }

            text.AppendLine();
        }

        var cleanLines = results.SelectMany(result => result.Sim.CleanStarts.Select(line => $"seed {result.Seed}, {line}")).ToList();

        if (cleanLines.Count > 0)
        {
            text.AppendLine("Clean starts (plan section 8):");
            text.AppendLine();

            foreach (var line in cleanLines)
            {
                text.AppendLine($"- {line}");
            }

            text.AppendLine();
        }

        var trades = results.SelectMany(result => result.Sim.Trades).ToList();

        if (trades.Count > 0)
        {
            text.AppendLine("Completed trades of the trader (first 12):");
            text.AppendLine();
            text.AppendLine("| Bought | Sold | Skill in / out | Ceiling | Fee in | Fee out | Wages paid | Profit | Return |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|");

            foreach (var trade in trades.Take(12))
            {
                text.AppendLine(string.Create(
                    Inv,
                    $"| season {trade.BoughtSeason} | season {trade.SoldSeason} | {trade.AbilityIn} / {trade.AbilityOut} | {trade.PotentialIn} | {Money(trade.FeeIn)} | {Money(trade.FeeOut)} | {Money(trade.Wages)} | {Money(trade.Profit)} | {trade.ReturnPercent:0}% |"));
            }

            text.AppendLine();
        }

        var notes = results.SelectMany(result => result.Sim.Log.Take(40).Select(line => $"seed {result.Seed} {line}")).Take(40).ToList();

        if (notes.Count > 0)
        {
            text.AppendLine("Signings and dismissals of the scripted managers (first 40 lines):");
            text.AppendLine();

            foreach (var note in notes)
            {
                text.AppendLine($"- {note}");
            }

            text.AppendLine();
        }
    }

    private static void Assumptions(StringBuilder text)
    {
        text.AppendLine("## 5. How the sandbox works, and where it differs from the game");
        text.AppendLine();
        text.AppendLine("- Real code, called as it is: the fans (count, mood, weekly review, the win/draw/loss curve), the stadium and structure rules, the economy rules (values, wages, league money, prize table, sponsor, objective, standing, budget, debt states), the player valuation, the renewal quote, daily training, retirements, the AI contract, market and finance policies, the squad, free-agent and emergency-replacement generators, promotion and relegation.");
        text.AppendLine("- Replaced: the match engine (a result is drawn from the fans' own curve of strength gap, then goals that fit it), the database, and the auction (the highest bid wins; a listing is open for at least one of three market rounds a matchday, about the game's 48 hours).");
        text.AppendLine("- Money moves once a matchday: league money, the sponsor's fixed part, wages, a third of a weekly run's shop takings, and a 34th of the season's operating cost and upkeep. Prize money, the sponsor's bonus and the standing step come at the end of the season; the parachute at the start of the next.");
        text.AppendLine("- No injuries, suspensions, condition or morale from matches; every player is available every week; appearances for the renewal quote come from the rank in the squad (a starter 30, the bench 12, the rest 2 of 34 matchdays, scaled to the season).");
        text.AppendLine("- Free agents: a weekly batch is generated and only the scripted managers bid on it, as in the game (FA-5). Released and expired players join the next batch at their value.");
        text.AppendLine("- The two spenders buy their stars straight from a computer club at 105% of his value (not a game rule), and are handed the fee if they cannot pay it (booked as a gift): it tests what the wages do to a club, not whether it can find the cash for a star. The trader knows every hidden ceiling (a perfect scout).");
        text.AppendLine("- A club in the red bids on nothing and builds nothing; in administration the board lists the highest earners at 80% of their value, and a player nobody buys is released with his agent paying the club 30% of his value; a scripted manager still in administration a whole season later is dismissed and the club plays on as a computer club. A computer club in administration just keeps selling.");
        text.AppendLine("- The clean start is the plan's section 8, played by the sandbox at the start of season 4 on the tier-1 computer club with the most cash.");
        text.AppendLine("- Rankings and shares use the pooled club-seasons of all seeds.");
    }

    private static void FeeTable(StringBuilder text, List<Result> results, int seasons)
    {
        text.AppendLine("## 3b. What a player costs on the market");
        text.AppendLine();
        text.AppendLine("Median fee of completed club-to-club transfers by the buyer's reading of the skill (the number of transfers in brackets). Free-agent signings are counted apart: their fee leaves the game.");
        text.AppendLine();
        text.AppendLine($"| Skill | {string.Join(" | ", Enumerable.Range(1, seasons).Select(season => $"S{season}"))} |");
        text.AppendLine($"|---|{string.Concat(Enumerable.Repeat("---|", seasons))}");

        var transfers = results.SelectMany(result => result.Sim.Transfers).ToList();

        foreach (var (low, high, label) in FeeBands)
        {
            var cells = Enumerable.Range(1, seasons).Select(season =>
            {
                var fees = transfers.Where(transfer => !transfer.Pool && transfer.Season == season && transfer.Ability >= low && transfer.Ability <= high).Select(transfer => (double)transfer.Fee).ToList();

                return fees.Count == 0 ? "-" : string.Create(Inv, $"{Money(Median(fees))} ({fees.Count})");
            });

            text.AppendLine($"| {label} | {string.Join(" | ", cells)} |");
        }

        var pool = transfers.Where(transfer => transfer.Pool).ToList();
        var club = transfers.Where(transfer => !transfer.Pool).ToList();

        text.AppendLine();
        text.AppendLine(string.Create(
            Inv,
            $"Over the run: {club.Count} club-to-club transfers in {results.Count} world(s) ({club.Count / (double)Math.Max(1, results.Count * seasons):0.0} a season), {pool.Count} free-agent signings ({(pool.Count == 0 ? "-" : Money(Median(pool, transfer => transfer.Fee)))} median fee)."));
        text.AppendLine();
    }

    private static void AgeProfile(StringBuilder text, List<Result> results)
    {
        text.AppendLine("## 3c. Where skill sits by age");
        text.AppendLine();
        text.AppendLine("Every player under contract, pooled over the seeds: when the world opened and when the last season ended. Skill is the mean of his 28 attributes (what the market prices); own-position skill is the mean of the attributes his position uses (what the fans' strength reads); the ceiling is his hidden potential.");
        text.AppendLine();
        text.AppendLine("| Age | Opening: players / skill / own-position skill / ceiling | Closing: players / skill / own-position skill / ceiling |");
        text.AppendLine("|---|---|---|");

        var opening = results.SelectMany(result => result.Sim.Opening).ToList();
        var closing = results.SelectMany(result => result.Sim.Closing).ToList();

        foreach (var (low, high, label) in AgeBands)
        {
            string Cell(List<(int Age, int Ability, int Potential, double Own)> source)
            {
                var group = source.Where(player => player.Age >= low && player.Age <= high).ToList();

                return group.Count == 0
                    ? "-"
                    : string.Create(Inv, $"{group.Count / (double)results.Count:0.0} / {group.Average(player => player.Ability):0.0} / {group.Average(player => player.Own):0.0} / {group.Average(player => player.Potential):0.0}");
            }

            text.AppendLine($"| {label} | {Cell(opening)} | {Cell(closing)} |");
        }

        text.AppendLine();
    }

    private static double SoldOut(List<ClubSeasonRow> group)
    {
        var homes = group.Sum(row => row.Books.HomeMatches);

        return homes == 0 ? 0 : group.Sum(row => row.Books.SoldOut) / (double)homes;
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a club could pay a fee and still hold half a season of its wages.</summary>
    private static bool CanPay(ClubSeasonRow row, long fee) => row.CashEnd >= fee + (row.WageBill / 2);

    /// <summary>How many more players at a wage a club could carry before its wages pass its income, beside the wages of the squad a median club has.</summary>
    private static double StarsWithin(double income, List<ClubSeasonRow> group, long starWage)
    {
        var bill = Median(group, row => row.Books.Wages);

        return starWage <= 0 ? 0 : Math.Max(0, (income - bill) / starWage);
    }

    private static double EnteredTop5(List<ClubSeasonRow> rows, int tier, int seasons)
    {
        var entered = 0;

        for (var season = 5; season <= seasons; season++)
        {
            var before = TopFive(rows, season - 1, tier);
            var now = TopFive(rows, season, tier);

            entered += now.Count(club => !before.Contains(club));
        }

        return entered;
    }

    private static HashSet<int> TopFive(List<ClubSeasonRow> rows, int season, int tier) =>
        [.. rows.Where(row => row.Season == season && row.Tier == tier).OrderByDescending(row => row.Books.Income).Take(5).Select(row => row.ClubIdx)];

    private static double TopOverMedian(List<Result> results, int season, int tier, bool aiOnly = false)
    {
        var ratios = new List<double>();

        foreach (var result in results)
        {
            var group = result.Sim.Rows.Where(row => row.Season == season && row.Tier == tier && (!aiOnly || row.Manager == "AI")).Select(row => (double)row.Books.Income).OrderByDescending(income => income).ToList();

            var median = Median([.. group]);

            if (group.Count >= 5 && median > 0)
            {
                ratios.Add(group.Take(5).Average() / median);
            }
        }

        return ratios.Count == 0 ? 0 : ratios.Average();
    }

    public static double Median<T>(IReadOnlyCollection<T> items, Func<T, double> value) => items.Count == 0 ? 0 : Median([.. items.Select(value)]);

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();

        return values.Count % 2 == 1 ? values[values.Count / 2] : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
    }

    private static double Percentile<T>(List<T> items, Func<T, double> value, double fraction)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var sorted = items.Select(value).OrderBy(number => number).ToList();

        return sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }

    private static double Share<T>(List<T> items, Func<T, bool> predicate) =>
        items.Count == 0 ? 0 : items.Count(predicate) / (double)items.Count;

    /// <summary>Minor units as the amount a manager reads: 12.4M, 850k, 1,250.</summary>
    private static string Money(double minor)
    {
        var shown = minor / 100.0;
        var magnitude = Math.Abs(shown);

        return magnitude >= 1_000_000
            ? string.Create(Inv, $"{shown / 1_000_000:0.0#}M")
            : magnitude >= 10_000
                ? string.Create(Inv, $"{shown / 1_000:0.#}k")
                : string.Create(Inv, $"{shown:N0}");
    }
}
