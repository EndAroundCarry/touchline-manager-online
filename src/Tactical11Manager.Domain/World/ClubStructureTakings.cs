namespace Tactical11Manager.Domain.World;

/// <summary>What a takings row records (game rules §13.7).</summary>
public enum StructureTakingsKind
{
    /// <summary>The snack bar's takings for one home match (`STRC-4`).</summary>
    MatchSale = 0,

    /// <summary>The merchandise shop's takings for one week (`STRC-6`).</summary>
    WeekSale = 1,

    /// <summary>One building's upkeep for one week (`STRC-8`).</summary>
    Upkeep = 2,
}

/// <summary>Stable codes and parsing for <see cref="StructureTakingsKind"/>.</summary>
public static class StructureTakingsKinds
{
    /// <summary>Converts a row kind to its stable code.</summary>
    /// <param name="kind">The row kind.</param>
    public static string ToCode(this StructureTakingsKind kind) => kind switch
    {
        StructureTakingsKind.MatchSale => "match_sale",
        StructureTakingsKind.WeekSale => "week_sale",
        StructureTakingsKind.Upkeep => "upkeep",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown structure takings kind."),
    };

    /// <summary>Parses a stable code back to its row kind.</summary>
    /// <param name="code">The stable code.</param>
    public static StructureTakingsKind FromCode(string code) => code switch
    {
        "match_sale" => StructureTakingsKind.MatchSale,
        "week_sale" => StructureTakingsKind.WeekSale,
        "upkeep" => StructureTakingsKind.Upkeep,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown structure takings kind code."),
    };
}

/// <summary>
/// One line of a building's sales and upkeep history, append-only (game rules §13.7, `STRC-4`, `STRC-6`,
/// `STRC-8`).
/// </summary>
/// <remarks>
/// The ledger alone cannot answer "what did the snack bar earn and cost", because a week's upkeep for both
/// buildings is one combined ledger entry (the ledger's unique index allows one entry per category per
/// correlation key). These rows are the per-building breakdown the Structures page reads, and each keeps the
/// customers, the base, the bonus and the reason, so a figure can still be explained after the rules are
/// retuned.
/// </remarks>
public sealed class ClubStructureTakings
{
    /// <summary>The reason code stored on a row that has no bonus, such as upkeep.</summary>
    public const string NoBonusReason = "none";

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubStructureTakings()
    {
    }

    /// <summary>Gets the row identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club whose building it is.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets which building.</summary>
    public StructureKind Kind { get; private set; }

    /// <summary>Gets what the row records.</summary>
    public StructureTakingsKind RowKind { get; private set; }

    /// <summary>Gets the fixture a match sale belongs to, or null for a weekly row.</summary>
    public Guid? FixtureId { get; private set; }

    /// <summary>Gets the first day of the week the row belongs to, the same week the weekly run settles.</summary>
    public DateOnly WeekOf { get; private set; }

    /// <summary>Gets the building's level when the row was written.</summary>
    public int Level { get; private set; }

    /// <summary>Gets the spectators (a match sale) or fans (a week sale); zero for upkeep.</summary>
    public int Customers { get; private set; }

    /// <summary>Gets the takings before the bonus, or the upkeep, in minor units (never negative).</summary>
    public long BaseMinor { get; private set; }

    /// <summary>Gets the bonus applied, in basis points of the base; zero for upkeep.</summary>
    public int BonusBp { get; private set; }

    /// <summary>Gets the stable code for why the bonus is what it is.</summary>
    public string BonusReason { get; private set; } = NoBonusReason;

    /// <summary>Gets the money the row moved, in minor units: takings are positive, upkeep negative.</summary>
    public long AmountMinor { get; private set; }

    /// <summary>Gets the ledger entry that moved the money. Both buildings' upkeep rows share one.</summary>
    public Guid LedgerEntryId { get; private set; }

