using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Abstractions.Squad;

/// <summary>A club's saved selection and the entries that make it up, loaded together.</summary>
/// <param name="Sheet">The sheet.</param>
/// <param name="Entries">The sheet's entries, in slot order.</param>
public sealed record TeamSheetRecord(FixtureTeamSheet Sheet, IReadOnlyList<TeamSheetEntry> Entries);

/// <summary>
/// Persistence for the squad module's fixture team sheets (`SQ-4`, `CAL-3`).
/// </summary>
/// <remarks>
/// <para>
/// A staging port like <see cref="ITacticsRepository"/>: it stages rows in the current unit of work and
/// never saves, so a save that replaces a selection commits once. Because a selection is replaced
/// wholesale on every save, the port exposes the delete as well as the add: a swap of two players between
/// slots cannot be expressed as a sequence of in-place edits without passing through a state that
/// violates one of the sheet's unique indexes.
/// </para>
/// <para>
/// The sheet's entries are not a navigation property on <see cref="FixtureTeamSheet"/>, for the same
/// reason a plan's slots are not: the aggregate does not police its own selection — the validator does —
/// so a load returns the two together rather than the sheet reaching into a child collection.
/// </para>
/// </remarks>
public interface ITeamSheetRepository
{
    /// <summary>Stages a new sheet.</summary>
    /// <param name="sheet">The sheet.</param>
    void AddSheet(FixtureTeamSheet sheet);

    /// <summary>Stages a new entry.</summary>
    /// <param name="entry">The entry.</param>
    void AddEntry(TeamSheetEntry entry);

    /// <summary>Removes entries, which is how a replacement selection is written.</summary>
    /// <param name="entries">The entries to remove.</param>
    void RemoveEntries(IReadOnlyList<TeamSheetEntry> entries);

    /// <summary>Loads a club's sheet for a fixture, or null when none has been saved.</summary>
    /// <param name="fixtureId">The fixture.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TeamSheetRecord?> FindAsync(
        Guid fixtureId,
        Guid clubId,
        CancellationToken cancellationToken);
}
