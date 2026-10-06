using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// One of the seven substitutes a plan names by default (`SQ-4`).
/// </summary>
/// <remarks>
/// <para>
/// A bench place has no position on the pitch, so it is not a <see cref="TacticalSlot"/>: it carries only a
/// number and a player. Numbers continue after the starters, 12–18, which is what the fixture team sheet
/// uses too, so a plan's bench and a sheet's bench read the same way.
/// </para>
/// <para>
/// Only filled places are stored. An empty place is the absence of a row, so a plan with no bench holds no
/// rows at all, and a plan saved before the bench existed needs no backfill.
/// </para>
/// </remarks>
public sealed class TacticalBenchSlot
{
    /// <summary>The first bench slot number, which follows the last starter's.</summary>
    public const int FirstSlotNumber = WorldRuleSet.TeamSheetStarters + 1;

    /// <summary>The last bench slot number.</summary>
    public const int LastSlotNumber = WorldRuleSet.TeamSheetStarters + WorldRuleSet.TeamSheetSubstitutes;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TacticalBenchSlot()
    {
    }

    /// <summary>Gets the bench place identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning plan.</summary>
    public Guid PlanId { get; private set; }

    /// <summary>Gets the slot number, 12–18.</summary>
    public int SlotNumber { get; private set; }

    /// <summary>Gets the substitute named for the place.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets when the place was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the place was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Names a substitute for a place on a plan's bench.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="planId">The owning plan.</param>
    /// <param name="slotNumber">The slot number, 12–18.</param>
    /// <param name="playerId">The substitute.</param>
    /// <param name="now">The current instant.</param>
    public static TacticalBenchSlot Place(Guid id, Guid planId, int slotNumber, Guid playerId, DateTimeOffset now)
    {
        if (slotNumber is < FirstSlotNumber or > LastSlotNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotNumber),
                slotNumber,
                $"A bench slot number is between {FirstSlotNumber} and {LastSlotNumber} (SQ-4).");
        }

        return new TacticalBenchSlot
        {
            Id = id,
            PlanId = planId,
            SlotNumber = slotNumber,
            PlayerId = playerId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Names a different substitute for the place.</summary>
    /// <param name="playerId">The substitute.</param>
    /// <param name="now">The current instant.</param>
    public void Assign(Guid playerId, DateTimeOffset now)
    {
        if (playerId == PlayerId)
        {
            return;
        }

        PlayerId = playerId;
        UpdatedAt = now;
        Version++;
    }
}