    /// <summary>Gets when the row was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records the snack bar's takings for one home match (`STRC-4`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The home club.</param>
    /// <param name="fixtureId">The match.</param>
    /// <param name="weekOf">The first day of the match's week.</param>
    /// <param name="level">The snack bar's level.</param>
    /// <param name="spectators">The crowd.</param>
    /// <param name="baseMinor">Spectators times spend, before the bonus.</param>
    /// <param name="bonusBp">The result bonus.</param>
    /// <param name="bonusReason">The result bonus's reason code.</param>
    /// <param name="amountMinor">The takings.</param>
    /// <param name="ledgerEntryId">The ledger entry that booked it.</param>
    /// <param name="now">The current instant.</param>
    public static ClubStructureTakings MatchSale(
        Guid id,
        Guid clubId,
        Guid fixtureId,
        DateOnly weekOf,
        int level,
        int spectators,
        long baseMinor,
        int bonusBp,
        string bonusReason,
        long amountMinor,
        Guid ledgerEntryId,
        DateTimeOffset now) =>
        Create(
            id, clubId, StructureKind.SnackBar, StructureTakingsKind.MatchSale, fixtureId, weekOf, level, spectators,
            baseMinor, bonusBp, bonusReason, amountMinor, ledgerEntryId, now);

    /// <summary>Records the merchandise shop's takings for one week (`STRC-6`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="weekOf">The first day of the week.</param>
    /// <param name="level">The shop's level.</param>
    /// <param name="fans">The club's fans.</param>
    /// <param name="baseMinor">Fans times spend, before the bonus.</param>
    /// <param name="bonusBp">The mood bonus.</param>
    /// <param name="bonusReason">The mood word's code.</param>
    /// <param name="amountMinor">The takings.</param>
    /// <param name="ledgerEntryId">The ledger entry that booked it.</param>
    /// <param name="now">The current instant.</param>
    public static ClubStructureTakings WeekSale(
        Guid id,
        Guid clubId,
        DateOnly weekOf,
        int level,
        int fans,
        long baseMinor,
        int bonusBp,
        string bonusReason,
        long amountMinor,
        Guid ledgerEntryId,
        DateTimeOffset now) =>
        Create(
            id, clubId, StructureKind.Merchandise, StructureTakingsKind.WeekSale, null, weekOf, level, fans,
            baseMinor, bonusBp, bonusReason, amountMinor, ledgerEntryId, now);

    /// <summary>Records one building's upkeep for one week (`STRC-8`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="kind">Which building.</param>
    /// <param name="weekOf">The first day of the week.</param>
    /// <param name="level">The building's level.</param>
    /// <param name="upkeepMinor">The upkeep, as a positive amount; the row stores the money as a cost.</param>
    /// <param name="ledgerEntryId">The combined ledger entry that booked both buildings' upkeep.</param>
    /// <param name="now">The current instant.</param>
    public static ClubStructureTakings Upkeep(
        Guid id,
        Guid clubId,
        StructureKind kind,
        DateOnly weekOf,
        int level,
        long upkeepMinor,
        Guid ledgerEntryId,
        DateTimeOffset now) =>
        Create(
            id, clubId, kind, StructureTakingsKind.Upkeep, null, weekOf, level, 0,
            upkeepMinor, 0, NoBonusReason, -upkeepMinor, ledgerEntryId, now);

    private static ClubStructureTakings Create(
        Guid id,
        Guid clubId,
        StructureKind kind,
        StructureTakingsKind rowKind,
        Guid? fixtureId,
        DateOnly weekOf,
        int level,
        int customers,
        long baseMinor,
        int bonusBp,
        string bonusReason,
        long amountMinor,
        Guid ledgerEntryId,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(customers);
        ArgumentOutOfRangeException.ThrowIfNegative(baseMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(bonusBp);
        ArgumentException.ThrowIfNullOrWhiteSpace(bonusReason);

        return new ClubStructureTakings
        {
            Id = id,
            ClubId = clubId,
            Kind = kind,
            RowKind = rowKind,
            FixtureId = fixtureId,
            WeekOf = weekOf,
            Level = level,
            Customers = customers,
            BaseMinor = baseMinor,
            BonusBp = bonusBp,
            BonusReason = bonusReason,
            AmountMinor = amountMinor,
            LedgerEntryId = ledgerEntryId,
            CreatedAt = now,
        };
    }
}
