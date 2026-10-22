using System.Globalization;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.SimulationBenchmarks;

// The market half of the sandbox: the weekly free-agent pool, club listings, the highest-bid auction and the AI clubs' evaluation.
internal sealed partial class EconSim
{
    // ---- the weekly pool of free agents (FA-1 to FA-8) --------------------------------------------------------------------------------

    /// <summary>A batch closes at the next batch: a leading bid signs the player, a listing with no bid disappears (FA-5). The fee leaves the game.</summary>
    private void ResolvePool()
    {
        foreach (var listing in Listings.Where(listing => listing.IsPool).ToList())
        {
            Listings.Remove(listing);
            listing.Player.Listed = false;

            if (listing.LeadingClub is { } buyer)
            {
                buyer.Reserved -= listing.Leading!.Value;

                if (buyer.Squad.Count < WorldRuleSet.SquadMaximumRegistered && buyer.Cash >= listing.Leading.Value)
                {
                    buyer.Cash -= listing.Leading.Value;
                    buyer.Books.FeesPaid += listing.Leading.Value;
                    Sign(buyer, listing, listing.Leading.Value);
                    Transfers.Add(new TransferRecord(Season, Week, listing.Player.Ability, listing.Player.AgeIn(GameYear), listing.Player.Potential, listing.Leading.Value, 0, buyer.Tier, Pool: true, EconRules.Value(listing.Player.AbilityTenths, listing.Player.AgeIn(GameYear))));
                }
            }
        }
    }

    private void OpenPool()
    {
        var week = TrainingBase.AddDays(((Season - 1) * 238) + (Week * 7));
        var contracted = Clubs.Sum(club => club.Squad.Count);
        var request = new FreeAgentBatchRequest(
            _seedText,
            _worldId,
            week,
            GameYear,
            [new FreeAgentCountry(CountryCode, NamePool, contracted)],
            Now);

        foreach (var generated in FreeAgentGenerator.Generate(request))
        {
            var player = FromGenerated(generated.Player, generated.Attributes, generated.State, generated.IsGem);
            var quote = EconRules.FreeAgent(player.Ability, player.AgeIn(GameYear), released: false);

            AddPoolListing(player, quote, released: false);
        }

        foreach (var player in _releasedWaiting)
        {
            var quote = EconRules.FreeAgent(player.Ability, player.AgeIn(GameYear), released: true);

            AddPoolListing(player, quote, released: true);
        }

        _releasedWaiting.Clear();
    }

    private void AddPoolListing(SimPlayer player, EconFreeAgentQuote quote, bool released)
    {
        player.Listed = true;
        Listings.Add(new SimListing
        {
            Id = MakeGuid(5, _listingOrdinal++),
            Player = player,
            Seller = null,
            MinimumFee = quote.FeeMinor,
            BuyerWage = quote.SeasonWageMinor,
            Seasons = quote.Seasons,
            OpenedRound = Round,
            Released = released,
        });
    }

    // ---- club listings ----------------------------------------------------------------------------------------------------------------

