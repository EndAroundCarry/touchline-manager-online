namespace TouchlineManager.Domain.Squad;

/// <summary>
/// The formation shape a plan starts from (`TAC-1`…`TAC-6`).
/// </summary>
/// <remarks>
/// A preset names the shape, not the exact coordinates: the slots carry their own normalized positions
/// (`TAC-7`, `TAC-9`), so a manager may drag within the shape's zones without the preset losing meaning.
/// </remarks>
public enum FormationPreset
{
    /// <summary>Four-four-two (`TAC-1`).</summary>
    FourFourTwo = 0,

    /// <summary>Four-three-three (`TAC-2`).</summary>
    FourThreeThree = 1,

    /// <summary>Four-two-three-one (`TAC-3`).</summary>
    FourTwoThreeOne = 2,

    /// <summary>Four-one-four-one (`TAC-4`).</summary>
    FourOneFourOne = 3,

    /// <summary>Three-five-two (`TAC-5`).</summary>
    ThreeFiveTwo = 4,

    /// <summary>Five-three-two (`TAC-6`).</summary>
    FiveThreeTwo = 5,
}

/// <summary>Stable codes and storage representation for <see cref="FormationPreset"/>.</summary>
public static class FormationPresets
{
    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 7;

    /// <summary>Every preset, in declaration order.</summary>
    public static readonly IReadOnlyList<FormationPreset> All =
    [
        FormationPreset.FourFourTwo,
        FormationPreset.FourThreeThree,
        FormationPreset.FourTwoThreeOne,
        FormationPreset.FourOneFourOne,
        FormationPreset.ThreeFiveTwo,
        FormationPreset.FiveThreeTwo,
    ];

    /// <summary>Converts a preset to its stable code.</summary>
    /// <param name="preset">The preset.</param>
    public static string ToCode(this FormationPreset preset) => preset switch
    {
        FormationPreset.FourFourTwo => "4-4-2",
        FormationPreset.FourThreeThree => "4-3-3",
        FormationPreset.FourTwoThreeOne => "4-2-3-1",
        FormationPreset.FourOneFourOne => "4-1-4-1",
        FormationPreset.ThreeFiveTwo => "3-5-2",
        FormationPreset.FiveThreeTwo => "5-3-2",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown formation preset."),
    };

    /// <summary>Parses a stable code back to its preset.</summary>
    /// <param name="code">The stable code.</param>
    public static FormationPreset FromCode(string code) => code switch
    {
        "4-4-2" => FormationPreset.FourFourTwo,
        "4-3-3" => FormationPreset.FourThreeThree,
        "4-2-3-1" => FormationPreset.FourTwoThreeOne,
        "4-1-4-1" => FormationPreset.FourOneFourOne,
        "3-5-2" => FormationPreset.ThreeFiveTwo,
        "5-3-2" => FormationPreset.FiveThreeTwo,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown formation preset code."),
    };
}

/// <summary>
/// A club's saved formation, slot layout, roles, and team instructions (`INS-11`).
/// </summary>
/// <remarks>
/// <para>
/// A plan is a versioned document because a team sheet references the version it was built from: a
/// fixture prepared against version 3 must stay interpretable after the manager saves version 4
/// (`data-model.md` §3.2). Every save bumps <see cref="Version"/>, which is also the ETag the API
/// exposes for conditional writes.
/// </para>
/// <para>
/// A club has exactly one default plan, enforced by a partial unique index on `is_default`. The plan
/// itself does not police that index — promoting one plan and demoting another is one workflow across
/// two rows, which the use case in a later Stage 4 milestone owns.
/// </para>
/// </remarks>
public sealed class TacticalPlan
{
    /// <summary>The longest plan name, so a column can be sized to hold one.</summary>
    public const int MaxNameLength = 64;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private TacticalPlan()
    {
    }

    /// <summary>Gets the plan identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning club.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the manager-facing plan name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the formation shape the plan starts from.</summary>
    public FormationPreset FormationPreset { get; private set; }

