using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Training;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.SimulationBenchmarks;

// The economy sandbox's state (plan toasty-scott, Milestone 0): plain mutable classes, nothing persisted.

internal sealed class SimPlayer
{
    public int Id { get; init; }

    public Guid Guid { get; init; }

    public PlayerPosition Position { get; init; }

    public PlayerPosition[] Secondary { get; init; } = [];

    public int[] Attr { get; set; } = [];

    public int[] Progress { get; set; } = new int[AttributeNames.Count];

    public int Potential { get; init; }

    public int BirthYear { get; init; }

    public int Aptitude { get; init; }

    public int ConditionBp { get; set; }

    public int FatigueBp { get; set; }

    public int MoraleBp { get; set; } = 5_000;

    public int SharpnessBp { get; set; } = 5_000;

    public SimClub? Club { get; set; }

    /// <summary>The wage for one of the season's 34 settlements.</summary>
    public long Wage { get; set; }

    public int ContractEnd { get; set; }

    public SquadStatus Status { get; set; } = SquadStatus.Rotation;

    public int Appearances { get; set; }

    public bool RetireAnnounced { get; set; }

    public bool IsGem { get; init; }

    /// <summary>The fee a scripted trader paid for him, 0 when he was not bought to be sold on.</summary>
    public long BoughtFee { get; set; }

    public int BoughtSeason { get; set; }

    public int BoughtAbility { get; set; }

    public int BoughtPotential { get; set; }

    public long WagesSinceBought { get; set; }

    public bool Listed { get; set; }

    public bool BoughtFromPool { get; set; }

    private int _ability = -1;

    /// <summary>The mean of his 28 attributes, rounded: the "ability" the wage and the value read.</summary>
    public int Ability => _ability >= 0 ? _ability : _ability = (int)Math.Round(Attr.Average(), MidpointRounding.AwayFromZero);

    /// <summary>The mean of the attributes his position uses (24 for an outfield player, 4 for a keeper): what the fans' strength reads.</summary>
    public double OwnSkill => TeamStrength.AbilityOf(Attr, IsGoalkeeper) / 100.0;

    public bool IsGoalkeeper => Position == PlayerPosition.Goalkeeper;

    public PositionFamily Family => PlayerPositions.FamilyOf(Position);

    public int AgeIn(int gameYear) => gameYear - BirthYear;

    private PlayerAttributeSet? _set;

    public PlayerAttributeSet AttrSet => _set ??= PlayerAttributeSet.FromValues(Attr);

    public void SetAttributes(int[] attributes)
    {
        Attr = attributes;
        _ability = -1;
        _set = null;
    }

    public AiSquadPlayer ToAi() => new(Guid, Position, Secondary, Attr, ConditionBp, IsAvailable: true);
}

/// <summary>One season's books for a club, in minor units.</summary>
internal sealed class Books
{
    public long Fixed;
    public long Gate;
    public long Snack;
    public long Merch;
    public long Prize;
    public long FeesReceived;
    public long Wages;
    public long Operating;
    public long Upkeep;
    public long FeesPaid;
    public long Building;
    public long Upgrades;
    public long Grants;
    public long CleanStart;
    public int HomeMatches;
    public long Attendance;
    public int SoldOut;

    public long Income => Fixed + Gate + Snack + Merch + Prize;

    /// <summary>The income the season's play earns, without the prize money that comes after the last match.</summary>
    public long IncomeBeforePrize => Fixed + Gate + Snack + Merch;

    public long Recurring => Wages + Operating + Upkeep;
}

internal sealed class SimClub
{
    public int Idx { get; init; }

