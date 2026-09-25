using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// One of eleven positions in a tactical plan, with its family, role, and normalized coordinates
/// (`TAC-7`…`TAC-9`).
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are scaled integers on a 0–10,000 axis rather than floating point, because the same slot
/// layout has to mean the same thing to the pitch renderer, the selection validator, and the engine's
/// snapshot hash (`TAC-9`, and the determinism rule behind `MAT-1`).
/// </para>
/// <para>
/// The assigned player is the default lineup: a plan names who occupies each slot, and a fixture's team
/// sheet may later override it. Assignment is nullable because a plan is legal before the manager has
/// picked anybody.
/// </para>
/// </remarks>
public sealed class TacticalSlot
{
    /// <summary>The lowest slot number, which is the goalkeeper's.</summary>
    public const int FirstSlotNumber = 1;

    /// <summary>The highest slot number (`TAC-9`; a pitch has eleven positions).</summary>
    public const int LastSlotNumber = 11;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TacticalSlot()
    {
    }

    /// <summary>Gets the slot identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning plan.</summary>
    public Guid PlanId { get; private set; }

    /// <summary>Gets the slot number, 1–11.</summary>
    public int SlotNumber { get; private set; }

    /// <summary>Gets the position family the slot asks for (`INS-10`).</summary>
    public PositionFamily PositionFamily { get; private set; }

    /// <summary>Gets the role the slot asks its occupant to play (`TAC-8`).</summary>
    public PlayerRole Role { get; private set; }

    /// <summary>Gets the normalized x coordinate, 0–10,000 (`TAC-9`).</summary>
    public int NormalizedX { get; private set; }

    /// <summary>Gets the normalized y coordinate, 0–10,000 (`TAC-9`).</summary>
    public int NormalizedY { get; private set; }

    /// <summary>Gets the player assigned to the slot by default, if the manager has picked one.</summary>
    public Guid? AssignedPlayerId { get; private set; }

    /// <summary>Gets when the slot was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the slot was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Places a slot in a plan.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="planId">The owning plan.</param>
    /// <param name="slotNumber">The slot number, 1–11.</param>
    /// <param name="positionFamily">The position family the slot asks for.</param>
    /// <param name="role">The role the slot asks for.</param>
    /// <param name="normalizedX">The normalized x coordinate, 0–10,000.</param>
    /// <param name="normalizedY">The normalized y coordinate, 0–10,000.</param>
    /// <param name="assignedPlayerId">The player assigned by default, if any.</param>
    /// <param name="now">The current instant.</param>
    public static TacticalSlot Place(
        Guid id,
        Guid planId,
        int slotNumber,
        PositionFamily positionFamily,
        PlayerRole role,
        int normalizedX,
        int normalizedY,
        Guid? assignedPlayerId,
        DateTimeOffset now)
    {
        if (slotNumber is < FirstSlotNumber or > LastSlotNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotNumber),
                slotNumber,
                $"A slot number is between {FirstSlotNumber} and {LastSlotNumber} (TAC-8).");
        }

        EnsureCoordinate(normalizedX, nameof(normalizedX));
        EnsureCoordinate(normalizedY, nameof(normalizedY));

        return new TacticalSlot
        {
            Id = id,
            PlanId = planId,
            SlotNumber = slotNumber,
            PositionFamily = positionFamily,
            Role = role,
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
            AssignedPlayerId = assignedPlayerId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Assigns a player to the slot, or clears the assignment.</summary>
    /// <param name="playerId">The player, or null to clear.</param>
    /// <param name="now">The current instant.</param>
    public void Assign(Guid? playerId, DateTimeOffset now)
    {
        AssignedPlayerId = playerId;

        Touch(now);
    }

    /// <summary>Moves the slot to a new validated position (`TAC-7`).</summary>
    /// <param name="normalizedX">The normalized x coordinate, 0–10,000.</param>
    /// <param name="normalizedY">The normalized y coordinate, 0–10,000.</param>
    /// <param name="now">The current instant.</param>
    public void MoveTo(int normalizedX, int normalizedY, DateTimeOffset now)
    {
        EnsureCoordinate(normalizedX, nameof(normalizedX));
        EnsureCoordinate(normalizedY, nameof(normalizedY));

        NormalizedX = normalizedX;
        NormalizedY = normalizedY;

        Touch(now);
    }

    /// <summary>Changes the role the slot asks for (`TAC-8`).</summary>
    /// <param name="role">The role.</param>
    /// <param name="now">The current instant.</param>
    public void ChangeRole(PlayerRole role, DateTimeOffset now)
    {
        Role = role;

        Touch(now);
    }

    private static void EnsureCoordinate(int value, string parameterName)
    {
        if (value is < WorldRuleSet.SlotCoordinateMin or > WorldRuleSet.SlotCoordinateMax)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"A slot coordinate is between {WorldRuleSet.SlotCoordinateMin} and {WorldRuleSet.SlotCoordinateMax} (TAC-9).");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