    /// <summary>Resolves every club listing that has been open for at least a round: the highest bid wins if the deal still holds (TRF-9).</summary>
    private void ResolveListings()
    {
        foreach (var listing in Listings.Where(listing => !listing.IsPool && listing.OpenedRound < Round).ToList())
        {
            Listings.Remove(listing);
            listing.Player.Listed = false;

            var seller = listing.Seller!;

            if (listing.LeadingClub is not { } buyer)
            {
                // A player the board listed and nobody bought is released: his agent pays the club a share of his value (plan section 6).
                if (listing.Player.BoardListed && listing.Player.Club == seller)
                {
                    ReleaseUnsold(seller, listing.Player);
                }

                continue;
            }

            var fee = listing.Leading!.Value;

            buyer.Reserved -= fee;

            var goalkeepers = seller.Squad.Count(player => player.IsGoalkeeper) - (listing.Player.IsGoalkeeper ? 1 : 0);
            var valid = listing.Player.Club == seller
                && buyer.Cash >= fee
                && buyer.Squad.Count < WorldRuleSet.SquadMaximumRegistered
                && seller.Squad.Count - 1 >= WorldRuleSet.SquadMinimumRegistered
                && goalkeepers >= WorldRuleSet.MinimumGoalkeepers;

            if (!valid)
            {
                continue;
            }

            buyer.Cash -= fee;
            seller.Cash += fee;
            buyer.Books.FeesPaid += fee;
            seller.Books.FeesReceived += fee;

            var player = listing.Player;

            if (player.BoughtFee > 0)
            {
                Trades.Add(new TradeRecord(
                    Season,
                    seller.Name,
                    player.BoughtSeason,
                    Season,
                    player.BoughtAbility,
                    player.Ability,
                    player.BoughtPotential,
                    player.BoughtFee,
                    fee,
                    player.WagesSinceBought,
                    player.BoughtPotential >= 16,
                    player.BoughtFromPool));

                player.BoughtFee = 0;
                player.WagesSinceBought = 0;
            }

            seller.Squad.Remove(player);
            Transfers.Add(new TransferRecord(Season, Week, player.Ability, player.AgeIn(GameYear), player.Potential, fee, seller.Tier, buyer.Tier, Pool: false, EconRules.Value(player.AbilityTenths, player.AgeIn(GameYear))));
            Sign(buyer, listing, fee);
        }
    }

    private void Sign(SimClub buyer, SimListing listing, long fee)
    {
        var player = listing.Player;

        player.Club = buyer;
        player.Wage = listing.BuyerWage;
        player.ContractEnd = Season + listing.Seasons - 1;
        player.Status = listing.IsPool ? FreeAgentTerms.SquadStatusFor(player.AgeIn(GameYear)) : SquadStatus.Rotation;
        player.Appearances = 0;
        player.RetireAnnounced = false;
        buyer.Squad.Add(player);

        if (buyer.Manager is { } manager)
        {
            manager.OnSigned(this, buyer, player, fee, listing);
        }
    }

    /// <summary>The season wage a listing quotes the buyer: the renewal quote of the player, whatever club he plays for (CON-5).</summary>
    public long QuoteWage(SimPlayer player, int seasons) =>
        EconRules.RenewalWage(
            new ContractRenewalInput(
                player.AbilityTenths,
                player.AgeIn(GameYear),
                player.Appearances,
                player.MoraleBp,
                Math.Max(0, player.ContractEnd - Season)),
            seasons);

    public SimListing? ListPlayer(SimClub club, SimPlayer player, long minimumFee, int seasons)
    {
        if (player.Listed || player.Club != club)
        {
            return null;
        }

        var goalkeepers = club.Squad.Count(other => other.IsGoalkeeper) - (player.IsGoalkeeper ? 1 : 0);

        if (club.Squad.Count - 1 < WorldRuleSet.SquadMinimumRegistered || goalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            return null;
        }

        player.Listed = true;

        var listing = new SimListing
        {
            Id = MakeGuid(5, _listingOrdinal++),
            Player = player,
            Seller = club,
            MinimumFee = Math.Max(1, minimumFee),
            BuyerWage = QuoteWage(player, seasons),
            Seasons = seasons,
            OpenedRound = Round,
        };

        Listings.Add(listing);

        return listing;
    }

    /// <summary>A bid: at least the minimum fee, or the leading bid and an increment, from cash the club has free (TRF-5, TRF-7).</summary>
    public static bool PlaceBid(SimClub club, SimListing listing, long amount)
    {
        // A club in the red has a transfer embargo (plan section 6).
        if (club.FinState != FinancialStateKind.Healthy
            || listing.Seller == club
            || listing.LeadingClub == club
            || club.Squad.Count >= WorldRuleSet.SquadMaximumRegistered
            || !AuctionRules.IsAcceptableBid(listing.MinimumFee, listing.Leading, amount)
            || club.Available < amount)
        {
            return false;
        }

        if (listing.LeadingClub is { } previous)
        {
            previous.Reserved -= listing.Leading!.Value;
        }

        listing.Leading = amount;
        listing.LeadingClub = club;
        club.Reserved += amount;

        return true;
    }

    public static long MinimumBid(SimListing listing) => AuctionRules.MinimumAcceptableBid(listing.MinimumFee, listing.Leading);