    public Guid Guid { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Tier { get; set; }

    public EconManager? Manager { get; set; }

    public long Cash { get; set; }

    public long Reserved { get; set; }

    /// <summary>Cash less every grant it has been given: where the club would be if nobody had bailed it out.</summary>
    public long GrantsTotal { get; set; }

    public long SeasonStartCash { get; set; }

    public long ShadowCash => Cash - GrantsTotal;

    public long ShadowMin { get; set; }

    public FanBaseState Fans { get; set; }

    public StadiumSeats Seats { get; set; }

    public int SnackLevel { get; set; } = 1;

    public int MerchLevel { get; set; } = 1;

    public List<SimPlayer> Squad { get; } = [];

    public FanForm Form { get; set; } = FanForm.None;

    public int Points { get; set; }

    public int GoalsFor { get; set; }

    public int GoalsAgainst { get; set; }

    public int Played { get; set; }

    public int TargetRank { get; set; }

    public int Strength { get; set; }

    public int Rank { get; set; }

    public bool FirstSeason { get; set; } = true;

    public int Standing { get; set; }

    public Books Books { get; set; } = new();

    public Queue<double> RecentFill { get; } = new();

    public long Available => Cash - Reserved;

    public long WageBill => Squad.Sum(player => player.Wage);

    public void ResetSeason()
    {
        Form = FanForm.None;
        Points = 0;
        GoalsFor = 0;
        GoalsAgainst = 0;
        Played = 0;
        Books = new Books();
        RecentFill.Clear();
        SeasonStartCash = Cash;
    }
}

internal sealed class SimListing
{
    public Guid Id { get; init; }

    public SimPlayer Player { get; init; } = null!;

    /// <summary>The seller, or null for a free agent of the weekly pool.</summary>
    public SimClub? Seller { get; init; }

    public long MinimumFee { get; init; }

    public long BuyerWage { get; init; }

    public int Seasons { get; init; }

    public long? Leading { get; set; }

    public SimClub? LeadingClub { get; set; }

    public int OpenedRound { get; init; }

    public bool Released { get; init; }

    public bool IsPool => Seller is null;
}

internal sealed record TransferRecord(int Season, int Week, int Ability, int Age, int Potential, long Fee, int SellerTier, int BuyerTier, bool Pool);

internal sealed record TradeRecord(
    int Season,
    string Club,
    int BoughtSeason,
    int SoldSeason,
    int AbilityIn,
    int AbilityOut,
    int PotentialIn,
    long FeeIn,
    long FeeOut,
    long Wages,
    bool Gem,
    bool FromPool)
{
    public long Profit => FeeOut - FeeIn - Wages;

    public double ReturnPercent => FeeIn <= 0 ? 0 : 100.0 * Profit / FeeIn;

    public double Multiple => FeeIn <= 0 ? 0 : (double)FeeOut / FeeIn;
}

/// <summary>One club's season, as the report reads it.</summary>
internal sealed record ClubSeasonRow(
    ulong Seed,
    int Season,
    int ClubIdx,
    string Club,
    string Manager,
    int Tier,
    int Rank,
    int Points,
    int Fans,
    int Capacity,
    int SnackLevel,
    int MerchLevel,
    Books Books,
    long CashEnd,
    long ShadowEnd,
    long ShadowMin,
    int SquadSize,
    double MeanAbility,
    int Players15,
    int Players17,
    int Players19,
    long WageBillPerSettlement,
    long ForecastIncome,
    int Strength,
    long CashStart)
{
    /// <summary>Whether the season's books explain every movement of the club's cash.</summary>
    public bool Reconciled => CashEnd == CashStart + Books.Income + Books.FeesReceived - Books.Wages - Books.Operating - Books.Upkeep
        - Books.FeesPaid - Books.Building - Books.Upgrades + Books.Grants + Books.CleanStart;

    public long Wages => Books.Wages;

    public long Income => Books.Income;

    public double WageShare => Books.Income <= 0 ? 0 : (double)Books.Wages / Books.Income;

    public double WageShareBeforePrize => Books.IncomeBeforePrize <= 0 ? 0 : (double)Books.Wages / Books.IncomeBeforePrize;
}
