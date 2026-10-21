using System.Globalization;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.SimulationBenchmarks;

// The end of a season: prize money, the season's row, promotion and relegation, the fans, retirements and contracts.
internal sealed partial class EconSim
{
    /// <summary>The season at whose start the clean-start manager takes a grown tier-1 club over.</summary>
    public const int CleanStartSeason = 4;

    private static readonly PlayerPosition[] OutfieldPositions =
    [
        PlayerPosition.CentreBack,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.Striker,
        PlayerPosition.LeftBack,
        PlayerPosition.RightWinger,
        PlayerPosition.DefensiveMidfielder,
    ];

    private void PickManagers()
    {
        // The scripted managers take the median club of their tier, so each starts as a typical club and not a favourite or a strugger.
        Take(1, 9, new PrudentBuilder("Prudent builder"));
        Take(2, 9, new Trader());
        Take(2, 10, new OverAmbitious());
        Take(3, 9, new BigSpender("Tier-3 big spender", stars: 3));
        Take(4, 9, new BigSpender("Tier-4 spender", stars: 1));

        void Take(int tier, int rank, EconManager manager)
        {
            var club = Tier(tier).First(candidate => candidate.TargetRank == rank);

            club.Manager = manager;
        }
    }

    private void EndSeason()
    {
        for (var tier = 1; tier <= Tiers; tier++)
        {
            var rank = 0;

            foreach (var club in Table(tier))
            {
                club.Rank = ++rank;
            }
        }

        foreach (var club in Clubs)
        {
            var prize = Rules.Prize(club.Tier, club.Rank);

            club.Cash += prize;
            club.Books.Prize += prize;
            club.ShadowMin = Math.Min(club.ShadowMin, club.ShadowCash);
        }

        foreach (var club in Clubs)
        {
            Rows.Add(MakeRow(club));
        }

        foreach (var club in Clubs.Where(club => club.Manager is not null))
        {
            club.Manager!.OnSeasonEnd(this, club);
        }

        if (Season == Seasons)
        {
            return;
        }

        // Nothing stays on the market over the rollover, and no bid stays reserved.
        foreach (var listing in Listings)
        {
            listing.Player.Listed = false;
        }

        foreach (var club in Clubs)
        {
            club.Reserved = 0;
        }

        Listings.Clear();

        foreach (var club in Clubs)
        {
            club.Fans = FanRuleSet.AfterSeason(club.Fans, club.Tier, club.Rank - club.TargetRank);
            club.FirstSeason = false;
        }

        var movements = PromotionRelegation.Compute(
            [.. Enumerable.Range(1, Tiers).Select(tier => new TierStandings(tier, [.. Table(tier).Select(club => club.Guid)]))]);
        var byGuid = Clubs.ToDictionary(club => club.Guid);

        foreach (var movement in movements.Where(movement => !movement.IsStationary))
        {
            var club = byGuid[movement.ClubId];

            club.Fans = FanRuleSet.OnTierChange(club.Fans, movement.FromTier, movement.ToTier);
            club.Tier = movement.ToTier;
        }

        RolloverSquads();
    }

    private ClubSeasonRow MakeRow(SimClub club) => new(
        Seed,
        Season,
        club.Idx,
        club.Name,
        club.Manager?.Name ?? "AI",
        club.Tier,
        club.Rank,
        club.Points,
        club.Fans.Fans,
        club.Seats.Total,
        club.SnackLevel,
        club.MerchLevel,
        club.Books,
        club.Cash,
        club.ShadowCash,
        club.ShadowMin,
        club.Squad.Count,
        club.Squad.Count == 0 ? 0 : club.Squad.Average(player => player.Ability),
        club.Squad.Count(player => player.Ability >= 15),
        club.Squad.Count(player => player.Ability >= 17),
        club.Squad.Count(player => player.Ability >= 19),
        club.WageBill,
        ForecastSeasonIncome(club),
        club.Strength,
        club.SeasonStartCash);

    // ---- retirements and contracts (CON-6 to CON-11) ----------------------------------------------------------------------------------