    // ---- the AI clubs (TRF-12) --------------------------------------------------------------------------------------------------------

    private void AiMarketRound()
    {
        foreach (var club in Clubs.Where(club => !club.Managed).OrderBy(club => club.Guid))
        {
            var open = Listings
                .Where(listing => !listing.IsPool)
                .Select(listing => new AiMarketListing(
                    listing.Id,
                    listing.Seller!.Guid,
                    listing.LeadingClub?.Guid,
                    listing.Player.Guid,
                    listing.Player.Family,
                    listing.Player.AbilityTenths,
                    listing.Player.AgeIn(GameYear),
                    listing.MinimumFee,
                    listing.Leading,
                    listing.BuyerWage))
                .ToList();

            var squad = club.Squad
                .Select(player => new AiMarketPlayer(
                    player.Guid,
                    player.Family,
                    player.AbilityTenths,
                    player.AgeIn(GameYear),
                    player.Status,
                    player.ContractEnd,
                    Season,
                    player.Listed,
                    player.Wage))
                .ToList();

            var finances = new AiMarketFinances(
                club.WageBill,
                EconomyRuleSet.WageBudgetMinorFor(club.Forecast),
                AiFinancePolicy.ReserveMinorFor(club.WageBill),
                club.FinState != FinancialStateKind.Healthy);
            var plan = AiMarketPolicy.Decide(new AiMarketClub(club.Guid, Math.Max(0, club.Available), finances, squad), open);

            foreach (var decision in plan.Listings)
            {
                var player = club.Squad.FirstOrDefault(candidate => candidate.Guid == decision.PlayerId);

                if (player is not null)
                {
                    ListPlayer(club, player, decision.MinimumFeeMinor, decision.Seasons);
                }
            }

            foreach (var bid in plan.Bids)
            {
                var listing = Listings.FirstOrDefault(candidate => candidate.Id == bid.ListingId);

                if (listing is not null)
                {
                    PlaceBid(club, listing, bid.AmountMinor);
                }
            }
        }
    }

