using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Who stands where in the eleven, as a flat run of twenty-two entities (`replay-v4`).
/// </summary>
/// <remarks>
/// Entity <c>0…10</c> are the home side's slots 1…11 and <c>11…21</c> the away side's, so a slot keeps its
/// entity — and its identifier in the presentation — however many substitutions change who occupies it. A slot
/// whose player has gone off and not been replaced holds <see cref="Guid.Empty"/>.
/// </remarks>
internal sealed class FilmRoster
{
    /// <summary>How many entities a roster has.</summary>
    public const int Size = 22;

    private readonly Dictionary<Guid, int> _byParticipant = [];

    /// <summary>Initializes a roster from its occupants.</summary>
    /// <param name="occupants">The occupant of each entity, or <see cref="Guid.Empty"/>.</param>
    public FilmRoster(Guid[] occupants)
    {
        Occupants = occupants;

        for (var entity = 0; entity < Size; entity++)
        {
            if (occupants[entity] != Guid.Empty)
            {
                _byParticipant[occupants[entity]] = entity;
            }
        }
    }

    /// <summary>Gets the occupant of each entity.</summary>
    public Guid[] Occupants { get; }

    /// <summary>Gets the entity index of a side's slot.</summary>
    /// <param name="side">The side.</param>
    /// <param name="slotNumber">The slot, 1…11.</param>
    public static int Index(MatchSide side, int slotNumber) => (side == MatchSide.Home ? 0 : 11) + slotNumber - 1;

    /// <summary>Gets the side an entity belongs to.</summary>
    /// <param name="entity">The entity.</param>
    public static MatchSide SideOf(int entity) => entity < 11 ? MatchSide.Home : MatchSide.Away;

    /// <summary>Gets the slot number an entity stands in, 1…11.</summary>
    /// <param name="entity">The entity.</param>
    public static int SlotOf(int entity) => (entity % 11) + 1;

    /// <summary>Gets the identifier the presentation gives an entity.</summary>
    /// <param name="entity">The entity.</param>
    public static string EntityId(int entity) => $"{(entity < 11 ? "H" : "A")}{SlotOf(entity)}";

    /// <summary>Gets the entity a participant occupies, or -1 when they are not on the pitch.</summary>
    /// <param name="participant">The participant.</param>
    public int EntityOf(Guid participant) => _byParticipant.TryGetValue(participant, out var entity) ? entity : -1;

    /// <summary>Gets whether anyone occupies an entity.</summary>
    /// <param name="entity">The entity.</param>
    public bool IsOccupied(int entity) => Occupants[entity] != Guid.Empty;

    /// <summary>Returns the roster with a player replaced or removed.</summary>
    /// <param name="off">The player leaving.</param>
    /// <param name="on">The player taking their slot, or null when the side plays a player short.</param>
    public FilmRoster With(Guid off, Guid? on)
    {
        var entity = EntityOf(off);

        if (entity < 0)
        {
            return this;
        }

        var next = (Guid[])Occupants.Clone();

        next[entity] = on ?? Guid.Empty;

        return new FilmRoster(next);
    }
}

/// <summary>
/// What every stage of the film reads and none changes (`replay-v4`).
/// </summary>
internal sealed class FilmContext
{
    private readonly Dictionary<Guid, MatchParticipantV1> _participants = [];
    private readonly Dictionary<Guid, MatchSide> _sides = [];

    /// <summary>Initializes the context.</summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="options">The film's constants.</param>
    public FilmContext(MatchInputV1 input, MatchResultV1 result, EngineRulesV2 rules, HighlightOptionsV1 options)
    {
        Input = input;
        Result = result;
        Rules = rules;
        Options = options;
        EventsBySequence = result.Events.ToDictionary(matchEvent => matchEvent.Sequence);

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            foreach (var participant in input.SideOf(side).Squad)
            {
                _participants[participant.ParticipantId] = participant;
                _sides[participant.ParticipantId] = side;
            }
        }

        var slots = new MatchSlotV1[FilmRoster.Size];
        var starters = new Guid[FilmRoster.Size];

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            foreach (var slot in input.SideOf(side).Slots)
            {
                var entity = FilmRoster.Index(side, slot.SlotNumber);

                slots[entity] = slot;
                starters[entity] = slot.ParticipantId;
            }
        }

        Slots = slots;
        Starters = new FilmRoster(starters);
    }

    /// <summary>Gets the frozen snapshot.</summary>
    public MatchInputV1 Input { get; }

    /// <summary>Gets the simulated result.</summary>
    public MatchResultV1 Result { get; }

    /// <summary>Gets the rules in force.</summary>
    public EngineRulesV2 Rules { get; }

    /// <summary>Gets the film's constants.</summary>
    public HighlightOptionsV1 Options { get; }

    /// <summary>Gets the events by sequence number.</summary>
    public Dictionary<int, EngineEventV1> EventsBySequence { get; }

    /// <summary>Gets the slot each entity stands in; a slot's definition does not change with its occupant.</summary>
    public MatchSlotV1[] Slots { get; }

    /// <summary>Gets the roster the match starts with.</summary>
    public FilmRoster Starters { get; }

    /// <summary>Gets a participant, or null for one the snapshot does not name.</summary>
    /// <param name="participant">The participant.</param>
    public MatchParticipantV1? ParticipantOf(Guid participant) =>
        _participants.TryGetValue(participant, out var found) ? found : null;

    /// <summary>Gets the side a participant plays for, or null for one the snapshot does not name.</summary>
    /// <param name="participant">The participant.</param>
    public MatchSide? SideOf(Guid participant) => _sides.TryGetValue(participant, out var side) ? side : null;

    /// <summary>Gets one of a participant's attributes, or a middling value for one the snapshot does not name.</summary>
    /// <param name="participant">The participant.</param>
    /// <param name="attribute">The attribute.</param>
    public int AttributeOf(Guid participant, MatchAttributeName attribute) =>
        ParticipantOf(participant)?.Attributes.ValueOf(attribute) ?? 10;

    /// <summary>Gets a side's instructions.</summary>
    /// <param name="side">The side.</param>
    public MatchInstructionsV1 InstructionsOf(MatchSide side) => Input.SideOf(side).Instructions;
}