    private void RolloverSquads()
    {
        var seasonGuid = MakeGuid(2, Season);
        var nextYear = GameYear + 1;

        foreach (var club in Clubs)
        {
            foreach (var player in club.Squad.ToList())
            {
                var decision = RetirementPolicy.Decide(
                    seasonGuid,
                    player.Guid,
                    new RetirementInput(player.AgeIn(nextYear), player.IsGoalkeeper, player.Ability, player.ConditionBp, player.RetireAnnounced));

                if (decision == RetirementDecision.Retire)
                {
                    club.Squad.Remove(player);
                    player.Club = null;
                }
                else if (decision == RetirementDecision.Announce)
                {
                    player.RetireAnnounced = true;
                }
            }
        }

        foreach (var club in Clubs)
        {
            RenewContracts(club, nextYear);
            TakeInYouth(club);
            Repair(club);

            var order = club.Squad.OrderByDescending(player => player.Ability).ThenBy(player => player.Id).ToList();

            for (var rank = 0; rank < order.Count; rank++)
            {
                order[rank].Status = rank switch
                {
                    < 2 => SquadStatus.KeyPlayer,
                    < 12 => SquadStatus.FirstTeam,
                    < 18 => SquadStatus.Rotation,
                    _ => SquadStatus.Prospect,
                };
            }
        }
    }

    private void RenewContracts(SimClub club, int nextYear)
    {
        var order = club.Squad.OrderByDescending(player => player.Ability).ThenBy(player => player.Id).ToList();

        for (var rank = 0; rank < order.Count; rank++)
        {
            order[rank].Appearances = AppearancesFor(rank);
        }

        var stable = club.Squad.Where(player => player.ContractEnd > Season).ToList();
        var expiring = club.Squad.Where(player => player.ContractEnd <= Season).ToList();

        if (expiring.Count == 0)
        {
            return;
        }

        IReadOnlyList<AiContractDecision> decisions = club.Manager is not null
            ? [.. expiring.Select(player => new AiContractDecision(player.Guid, Renew: true, Seasons: 2))]
            : AiContractPolicy.Decide(
                stable.Count,
                stable.Count(player => player.IsGoalkeeper),
                [.. expiring.Select(player => new AiContractPlayer(player.Guid, player.Ability, player.AgeIn(nextYear), player.IsGoalkeeper))]);

        foreach (var decision in decisions)
        {
            var player = expiring.First(candidate => candidate.Guid == decision.PlayerId);

            if (decision.Renew)
            {
                player.Wage = Rules.RenewalWagePerWeek(
                    new ContractRenewalInput(
                        player.Ability,
                        player.Potential,
                        player.AgeIn(nextYear),
                        player.Appearances,
                        player.MoraleBp,
                        club.Tier,
                        RemainingSeasons: 0),
                    decision.Seasons);
                player.ContractEnd = Season + decision.Seasons;
            }
            else
            {
                Detach(club, player);
            }
        }
    }

    private static readonly PlayerPosition[] YouthPositions =
    [
        PlayerPosition.Goalkeeper,
        PlayerPosition.CentreBack,
        PlayerPosition.CentralMidfielder,
        PlayerPosition.Striker,
        PlayerPosition.LeftBack,
        PlayerPosition.RightWinger,
        PlayerPosition.DefensiveMidfielder,
        PlayerPosition.CentreBack,
        PlayerPosition.AttackingMidfielder,
        PlayerPosition.RightBack,
        PlayerPosition.LeftWinger,
        PlayerPosition.CentralMidfielder,
    ];

    /// <summary>The yearly youth intake: a few 17 and 18-year-olds on three-year contracts, never past the registered maximum (plan toasty-scott, answer to the supply finding).</summary>
    private void TakeInYouth(SimClub club)
    {
        for (var index = 0; index < YouthPerClub && club.Squad.Count < WorldRuleSet.SquadMaximumRegistered; index++)
        {
            var position = YouthPositions[(club.Idx + (Season * 5) + index) % YouthPositions.Length];
            var member = PlayerGenerator.GenerateYouth(
                _seedText,
                NamePool,
                CountryCode,
                _worldId,
                club.Guid,
                club.Tier,
                MakeGuid(2, Season + 1),
                Season + 1,
                GameYear + 1,
                position,
                (Season * 100) + index,
                Now);

            var player = FromGenerated(member.Player, member.Attributes, member.State, gem: false);

            player.Club = club;
            player.Wage = Rules.GeneratedWagePerWeek(player.Ability, club.Tier);
            player.ContractEnd = member.Contract.EndSeasonNumber;
            player.Status = SquadStatus.Prospect;
            club.Squad.Add(player);
        }
    }

