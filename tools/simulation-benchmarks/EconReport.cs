using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.SimulationBenchmarks;

// Runs the economy sandbox for several seeds and writes the season tables (CSV), and a report that reads the 13 targets of plan toasty-scott
// against what the run found. Amounts in the report are the amounts a manager would read: minor units divided by 100.
internal static class EconReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly (int Number, string Manager, string Text)[] SpenderTargets =
    [
        (2, "Tier-3 big spender", "Tier 3 signing three 15+ players goes into the red within the season"),
        (3, "Tier-4 spender", "Tier 4: one 15+ player pushes wages above 100% of income"),
    ];

    private static readonly (int Low, int High, string Label)[] FeeBands = [(1, 8, "up to 8"), (9, 11, "9-11"), (12, 14, "12-14"), (15, 17, "15-17"), (18, 20, "18-20")];

    private static readonly (int Low, int High, string Label)[] AgeBands = [(17, 18, "17-18"), (19, 20, "19-20"), (21, 22, "21-22"), (23, 24, "23-24"), (25, 27, "25-27"), (28, 30, "28-30"), (31, 40, "31+")];

    private static readonly int[] MatureSeasons = [3, 5, 10];

    private static readonly int[] EliteSeasons = [5, 10];

    private sealed record Result(ulong Seed, EconSim Sim, double Seconds);

    public static void Run(int seeds, ulong firstSeed, int seasons, string outDir, IEconRules rules, int youthPerClub = 0, int trainingPercent = 100, int backgroundPercent = 0)
    {
        Directory.CreateDirectory(outDir);

        var results = new List<Result>();
        var total = Stopwatch.StartNew();

        for (var index = 0; index < seeds; index++)
        {
            var seed = firstSeed + (ulong)index;
            var watch = Stopwatch.StartNew();
            var sim = new EconSim(rules, seed, seasons, youthPerClub, trainingPercent, backgroundPercent);

            sim.Run();
            results.Add(new Result(seed, sim, watch.Elapsed.TotalSeconds));
            Console.WriteLine(string.Create(Inv, $"econ seed {seed}: {seasons} seasons in {watch.Elapsed.TotalSeconds:0.0}s, {sim.Transfers.Count} transfers, {sim.Trades.Count} trades"));
        }

        WriteClubsCsv(Path.Combine(outDir, "econ-clubs.csv"), results);
        WriteTiersCsv(Path.Combine(outDir, "econ-tiers.csv"), results, seasons);

        var markdown = Markdown(rules, results, seasons, total.Elapsed.TotalSeconds, youthPerClub, trainingPercent, backgroundPercent);

        File.WriteAllText(Path.Combine(outDir, "econ-report.md"), markdown, new UTF8Encoding(false));
        Console.WriteLine(string.Create(Inv, $"econ: {seeds} seeds x {seasons} seasons in {total.Elapsed.TotalSeconds:0.0}s -> {Path.GetFullPath(outDir)}"));
    }

    // ---- CSV --------------------------------------------------------------------------------------------------------------------------

    private static void WriteClubsCsv(string path, List<Result> results)
    {
        var text = new StringBuilder();

        text.AppendLine("seed,season,club,manager,tier,rank,points,fans,capacity,snack_level,merch_level,fixed,gate,snack,merch,prize,fees_received,wages,operating,upkeep,fees_paid,building,upgrades,grants,cash_end,shadow_end,shadow_min,squad,mean_ability,players_15,players_17,players_19,wage_bill_per_settlement,forecast_income,wage_share,wage_share_before_prize,sold_out_share,cash_start,reconciled");

        foreach (var row in results.SelectMany(result => result.Sim.Rows))
        {
            var books = row.Books;

            text.AppendLine(string.Create(
                Inv,
                $"{row.Seed},{row.Season},{row.Club},{row.Manager},{row.Tier},{row.Rank},{row.Points},{row.Fans},{row.Capacity},{row.SnackLevel},{row.MerchLevel},{books.Fixed},{books.Gate},{books.Snack},{books.Merch},{books.Prize},{books.FeesReceived},{books.Wages},{books.Operating},{books.Upkeep},{books.FeesPaid},{books.Building},{books.Upgrades},{books.Grants},{row.CashEnd},{row.ShadowEnd},{row.ShadowMin},{row.SquadSize},{row.MeanAbility:0.00},{row.Players15},{row.Players17},{row.Players19},{row.WageBillPerSettlement},{row.ForecastIncome},{row.WageShare:0.000},{row.WageShareBeforePrize:0.000},{(books.HomeMatches == 0 ? 0 : (double)books.SoldOut / books.HomeMatches):0.00},{row.CashStart},{(row.Reconciled ? 1 : 0)}"));
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static void WriteTiersCsv(string path, List<Result> results, int seasons)
    {
        var text = new StringBuilder();

        text.AppendLine("season,tier,clubs,income_median,fixed_median,gate_median,snack_median,merch_median,prize_median,wages_median,wage_share_median,share_over_100,share_over_70,cash_p10,cash_p50,cash_p90,shadow_min_p10,clubs_with_grants,players_15_mean,players_17_mean,players_19_mean,top5_income_over_median,top5_income_over_median_ai,sold_out_share,would_be_red_share,squad_mean,skill_mean");

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
                    $"{season},{tier},{group.Count},{Median(group, row => row.Books.Income):0},{Median(group, row => row.Books.Fixed):0},{Median(group, row => row.Books.Gate):0},{Median(group, row => row.Books.Snack):0},{Median(group, row => row.Books.Merch):0},{Median(group, row => row.Books.Prize):0},{Median(group, row => row.Books.Wages):0},{Median(group, row => row.WageShare):0.000},{Share(group, row => row.WageShare > 1.0):0.000},{Share(group, row => row.WageShare > 0.7):0.000},{Percentile(group, row => row.CashEnd, 0.1):0},{Percentile(group, row => row.CashEnd, 0.5):0},{Percentile(group, row => row.CashEnd, 0.9):0},{Percentile(group, row => row.ShadowMin, 0.1):0},{Share(group, row => row.Books.Grants > 0):0.000},{group.Average(row => row.Players15):0.00},{group.Average(row => row.Players17):0.00},{group.Average(row => row.Players19):0.00},{TopOverMedian(results, season, tier):0.00},{TopOverMedian(results, season, tier, aiOnly: true):0.00},{SoldOut(group):0.000},{Share(group, row => row.ShadowMin < 0):0.000},{group.Average(row => row.SquadSize):0.0},{group.Average(row => row.MeanAbility):0.00}"));
            }
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    // ---- the report -------------------------------------------------------------------------------------------------------------------

    private static string Markdown(IEconRules rules, List<Result> results, int seasons, double seconds, int youthPerClub, int trainingPercent, int backgroundPercent)
    {
        var rows = results.SelectMany(result => result.Sim.Rows).ToList();
        var text = new StringBuilder();

        text.AppendLine(string.Create(Inv, $"# Economy sandbox: {rules.Name}"));
        text.AppendLine();
        text.AppendLine(string.Create(
            Inv,
            $"{results.Count} seed(s) ({string.Join(", ", results.Select(result => result.Seed))}), {seasons} seasons, one country of {EconSim.Tiers} tiers of {EconSim.ClubsPerTier} clubs, {seconds:0} s in all. Amounts are what a manager reads (minor units divided by 100); a season is {EconSim.Weeks} settlements. Youth intake: {(youthPerClub == 0 ? "none (the game today)" : $"{youthPerClub} players of 17-18 per club at each rollover")}. Player development speed: {trainingPercent}% of today's; background growth of untrained attributes: {backgroundPercent}% of the daily budget."));
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

        Diagnosis(text, rules, rows);
        SeasonTable(text, results, rows, seasons);
        FeeTable(text, results, seasons);
        AgeProfile(text, results);
        Targets(text, rules, results, rows, seasons);
        Archetypes(text, results, rows, seasons);
        Assumptions(text);

        return text.ToString();
    }

    private static void Diagnosis(StringBuilder text, IEconRules rules, List<ClubSeasonRow> rows)
    {
        text.AppendLine("## 1. The diagnosis in numbers (season 1, a typical club of each tier)");
        text.AppendLine();
        text.AppendLine("Median over the AI clubs of each tier, season 1.");
        text.AppendLine();
        text.AppendLine("| Tier | Squad wages | Income | League and sponsor | Gate | Snack bar | Shop | Prize | Wages / income | Wages / income before prize | Opening cash |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        for (var tier = 1; tier <= EconSim.Tiers; tier++)
        {
            var group = rows.Where(row => row.Season == 1 && row.Tier == tier && row.Manager == "AI").ToList();

            text.AppendLine(string.Create(
                Inv,
                $"| {tier} | {Money(Median(group, row => row.Books.Wages))} | {Money(Median(group, row => row.Books.Income))} | {Money(Median(group, row => row.Books.Fixed))} | {Money(Median(group, row => row.Books.Gate))} | {Money(Median(group, row => row.Books.Snack))} | {Money(Median(group, row => row.Books.Merch))} | {Money(Median(group, row => row.Books.Prize))} | {Median(group, row => row.WageShare):P0} | {Median(group, row => row.WageShareBeforePrize):P0} | {Money(rules.OpeningCash(tier))} |"));
        }

        text.AppendLine();
        text.AppendLine("What one player costs a club a season in wages, and what he is worth at 26 with his ceiling equal to his skill, by the tier of the club that prices him:");
        text.AppendLine();
        text.AppendLine("| Skill | Wage tier 1 | tier 2 | tier 3 | tier 4 | Value tier 1 | tier 2 | tier 3 | tier 4 |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|");

        foreach (var skill in new[] { 8, 10, 13, 15, 17, 19, 20 })
        {
            var wages = Enumerable.Range(1, EconSim.Tiers).Select(tier => Money(rules.GeneratedWagePerWeek(skill, tier) * EconSim.Weeks));
            var values = Enumerable.Range(1, EconSim.Tiers).Select(tier => Money(rules.Value(skill, skill, 26, tier)));

            text.AppendLine($"| {skill} | {string.Join(" | ", wages)} | {string.Join(" | ", values)} |");
        }

        text.AppendLine();
        text.AppendLine("A tier-4 club prices an 18-rated player at one eighth of what a tier-1 club does, so a poor club can afford a star; and a 20-rated player is worth about three times a 13-rated one, not the 40 or more of real football.");
        text.AppendLine();
        Claims(text, rules, rows);
    }

    /// <summary>The plan's context section, one claim at a time, against what the run measured.</summary>
    private static void Claims(StringBuilder text, IEconRules rules, List<ClubSeasonRow> rows)
    {
        var ai = rows.Where(row => row.Manager == "AI").ToList();
        var tier1 = ai.Where(row => row.Season == 1 && row.Tier == 1).ToList();
        var income = Median(tier1, row => row.Books.Income);
        var wages = Median(tier1, row => row.Books.Wages);

        text.AppendLine("The plan's context, checked:");
        text.AppendLine();
        text.AppendLine(string.Create(Inv, $"- **Wages barely matter.** A tier-1 AI club's squad costs {Money(wages / EconSim.Weeks)} a week against {Money(income / EconSim.Weeks)} a week of income (the plan said 13k against 115k): wages are {Median(tier1, row => row.WageShare):P0} of income (the plan said 11%). The gate is lower than the plan's figure because a club hosts 17 matches in 34 weeks, not one every week."));
        text.AppendLine(string.Create(Inv, $"- **Values are flat.** A 20-rated player is worth {Money(rules.Value(20, 20, 26, 1))}, a 13-rated one {Money(rules.Value(13, 13, 26, 1))} ({(double)rules.Value(20, 20, 26, 1) / rules.Value(13, 13, 26, 1):0.0}x); a tier-1 club opens with {Money(rules.OpeningCash(1))}, which buys {(double)rules.OpeningCash(1) / rules.Value(20, 20, 26, 1):0.0} of them."));
        text.AppendLine(string.Create(Inv, $"- **Nothing can go bankrupt.** Emergency grants fired in {ai.Count(row => row.Books.Grants > 0)} of {ai.Count} AI club-seasons; the lowest cash any AI club would have reached without a grant was {Money(ai.Min(row => row.ShadowMin))}."));
        text.AppendLine(string.Create(Inv, $"- **Computer clubs never build.** The largest ground an AI club ever had was {ai.Max(row => row.Capacity):N0} places, and the snack bar and shop of every AI club stayed at level {ai.Max(row => Math.Max(row.SnackLevel, row.MerchLevel))}."));
        text.AppendLine(string.Create(Inv, $"- **The opening ground is outgrown.** The share of tier-1 AI home matches where more wanted to come than the 5,000 places held: season 1 {SoldOut(tier1):P0}, season 2 {SoldOut(ai.Where(row => row.Season == 2 && row.Tier == 1).ToList()):P0}, season 3 {SoldOut(ai.Where(row => row.Season == 3 && row.Tier == 1).ToList()):P0}."));
        text.AppendLine(string.Create(Inv, $"- **Buildings earn more in the lower tiers.** Snack bar and shop are {Median(ai.Where(row => row.Season == 1 && row.Tier == 1).ToList(), row => (double)(row.Books.Snack + row.Books.Merch) / Math.Max(1, row.Books.Income)):P0} of a tier-1 club's income and {Median(ai.Where(row => row.Season == 1 && row.Tier == 4).ToList(), row => (double)(row.Books.Snack + row.Books.Merch) / Math.Max(1, row.Books.Income)):P0} of a tier-4 club's."));

        var last = ai.Max(row => row.Season);

        text.AppendLine(string.Create(Inv, $"- **Not in the plan, found by the run:** with no intake of young players and no free-agent signings by the AI (FA-5), a tier-1 AI squad shrinks from {ai.Where(row => row.Season == 1 && row.Tier == 1).Average(row => row.SquadSize):0.0} players in season 1 to {ai.Where(row => row.Season == last && row.Tier == 1).Average(row => row.SquadSize):0.0} in season {last}, and its mean skill moves from {ai.Where(row => row.Season == 1 && row.Tier == 1).Average(row => row.MeanAbility):0.0} to {ai.Where(row => row.Season == last && row.Tier == 1).Average(row => row.MeanAbility):0.0}; the wage bill follows it down ({Money(Median(tier1, row => row.Books.Wages))} in season 1, {Money(Median(ai.Where(row => row.Season == last && row.Tier == 1).ToList(), row => row.Books.Wages))} in season {last}). Whatever the new rules pay, the AI wage bill will not grow by itself."));
        text.AppendLine();
    }

    private static void SeasonTable(StringBuilder text, List<Result> results, List<ClubSeasonRow> rows, int seasons)
    {
        text.AppendLine("## 2. Season by season, tier by tier (AI clubs only)");
        text.AppendLine();
        text.AppendLine("Median over clubs and seeds; the cash columns are the 10th, 50th and 90th percentile at the season's end, after the prize money. \"Would-be low\" is the 10th percentile of the lowest cash the club would have reached if no emergency grant had ever bailed it out, and \"Would-be red\" the share of clubs for which that was below zero. \"Sold out\" is the share of home matches where more people wanted to come than the ground held. Administration and dismissal do not exist in the game yet, so they are zero by construction."); 
        text.AppendLine();
        text.AppendLine("| Season | Tier | Income | Wages / income | Over 100% | Over 70% | Cash p10 / p50 / p90 | Would-be low p10 | Would-be red | Bailed out | 15+ per club | 17+ per club | 19+ per club | Squad / mean skill | Sold out | Top 5 income / median |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

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
                    $"| {season} | {tier} | {Money(Median(group, row => row.Books.Income))} | {Median(group, row => row.WageShare):P0} | {Share(group, row => row.WageShare > 1.0):P0} | {Share(group, row => row.WageShare > 0.7):P0} | {Money(Percentile(group, row => row.CashEnd, 0.1))} / {Money(Percentile(group, row => row.CashEnd, 0.5))} / {Money(Percentile(group, row => row.CashEnd, 0.9))} | {Money(Percentile(group, row => row.ShadowMin, 0.1))} | {Share(group, row => row.ShadowMin < 0):P0} | {Share(group, row => row.Books.Grants > 0):P0} | {group.Average(row => row.Players15):0.0} | {group.Average(row => row.Players17):0.00} | {group.Average(row => row.Players19):0.00} | {group.Average(row => row.SquadSize):0.0} / {group.Average(row => row.MeanAbility):0.0} | {SoldOut(group):P0} | {TopOverMedian(results, season, tier, aiOnly: true):0.0}x |"));
            }
        }

        text.AppendLine();
    }

    private static void Targets(StringBuilder text, IEconRules rules, List<Result> results, List<ClubSeasonRow> rows, int seasons)
    {
        text.AppendLine("## 3. The 13 targets read against today's rules");
        text.AppendLine();
        text.AppendLine("\"Today\" is the sandbox on the game's current rules. A target marked n/a needs a feature the game does not have yet (debt, administration, sponsors); the number beside it is the baseline Milestone 1 has to move.");
        text.AppendLine();
        text.AppendLine("| # | Target | Today | Verdict |");
        text.AppendLine("|---|---|---|---|");

        var ai = rows.Where(row => row.Manager == "AI").ToList();

        // 1. wages matter.
        var shares = Enumerable.Range(1, EconSim.Tiers)
            .Select(tier => (tier, share: Median(ai.Where(row => row.Season == 1 && row.Tier == tier).ToList(), row => row.WageShare)))
            .ToList();
        var worstOver = Enumerable.Range(1, seasons)
            .SelectMany(season => Enumerable.Range(1, EconSim.Tiers).Select(tier => Share(ai.Where(row => row.Season == season && row.Tier == tier).ToList(), row => row.WageShare > 1.0)))
            .Max();
        var wagesHit = shares.All(entry => entry.share is >= 0.55 and <= 0.75) && worstOver < 0.05;

        text.AppendLine($"| 1 | Wages are 55-75% of income in every tier in season 1, and under 5% of AI clubs are over 100% in any season | season 1 median: {string.Join(", ", shares.Select(entry => string.Create(Inv, $"tier {entry.tier} {entry.share:P0}")))}; worst share of clubs over 100% in any season-tier: {worstOver:P0} | {Verdict(wagesHit)} |");

        // 2 and 3. a big spender.
        foreach (var (number, manager, text2) in SpenderTargets)
        {
            var spender = rows.Where(row => row.Manager == manager && row.Season == 1).ToList();
            var low = spender.Count == 0 ? 0 : spender.Min(row => row.ShadowMin);
            var wageShare = spender.Count == 0 ? 0 : spender.Average(row => row.WageShare);
            var grants = spender.Count(row => row.Books.Grants > 0);
            var now15 = spender.Count == 0 ? 0 : spender.Average(row => row.Players15);
            var hit = number == 2 ? low < 0 : wageShare > 1.0;

            text.AppendLine($"| {number} | {text2} | season 1 with {now15:0.0} players of 15+ on average: wages / income {wageShare:P0}, would-be low {Money(low)}, bailed out in {grants} of {spender.Count} seeds | {Verdict(hit)} |");
        }

        // 4. tier 2 and 5-6 stars.
        var tier2 = new List<string>();

        for (var season = 1; season <= seasons; season += season == 1 ? 2 : 3)
        {
            var group = ai.Where(row => row.Season == season && row.Tier == 2).ToList();

            if (group.Count == 0)
            {
                continue;
            }

            var median = Median(group, row => row.Books.Income);
            var top = Percentile(group, row => row.Books.Income, 0.75);
            var star = rules.GeneratedWagePerWeek(15, 2) * EconSim.Weeks;

            tier2.Add(string.Create(Inv, $"season {season}: median club could carry {StarsWithin(median, group, star, 1.0):0} stars of 15+ at 100% of income, top quartile {StarsWithin(top, group, star, 1.0):0}"));
        }

        text.AppendLine($"| 4 | Tier 2 sustains 5-6 players of 15+ only as a grown club (top-quartile income), never in season 1 | {string.Join("; ", tier2)} (a 15-rated player costs {Money(rules.GeneratedWagePerWeek(15, 2) * EconSim.Weeks)} a season at tier 2) | {Verdict(false)} |");

        // 5. mature tier-1 club.
        var prudent = rows.Where(row => row.Manager == "Prudent builder").OrderBy(row => row.Season).ToList();
        var matureLine = new List<string>();

        foreach (var season in MatureSeasons.Where(season => season <= seasons))
        {
            var group = prudent.Where(row => row.Season == season).ToList();

            if (group.Count == 0)
            {
                continue;
            }

            var income = group.Average(row => (double)row.Books.Income);
            var wage19 = rules.GeneratedWagePerWeek(19, 1) * EconSim.Weeks;
            var affordable = wage19 <= 0 ? 0 : (0.75 * income - group.Average(row => (double)row.Books.Wages)) / wage19;

            matureLine.Add(string.Create(Inv, $"season {season}: ground {group.Average(row => row.Capacity):N0}, income {Money((long)income)}, wages {group.Average(row => row.WageShare):P0}, room for {affordable:0} more players of 19 at 75%"));
        }

        text.AppendLine($"| 5 | A mature tier-1 club (ground 25-30k, buildings at the top, 3-5 seasons in) carries 3-4 players of 19-20 at 75% wages or less | {string.Join("; ", matureLine)}. A 19-rated player costs {Money(rules.GeneratedWagePerWeek(19, 1) * EconSim.Weeks)} a season | {Verdict(false)} |");

        // 6. elite.
        // "The best player there is": a 20-rated 21-year-old, at the price a tier-1 club puts on him.
        var best = rules.Value(20, 20, 21, 1);
        var elite = new List<string>();
        var firstRich = Enumerable.Range(1, seasons).FirstOrDefault(season => rows.Any(row => row.Season == season && row.Tier == 1 && CanPay(row, best)));

        foreach (var season in EliteSeasons.Where(season => season <= seasons))
        {
            var tier1Rows = rows.Where(row => row.Season == season && row.Tier == 1).ToList();
            var top = tier1Rows.OrderByDescending(row => row.CashEnd).Take(5).ToList();

            elite.Add(string.Create(Inv, $"season {season}: {tier1Rows.Count(row => CanPay(row, best)) / (double)Math.Max(1, tier1Rows.Count):P0} of tier-1 clubs could pay for him and keep half a season of wages, the five richest hold {Money(top.Average(row => row.CashEnd))} ({top.Average(row => row.CashEnd) / best:0}x his price)"));
        }

        text.AppendLine($"| 6 | From season 10 the 3-5 dominant tier-1 clubs can pay for the best player there is (about 100M in the new money) and keep a reserve; no club can before season 8; top income is 2.5x the tier median or more | the best player (a 20-rated 21-year-old) is priced at {Money(best)}. {string.Join("; ", elite)}. First season a club could pay for him: {(firstRich == 0 ? "never" : firstRich.ToString(Inv))}. Top 5 income / median in tier 1 at season {seasons}: {TopOverMedian(results, seasons, 1):0.0}x ({TopOverMedian(results, seasons, 1, aiOnly: true):0.0}x counting AI clubs only) | {Verdict(false)} |");

        // 7. mobility.
        var entered = new List<double>();

        foreach (var result in results)
        {
            for (var tier = 1; tier <= EconSim.Tiers; tier++)
            {
                entered.Add(EnteredTop5(result.Sim.Rows, tier, seasons));
            }
        }

        var spread = TopOverMedian(results, 4, 1);
        var spreadAi = TopOverMedian(results, 4, 1, aiOnly: true);

        text.AppendLine($"| 7 | At least one club enters a league's top 5 by income between seasons 5 and 10 (and there is a top 5 to enter) | per league and seed, on average {entered.Average():0.0} clubs change places in the top 5 after season 4; at season 4 the top 5 of tier 1 earn {spread:0.0}x the median ({spreadAi:0.0}x counting AI clubs only, whose grounds never grow), so among AI clubs the order is noise | {Verdict(entered.Average() >= 1 && spreadAi >= 1.5)} |");

        // 8. trading.
        var trades = results.SelectMany(result => result.Sim.Trades).ToList();
        var gems = trades.Where(trade => trade.Gem).ToList();
        var returnLine = trades.Count == 0 ? "no completed trade" : string.Create(Inv, $"{trades.Count} completed trades ({trades.Count(trade => trade.FromPool)} bought from the free-agent pool), median return {Median(trades, trade => trade.ReturnPercent):0}% of the fee after wages; best {trades.Max(trade => trade.ReturnPercent):0}%");
        var gemLine = gems.Count == 0 ? "no gem (ceiling 16+) was bought and sold" : string.Create(Inv, $"{gems.Count} gems, median fee out / fee in {Median(gems, trade => trade.Multiple):0.0}x");

        text.AppendLine($"| 8 | The trader's median completed trade returns 50% or more after wages; a gem bought young returns 5x his fee | {returnLine}; {gemLine} | {Verdict(trades.Count > 0 && Median(trades, trade => trade.ReturnPercent) >= 50 && gems.Count > 0 && Median(gems, trade => trade.Multiple) >= 5)} |");

        // 9. inflation and bail-outs.
        var transfers = results.SelectMany(result => result.Sim.Transfers).Where(transfer => !transfer.Pool).ToList();
        var drifts = new List<string>();
        var drifted = false;

        foreach (var (low, high, label) in new[] { (9, 11, "skill 9-11"), (12, 14, "skill 12-14"), (15, 17, "skill 15-17"), (18, 20, "skill 18-20") })
        {
            var early = transfers.Where(transfer => transfer.Season <= 3 && transfer.Ability >= low && transfer.Ability <= high).Select(transfer => (double)transfer.Fee).ToList();
            var late = transfers.Where(transfer => transfer.Season >= seasons - 2 && transfer.Ability >= low && transfer.Ability <= high).Select(transfer => (double)transfer.Fee).ToList();

            if (early.Count >= 10 && late.Count >= 10)
            {
                var drift = (Median(late) / Median(early)) - 1;

                drifted |= Math.Abs(drift) > 0.25;
                drifts.Add(string.Create(Inv, $"{label} {drift:+0%;-0%}"));
            }
        }

        var grantRate = ai.Count(row => row.Books.Grants > 0) / (double)Math.Max(1, ai.Count);

        text.AppendLine($"| 9 | Median fee per skill band drifts less than 25% over the run; under 2% of AI clubs a season go into administration; the emergency grant never fires | fee drift between the first three and last three seasons: {(drifts.Count == 0 ? "too few transfers" : string.Join(", ", drifts))}; AI club-seasons that needed an emergency grant: {grantRate:P1} | {Verdict(!drifted && grantRate == 0)} |");

        // 10. relegation.
        var relegated = new List<ClubSeasonRow>();

        foreach (var result in results)
        {
            foreach (var row in result.Sim.Rows.Where(row => row.Season < seasons && row.Tier < EconSim.Tiers && row.Rank > EconSim.ClubsPerTier - 3))
            {
                var next = result.Sim.Rows.FirstOrDefault(other => other.Season == row.Season + 1 && other.ClubIdx == row.ClubIdx);

                if (next is not null && next.Tier == row.Tier + 1 && row.Manager == "AI")
                {
                    relegated.Add(next);
                }
            }
        }

        var survive = relegated.Count == 0 ? 1.0 : relegated.Count(row => row.Books.Grants == 0) / (double)relegated.Count;

        text.AppendLine($"| 10 | A relegated club with a normal squad does not reach administration in its first season down | {relegated.Count} relegated AI clubs: median wages / income in their first season down {(relegated.Count == 0 ? 0 : Median(relegated, row => row.WageShare)):P0}, {survive:P0} needed no bail-out. Administration does not exist yet | n/a, baseline {survive:P0} survive |");

        // 11. buildings.
        var shares2 = Enumerable.Range(1, EconSim.Tiers)
            .Select(tier => Median(ai.Where(row => row.Season == 1 && row.Tier == tier).ToList(), row => row.Books.Income <= 0 ? 0 : (double)(row.Books.Snack + row.Books.Merch) / row.Books.Income))
            .ToList();
        var relative = shares2[0] <= 0 ? 0 : shares2[EconSim.Tiers - 1] / shares2[0];

        text.AppendLine($"| 11 | The tier-4 to tier-1 relative gain from the snack bar and shop is no longer above 2x | share of income from snack bar and shop in season 1: {string.Join(", ", shares2.Select((share, index) => string.Create(Inv, $"tier {index + 1} {share:P1}")))}; tier 4 / tier 1 = {relative:0.0}x | {Verdict(relative <= 2.0)} |");

        // 12. clean start.
        var clean = results.SelectMany(result => result.Sim.CleanStarts.Select(line => $"seed {result.Seed}, {line}")).ToList();

        text.AppendLine($"| 12 | A clean-start squad's wages are 55-65% of the forecast; a clean-start tier-1 club after season 3 is usually relegated, then regrows | {(clean.Count == 0 ? "no clean start ran" : $"{clean.Count} clean starts, see section 4")} | {Verdict(false)} |");

        // 13. prize weight.
        var prize = Enumerable.Range(1, EconSim.Tiers)
            .Select(tier => (tier, share: Median(ai.Where(row => row.Season == 1 && row.Tier == tier).ToList(), row => row.Books.Income <= 0 ? 0 : (double)row.Books.Prize / row.Books.Income)))
            .ToList();
        var eliteRows = rows.Where(row => row.Season == seasons && row.Tier == 1).OrderByDescending(row => row.CashEnd).Take(5).ToList();
        var eliteShare = eliteRows.Count == 0 ? 0 : eliteRows.Average(row => row.Books.Income <= 0 ? 0 : (double)row.Books.Prize / row.Books.Income);

        text.AppendLine($"| 13 | Prize money is 15-25% of a fresh club's season income in every tier, and under 10% for the elite by season 10 | season 1 median prize / income: {string.Join(", ", prize.Select(entry => string.Create(Inv, $"tier {entry.tier} {entry.share:P0}")))}; five richest tier-1 clubs in season {seasons}: {eliteShare:P0} | {Verdict(prize.All(entry => entry.share is >= 0.15 and <= 0.25) && eliteShare < 0.10)} |");

        text.AppendLine();
    }

    private static void Archetypes(StringBuilder text, List<Result> results, List<ClubSeasonRow> rows, int seasons)
    {
        text.AppendLine("## 4. The scripted managers");
        text.AppendLine();
        text.AppendLine("Each plays the median club of its tier. One line per season, averaged over the seeds. \"Would-be low\" is the lowest the club's cash would have been without emergency grants.");
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
            text.AppendLine("| Season | Tier | Place | Income | Wages | Wages / income | Cash at the end | Would-be low | Bailed out | 15+ | 19+ | Ground | Snack / shop |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            for (var season = 1; season <= seasons; season++)
            {
                var group = mine.Where(row => row.Season == season).ToList();

                if (group.Count == 0)
                {
                    continue;
                }

                text.AppendLine(string.Create(
                    Inv,
                    $"| {season} | {group.Average(row => row.Tier):0.0} | {group.Average(row => row.Rank):0.0} | {Money((long)group.Average(row => row.Books.Income))} | {Money((long)group.Average(row => row.Books.Wages))} | {group.Average(row => row.WageShare):P0} | {Money((long)group.Average(row => row.CashEnd))} | {Money((long)group.Average(row => row.ShadowMin))} | {group.Count(row => row.Books.Grants > 0)} of {group.Count} | {group.Average(row => row.Players15):0.0} | {group.Average(row => row.Players19):0.0} | {group.Average(row => row.Capacity):N0} | {group.Average(row => row.SnackLevel):0.0} / {group.Average(row => row.MerchLevel):0.0} |"));
            }

            text.AppendLine();
        }

        var cleanLines = results.SelectMany(result => result.Sim.CleanStarts.Select(line => $"seed {result.Seed}, {line}")).ToList();

        if (cleanLines.Count > 0)
        {
            text.AppendLine("Clean starts (plan section 8; the game does not have them yet):");
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
            text.AppendLine("Signings of the spenders (first 40 lines):");
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
        text.AppendLine("- Real code, called as it is: the fans (count, mood, weekly review, the win/draw/loss curve), the stadium and structure rules, the player valuation, the renewal quote, daily training (86 days a season, default programme, normal intensity), retirements, the AI contract and market policies, the squad, free-agent and emergency-replacement generators, promotion and relegation.");
        text.AppendLine("- Replaced: the match engine (a result is drawn from the fans' own curve of strength gap, then goals that fit it), the database, and the auction (the highest bid wins; a listing is open for at least one of three market rounds a week, about the game's 48 hours).");
        text.AppendLine("- No injuries, suspensions, condition or morale from matches; every player is available every week; appearances for the renewal quote come from the rank in the squad (starter 30, bench 12, other 2).");
        text.AppendLine("- Free agents: a weekly batch is generated and only the scripted managers bid on it, as in the game (FA-5). Released and expired players join the next batch at full value.");
        text.AppendLine("- The two spenders buy their stars straight from an AI club at 105% of his value (not a game rule): it tests what the economy does to a club that signs stars, not whether the market has stars to sell. The trader knows every hidden ceiling (a perfect scout) and asks 90% of value, so the AI buys.");
        text.AppendLine("- The clean start is the plan's section 8, played by the sandbox at the start of season 4 on the tier-1 AI club with the most cash.");
        text.AppendLine("- Rankings and shares use the pooled club-seasons of all seeds.");
    }

    private static void FeeTable(StringBuilder text, List<Result> results, int seasons)
    {
        text.AppendLine("## 2b. What a player costs on the market");
        text.AppendLine();
        text.AppendLine("Median fee of completed club-to-club transfers by the buyer's reading of the skill (the number of transfers in brackets). Free-agent signings are counted apart: their fee leaves the game and is priced at the lowest tier.");
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
        text.AppendLine("## 2c. Where skill sits by age");
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

    private static bool CanPay(ClubSeasonRow row, long fee) => row.CashEnd >= fee + (row.WageBillPerSettlement * (EconSim.Weeks / 2));

    private static string Verdict(bool hit) => hit ? "hit" : "missed";

    /// <summary>How many players at the wage a club could carry within a share of its income, beside the wages of the squad a median club has.</summary>
    private static double StarsWithin(double income, List<ClubSeasonRow> group, long starWage, double share)
    {
        var bill = Median(group, row => row.Books.Wages);

        return starWage <= 0 ? 0 : Math.Max(0, ((share * income) - bill) / starWage);
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
