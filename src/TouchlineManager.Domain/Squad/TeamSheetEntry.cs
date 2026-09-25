using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>Whether an entry is a starter or a substitute (`SQ-4`).</summary>
public enum TeamSheetDesignation
{
    /// <summary>One of the eleven starters.</summary>
    Starter = 0,

    /// <summary>One of the substitutes.</summary>
    Substitute = 1,
}

/// <summary>Stable codes and storage representation for <see cref="TeamSheetDesignation"/>.</summary>
public static class TeamSheetDesignations
{
    /// <summary>The code for <see cref="TeamSheetDesignation.Starter"/>.</summary>
    public const string StarterCode = "starter";

    /// <summary>The code for <see cref="TeamSheetDesignation.Substitute"/>.</summary>
    public const string SubstituteCode = "substitute";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 10;

    /// <summary>Converts a designation to its stable code.</summary>
    /// <param name="designation">The designation.</param>
    public static string ToCode(this TeamSheetDesignation designation) => designation switch
    {
        TeamSheetDesignation.Starter => StarterCode,
        TeamSheetDesignation.Substitute => SubstituteCode,
        _ => throw new ArgumentOutOfRangeException(nameof(designation), designation, "Unknown designation."),
    };

    /// <summary>Parses a stable code back to its designation.</summary>
    /// <param name="code">The stable code.</param>
    public static TeamSheetDesignation FromCode(string code) => code switch
    {
        StarterCode => TeamSheetDesignation.Starter,
        SubstituteCode => TeamSheetDesignation.Substitute,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown designation code."),
    };
}

/// <summary>
/// One player's place in a fixture team sheet (`SQ-4`).
/// </summary>
/// <remarks>
/// <para>
/// The slot numbers are unique across the whole sheet rather than per designation: starters take slots
/// 1–11 and substitutes 12–18. That is what lets a single `unique (team_sheet_id, slot_number)` index
/// exist (`data-model.md` §3.2) and what makes "sheet/slot" mean one thing.
/// </para>
/// <para>
/// A role override is optional: it lets one fixture ask a player to do a different job from the slot's
/// own role without editing the club's plan.
/// </para>
/// </remarks>
public sealed class TeamSheetEntry
{
    /// <summary>The first slot number, which is the first starter's.</summary>
    public const int FirstSlotNumber = 1;

    /// <summary>The last slot number, which is the last substitute's.</summary>
    public const int LastSlotNumber = WorldRuleSet.TeamSheetStarters + WorldRuleSet.TeamSheetSubstitutes;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TeamSheetEntry()
    {
    }

    /// <summary>Gets the entry identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning team sheet.</summary>
    public Guid TeamSheetId { get; private set; }

    /// <summary>Gets the selected player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets whether the player starts or sits on the bench.</summary>
    public TeamSheetDesignation Designation { get; private set; }

    /// <summary>Gets the slot number. Starters take 1–11, substitutes 12–18.</summary>
    public int SlotNumber { get; private set; }

    /// <summary>Gets the role override for this fixture, if the manager set one.</summary>
    public PlayerRole? RoleOverride { get; private set; }

    /// <summary>Gets when the entry was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the entry was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Selects a player into a sheet.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="teamSheetId">The owning team sheet.</param>
    /// <param name="playerId">The selected player.</param>
    /// <param name="designation">Whether the player starts or is a substitute.</param>
    /// <param name="slotNumber">The slot number. Starters 1–11, substitutes 12–18.</param>
    /// <param name="roleOverride">An optional role override for this fixture.</param>
    /// <param name="now">The current instant.</param>
    public static TeamSheetEntry Select(
        Guid id,
        Guid teamSheetId,
        Guid playerId,
        TeamSheetDesignation designation,
        int slotNumber,
        PlayerRole? roleOverride,
        DateTimeOffset now)
    {
        if (slotNumber is < FirstSlotNumber or > LastSlotNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotNumber),
                slotNumber,
                $"A team-sheet slot is between {FirstSlotNumber} and {LastSlotNumber} (SQ-4).");
        }

        var isStarterSlot = slotNumber <= WorldRuleSet.TeamSheetStarters;

        if (isStarterSlot != (designation == TeamSheetDesignation.Starter))
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotNumber),
                slotNumber,
                $"Starters take slots 1–{WorldRuleSet.TeamSheetStarters} and substitutes slots {WorldRuleSet.TeamSheetStarters + 1}–{LastSlotNumber} (SQ-4).");
        }

        return new TeamSheetEntry
        {
            Id = id,
            TeamSheetId = teamSheetId,
            PlayerId = playerId,
            Designation = designation,
            SlotNumber = slotNumber,
            RoleOverride = roleOverride,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Sets or clears the role override.</summary>
    /// <param name="roleOverride">The role override, or null to clear it.</param>
    /// <param name="now">The current instant.</param>
    public void OverrideRole(PlayerRole? roleOverride, DateTimeOffset now)
    {
        RoleOverride = roleOverride;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