    // ---- scripted managers' tools -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Buys a player straight from an AI club at 105% of his value, whether or not he is listed. Not a game rule: the sandbox's way of putting a
    /// star in front of a scripted spender, so the economy is tested and not the market's supply.
    /// </summary>
    public SimPlayer? Poach(SimClub buyer, int minAbility, int maxAge, long maxFee, bool endow = false)
    {
        if (buyer.FinState != FinancialStateKind.Healthy)
        {
            return null;
        }

        SimPlayer? best = null;
        var bestFee = 0L;

        foreach (var seller in Clubs.Where(club => club != buyer && !club.Managed && club.Squad.Count > WorldRuleSet.SquadMinimumRegistered + 1))
        {
            foreach (var player in seller.Squad.Where(player => !player.Listed && player.Ability >= minAbility && player.AgeIn(GameYear) <= maxAge))
            {
                var fee = EconRules.Value(player.AbilityTenths, player.AgeIn(GameYear)) * 105 / 100;

                if (fee > maxFee || (!endow && fee > buyer.Available))
                {
                    continue;
                }

                // The cheapest player who is good enough: the test is what one more star costs a club, not how much it would pay for the best.
                if (best is null || player.Ability < best.Ability || (player.Ability == best.Ability && fee < bestFee))
                {
                    best = player;
                    bestFee = fee;
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        var from = best.Club!;
        var goalkeepers = from.Squad.Count(player => player.IsGoalkeeper) - (best.IsGoalkeeper ? 1 : 0);

        if (goalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            return null;
        }

        var wage = QuoteWage(best, 3);

        if (endow && bestFee > buyer.Available)
        {
            Gift(buyer, bestFee - buyer.Available);
        }

        buyer.Cash -= bestFee;
        from.Cash += bestFee;
        buyer.Books.FeesPaid += bestFee;
        from.Books.FeesReceived += bestFee;
        from.Squad.Remove(best);
        Transfers.Add(new TransferRecord(Season, Week, best.Ability, best.AgeIn(GameYear), best.Potential, bestFee, from.Tier, buyer.Tier, Pool: false, EconRules.Value(best.AbilityTenths, best.AgeIn(GameYear))));

        best.Club = buyer;
        best.Wage = wage;
        best.ContractEnd = Season + 2;
        best.Status = SquadStatus.KeyPlayer;
        buyer.Squad.Add(best);
        buyer.Manager?.OnSigned(this, buyer, best, bestFee, listing: null);

        return best;
    }

    /// <summary>Adds places to a ground, paid at once (STAD-4). Returns how many were built.</summary>
    public static int BuildPlaces(SimClub club, StadiumStand stand, int places)
    {
        if (club.FinState != FinancialStateKind.Healthy)
        {
            return 0;
        }

        var room = StadiumRuleSet.MaxCapacity - club.Seats.Total;
        var cost = EconRules.BuildCostPerPlace(stand, club.Tier);
        var affordable = cost <= 0 ? places : (int)Math.Min(places, Math.Max(0, club.Available) / cost);
        var built = Math.Max(0, Math.Min(Math.Min(places, room), affordable));

        if (built == 0)
        {
            return 0;
        }

        var seats = club.Seats;

        club.Seats = stand switch
        {
            StadiumStand.Standing => seats with { Standing = seats.Standing + built },
            StadiumStand.Seating => seats with { Seating = seats.Seating + built },
            StadiumStand.CoveredSeating => seats with { CoveredSeating = seats.CoveredSeating + built },
            _ => seats with { Vip = seats.Vip + built },
        };

        club.Cash -= built * cost;
        club.Books.Building += built * cost;

        return built;
    }

    public static bool UpgradeStructure(SimClub club, bool snackBar)
    {
        if (club.FinState != FinancialStateKind.Healthy)
        {
            return false;
        }

        var level = snackBar ? club.SnackLevel : club.MerchLevel;

        if (level >= StructureRuleSet.MaxLevel)
        {
            return false;
        }

        var cost = EconRules.UpgradeCost(level + 1, club.Tier);

        if (club.Available < cost)
        {
            return false;
        }

        club.Cash -= cost;
        club.Books.Upgrades += cost;

        if (snackBar)
        {
            club.SnackLevel++;
        }
        else
        {
            club.MerchLevel++;
        }

        return true;
    }

    /// <summary>Releases a player: half the wages left are paid at once and he joins the next free-agent batch (CON-12).</summary>
    public bool Release(SimClub club, SimPlayer player, long payout)
    {
        var goalkeepers = club.Squad.Count(other => other.IsGoalkeeper) - (player.IsGoalkeeper ? 1 : 0);

        if (player.Listed || club.Squad.Count - 1 < WorldRuleSet.SquadMinimumRegistered || goalkeepers < WorldRuleSet.MinimumGoalkeepers || club.Available < payout)
        {
            return false;
        }

        club.Cash -= payout;
        club.Books.FeesPaid += payout;
        Detach(club, player);

        return true;
    }

    private void ReleaseUnsold(SimClub club, SimPlayer player)
    {
        var goalkeepers = club.Squad.Count(other => other.IsGoalkeeper) - (player.IsGoalkeeper ? 1 : 0);

        if (club.Squad.Count - 1 < WorldRuleSet.SquadMinimumRegistered || goalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            return;
        }

        var fee = EconomyRuleSet.AdministrationReleaseFeeMinor(EconRules.Value(player.AbilityTenths, player.AgeIn(GameYear)));

        club.Cash += fee;
        club.Books.FeesReceived += fee;
        club.Books.BoardReleases++;
        Detach(club, player);
    }

    private void Detach(SimClub club, SimPlayer player)
    {
        club.Squad.Remove(player);
        player.Club = null;
        player.Wage = 0;
        player.BoardListed = false;
        _releasedWaiting.Add(player);
    }

    public IEnumerable<SimListing> OpenClubListings => Listings.Where(listing => !listing.IsPool);

    public IEnumerable<SimListing> OpenPoolListings => Listings.Where(listing => listing.IsPool);

    public string Describe(SimPlayer player) =>
        string.Create(CultureInfo.InvariantCulture, $"{player.Position} {player.Ability} (age {player.AgeIn(GameYear)}, ceiling {player.Potential})");
}