    /// <summary>A club below the legal minimum, or without two goalkeepers, is given generated players (SQ-8, SQ-9).</summary>
    private void Repair(SimClub club)
    {
        var added = 0;

        while ((club.Squad.Count < WorldRuleSet.SquadMinimumRegistered
                || club.Squad.Count(player => player.IsGoalkeeper) < WorldRuleSet.MinimumGoalkeepers)
            && added < WorldRuleSet.SquadMinimumRegistered + WorldRuleSet.MinimumGoalkeepers)
        {
            var position = club.Squad.Count(player => player.IsGoalkeeper) < WorldRuleSet.MinimumGoalkeepers
                ? PlayerPosition.Goalkeeper
                : OutfieldPositions[added % OutfieldPositions.Length];

            var member = PlayerGenerator.GenerateEmergencyReplacement(
                _seedText,
                NamePool,
                CountryCode,
                _worldId,
                club.Guid,
                club.Tier,
                MakeGuid(2, Season + 1),
                Season + 1,
                GameYear + 1,
                position,
                _replacementOrdinal++,
                Now);

            var player = FromGenerated(member.Player, member.Attributes, member.State, gem: false);

            player.Club = club;
            player.Wage = Rules.GeneratedWagePerWeek(player.Ability, club.Tier);
            player.ContractEnd = Season + 1;
            player.Status = SquadStatus.Prospect;
            club.Squad.Add(player);
            added++;
        }
    }

    // ---- the clean start (plan section 8; not a rule of the game today) ---------------------------------------------------------------

    /// <summary>
    /// A manager takes over the tier-1 AI club with the most cash and starts clean: the old players are released, the club returns to its tier's
    /// default seeded squad, opening ground and buildings, opening cash and no debt, and its fans to the tier's opening numbers (FAN-9).
    /// </summary>
    private void TakeOverWithCleanStart()
    {
        var club = Tier(1).Where(candidate => candidate.Manager is null).OrderByDescending(candidate => candidate.Cash).ThenBy(candidate => candidate.Idx).First();
        var cashBefore = club.Cash;
        var billBefore = club.WageBill;
        var capacityBefore = club.Seats.Total;
        var strengthBefore = club.Strength;
        var squadBefore = club.Squad.Count;

        foreach (var listing in Listings.Where(listing => listing.Seller == club || listing.LeadingClub == club).ToList())
        {
            if (listing.LeadingClub is { } leader)
            {
                leader.Reserved -= listing.Leading ?? 0;
            }

            listing.Player.Listed = false;
            Listings.Remove(listing);
        }

        foreach (var player in club.Squad.ToList())
        {
            player.Listed = false;
            Detach(club, player);
        }

        var request = new SquadGenerationRequest(
            _seedText + "-clean",
            NamePool,
            CountryCode,
            _worldId,
            club.Guid,
            1_000 + (club.Idx * 10) + Season,
            club.Tier,
            MakeGuid(2, Season),
            Season,
            GameYear,
            Now);

        foreach (var member in PlayerGenerator.GenerateSquad(request))
        {
            var player = FromGenerated(member.Player, member.Attributes, member.State, gem: false);

            player.Club = club;
            player.Wage = Rules.GeneratedWagePerWeek(player.Ability, club.Tier);
            player.ContractEnd = member.Contract.EndSeasonNumber;
            player.Status = member.Contract.SquadStatus;
            club.Squad.Add(player);
        }

        club.Seats = new StadiumSeats(
            StadiumRuleSet.OpeningStanding,
            StadiumRuleSet.OpeningSeating,
            StadiumRuleSet.OpeningCoveredSeating,
            StadiumRuleSet.OpeningVip);
        club.SnackLevel = 1;
        club.MerchLevel = 1;
        club.Reserved = 0;
        club.Books.CleanStart = Rules.OpeningCash(club.Tier) - club.Cash;
        club.Cash = Rules.OpeningCash(club.Tier);
        club.GrantsTotal = 0;
        club.ShadowMin = club.Cash;
        club.Fans = FanRuleSet.OnTakeover(club.Tier);
        club.Manager = new PrudentBuilder("Clean-start taker");

        ComputeStrengths();

        var forecast = ForecastSeasonIncome(club);

        CleanStarts.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"season {Season}: {club.Name} (cash {cashBefore / 100:N0}, {squadBefore} players on {billBefore * Weeks / 100:N0} a season, ground {capacityBefore:N0}, strength {strengthBefore / 100.0:0.00}) "
            + $"-> opening cash {club.Cash / 100:N0}, {club.Squad.Count} players on {club.WageBill * Weeks / 100:N0} a season "
            + $"= {(forecast <= 0 ? 0 : 100.0 * club.WageBill * Weeks / forecast):0}% of a forecast income of {forecast / 100:N0}, strength {club.Strength / 100.0:0.00}"));
    }
}
