using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.World;

/// <summary>How many places of each kind a ground holds.</summary>
/// <param name="Standing">Standing places.</param>
/// <param name="Seating">Uncovered seats.</param>
/// <param name="CoveredSeating">Covered seats.</param>
/// <param name="Vip">VIP seats.</param>
public readonly record struct StadiumSeats(int Standing, int Seating, int CoveredSeating, int Vip)
{
    /// <summary>Gets every place in the ground.</summary>
    public int Total => Standing + Seating + CoveredSeating + Vip;

    /// <summary>Gets how many places of one kind the ground holds.</summary>
    /// <param name="stand">The kind of place.</param>
    public int Of(StadiumStand stand) => stand switch
    {
        StadiumStand.Standing => Standing,
        StadiumStand.Seating => Seating,
        StadiumStand.CoveredSeating => CoveredSeating,
        StadiumStand.Vip => Vip,
        _ => throw new ArgumentOutOfRangeException(nameof(stand), stand, "Unknown stadium stand."),
    };
}

/// <summary>
/// A club's stadium: how many places of each kind it holds (game rules §13.5, `STAD-1`…`STAD-6`).
/// </summary>
/// <remarks>
/// <para>
/// A ground has no level of its own. Its <see cref="Level"/> is a reading of its <see cref="Capacity"/> —
/// the number of 5,000-place blocks it spans — so the level and the picture of the ground can never
/// disagree with the seats a manager has bought, and a manager who adds a single seat past a block's end
/// moves the ground to the next level (`STAD-1`).
/// </para>
/// <para>
/// Seats are only ever added, never removed (`STAD-6`), and every addition is paid for by a ledger entry the
/// use case posts in the same unit of work. This type owns the shape of the ground; the money is the
/// finance module's.
/// </para>
/// </remarks>
public sealed class ClubStadium
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubStadium()
    {
    }

    /// <summary>Gets the stadium identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club that owns the ground. A club has exactly one.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the standing places.</summary>
    public int StandingSeats { get; private set; }

    /// <summary>Gets the uncovered seats.</summary>
    public int SeatingSeats { get; private set; }

    /// <summary>Gets the covered seats.</summary>
    public int CoveredSeats { get; private set; }

    /// <summary>Gets the VIP seats.</summary>
    public int VipSeats { get; private set; }

    /// <summary>Gets when the stadium was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the stadium was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets the places by kind.</summary>
    public StadiumSeats Seats => new(StandingSeats, SeatingSeats, CoveredSeats, VipSeats);

    /// <summary>Gets every place in the ground.</summary>
    public int Capacity => Seats.Total;

    /// <summary>Gets the ground's level, 1–10: how many 5,000-place blocks it spans (`STAD-1`).</summary>
    public int Level => StadiumRuleSet.LevelFor(Capacity);

    /// <summary>Opens a club's ground with the places every club starts with (`STAD-2`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The owning club.</param>
    /// <param name="now">The current instant.</param>
    public static ClubStadium Open(Guid id, Guid clubId, DateTimeOffset now) =>
        new()
        {
            Id = id,
            ClubId = clubId,
            StandingSeats = StadiumRuleSet.OpeningStanding,
            SeatingSeats = StadiumRuleSet.OpeningSeating,
            CoveredSeats = StadiumRuleSet.OpeningCoveredSeating,
            VipSeats = StadiumRuleSet.OpeningVip,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>Gets how many more places the ground can still take (`STAD-1`).</summary>
    public int RemainingRoom => StadiumRuleSet.MaxCapacity - Capacity;

    /// <summary>Adds places of one kind.</summary>
    /// <param name="stand">The kind of place.</param>
    /// <param name="count">How many to add, 1 and up.</param>
    /// <param name="now">The current instant.</param>
    /// <exception cref="InvalidOperationException">When the ground cannot hold that many more places.</exception>
    public void AddSeats(StadiumStand stand, int count, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        if (count > RemainingRoom)
        {
            throw new InvalidOperationException(
                $"The ground holds {Capacity} and takes at most {StadiumRuleSet.MaxCapacity}, so {count} more do not fit (STAD-1).");
        }

        switch (stand)
        {
            case StadiumStand.Standing:
                StandingSeats += count;
                break;
            case StadiumStand.Seating:
                SeatingSeats += count;
                break;
            case StadiumStand.CoveredSeating:
                CoveredSeats += count;
                break;
            case StadiumStand.Vip:
                VipSeats += count;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stand), stand, "Unknown stadium stand.");
        }

        UpdatedAt = now;
        Version++;
    }
}