    /// <summary>Gets the overall approach.</summary>
    public Mentality Mentality { get; private set; }

    /// <summary>Gets how quickly the team moves the ball.</summary>
    public Tempo Tempo { get; private set; }

    /// <summary>Gets what kind of pass the team favours.</summary>
    public PassingStyle Passing { get; private set; }

    /// <summary>Gets how far the team spreads across the pitch.</summary>
    public Width Width { get; private set; }

    /// <summary>Gets where the team begins to press.</summary>
    public Pressing Pressing { get; private set; }

    /// <summary>Gets how high the defensive line holds.</summary>
    public DefensiveLine DefensiveLine { get; private set; }

    /// <summary>Gets how committed the tackling is.</summary>
    public TacklingStyle Tackling { get; private set; }

    /// <summary>Gets whether the team runs down the clock.</summary>
    public TimeWasting TimeWasting { get; private set; }

    /// <summary>Gets whether this is the club's default plan (`INS-11`).</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Gets when the plan was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the plan was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the plan version. A team sheet references the version it was prepared against.</summary>
    public long Version { get; private set; }

    /// <summary>Creates a plan.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The owning club.</param>
    /// <param name="name">The manager-facing name.</param>
    /// <param name="formationPreset">The formation shape.</param>
    /// <param name="instructions">The eight team instructions.</param>
    /// <param name="isDefault">Whether this is the club's default plan.</param>
    /// <param name="now">The current instant.</param>
    public static TacticalPlan Create(
        Guid id,
        Guid clubId,
        string name,
        FormationPreset formationPreset,
        TeamInstructionSet instructions,
        bool isDefault,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(instructions);

        if (name.Trim().Length > MaxNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                name.Length,
                $"A plan name is at most {MaxNameLength} characters.");
        }

        return new TacticalPlan
        {
            Id = id,
            ClubId = clubId,
            Name = name.Trim(),
            FormationPreset = formationPreset,
            Mentality = instructions.Mentality,
            Tempo = instructions.Tempo,
            Passing = instructions.Passing,
            Width = instructions.Width,
            Pressing = instructions.Pressing,
            DefensiveLine = instructions.DefensiveLine,
            Tackling = instructions.Tackling,
            TimeWasting = instructions.TimeWasting,
            IsDefault = isDefault,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Gets the plan's team instructions as one value.</summary>
    public TeamInstructionSet Instructions => new()
    {
        Mentality = Mentality,
        Tempo = Tempo,
        Passing = Passing,
        Width = Width,
        Pressing = Pressing,
        DefensiveLine = DefensiveLine,
        Tackling = Tackling,
        TimeWasting = TimeWasting,
    };

    /// <summary>Replaces the formation, instructions, and name.</summary>
    /// <param name="name">The manager-facing name.</param>
    /// <param name="formationPreset">The formation shape.</param>
    /// <param name="instructions">The eight team instructions.</param>
    /// <param name="now">The current instant.</param>
    public void Revise(
        string name,
        FormationPreset formationPreset,
        TeamInstructionSet instructions,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(instructions);

        Name = name.Trim();
        FormationPreset = formationPreset;
        Mentality = instructions.Mentality;
        Tempo = instructions.Tempo;
        Passing = instructions.Passing;
        Width = instructions.Width;
        Pressing = instructions.Pressing;
        DefensiveLine = instructions.DefensiveLine;
        Tackling = instructions.Tackling;
        TimeWasting = instructions.TimeWasting;

        Touch(now);
    }

    /// <summary>Promotes the plan to the club's default (`INS-11`).</summary>
    /// <param name="now">The current instant.</param>
    public void MakeDefault(DateTimeOffset now)
    {
        IsDefault = true;

        Touch(now);
    }

    /// <summary>Demotes the plan from the club's default.</summary>
    /// <param name="now">The current instant.</param>
    public void RemoveDefault(DateTimeOffset now)
    {
        IsDefault = false;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
