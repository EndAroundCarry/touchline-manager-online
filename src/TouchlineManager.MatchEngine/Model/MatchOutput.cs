namespace TouchlineManager.MatchEngine.Model;

/// <summary>
/// One side's match statistics.
/// </summary>
/// <remarks>
/// Every count here is derived from the event sequence rather than accumulated separately, so the
/// reconciliation rule holds by construction: the score equals the goal events, and each statistic equals
/// the events of its type (MAT-5, master plan §8.5). Accumulating in two places is how a scoreboard and a
/// stats panel come to disagree after a refactor.
/// </remarks>
public sealed record MatchStatisticsV1
{
    /// <summary>Gets the share of possession, in basis points.</summary>
    public required int PossessionBasisPoints { get; init; }

    /// <summary>Gets goals scored.</summary>
    public required int Goals { get; init; }

    /// <summary>Gets shots taken, which is every shot event whatever its outcome.</summary>
    public required int Shots { get; init; }

    /// <summary>Gets shots on target: goals plus saves.</summary>
    public required int ShotsOnTarget { get; init; }

    /// <summary>Gets shots that missed the target.</summary>
    public required int ShotsOffTarget { get; init; }

    /// <summary>Gets shots blocked by a defender.</summary>
    public required int ShotsBlocked { get; init; }

    /// <summary>Gets shots that hit the woodwork.</summary>
    public required int WoodworkHits { get; init; }

    /// <summary>Gets saves made by the goalkeeper.</summary>
    public required int Saves { get; init; }

    /// <summary>Gets corners won.</summary>
    public required int Corners { get; init; }

    /// <summary>Gets offsides given against the side.</summary>
    public required int Offsides { get; init; }

    /// <summary>Gets fouls committed.</summary>
    public required int Fouls { get; init; }

    /// <summary>Gets yellow cards shown.</summary>
    public required int YellowCards { get; init; }

    /// <summary>Gets red cards shown, counting a second yellow.</summary>
    public required int RedCards { get; init; }

    /// <summary>Gets penalties awarded to the side.</summary>
    public required int PenaltiesAwarded { get; init; }

    /// <summary>Gets penalties scored by the side.</summary>
    public required int PenaltiesScored { get; init; }

    /// <summary>Gets injuries suffered by the side.</summary>
    public required int Injuries { get; init; }

    /// <summary>Gets substitutions made by the side.</summary>
    public required int Substitutions { get; init; }
}

/// <summary>
/// One player's participation in the match.
/// </summary>
/// <remarks>
/// The record of who played and for how long. Contract renewal's inputs include playing time (`CON-3`),
/// and the discipline and injury stages apply suspensions and absences from these lines rather than from
/// re-reading the event stream, so the line is part of the output contract rather than a convenience.
/// </remarks>
public sealed record MatchPlayerLineV1
{
    /// <summary>Gets the participant.</summary>
    public required Guid ParticipantId { get; init; }

    /// <summary>Gets the club.</summary>
    public required Guid ClubId { get; init; }

    /// <summary>Gets which side the player played for.</summary>
    public required MatchSide Side { get; init; }

    /// <summary>Gets whether the player was in the eleven rather than on the bench.</summary>
    public required bool Started { get; init; }

    /// <summary>Gets how many minutes the player was on the pitch.</summary>
    public required int MinutesPlayed { get; init; }

    /// <summary>Gets goals scored.</summary>
    public required int Goals { get; init; }

    /// <summary>Gets yellow cards received.</summary>
    public required int YellowCards { get; init; }

    /// <summary>Gets whether the player was sent off.</summary>
    public required bool SentOff { get; init; }

    /// <summary>Gets how many fixtures an injury rules the player out for, or zero when uninjured.</summary>
    public required int AbsenceFixtures { get; init; }
}

/// <summary>
/// The complete, immutable result of simulating one match.
/// </summary>
/// <remarks>
/// <para>
/// Carries both hashes. The input hash pins the exact snapshot simulated; the output hash pins the exact
/// result, so re-running the same snapshot, seed, and engine version reproduces it byte for byte (MAT-9).
/// A disputed result is re-derived rather than argued about, and an engine defect is found by a golden hash
/// failing rather than by a manager noticing.
/// </para>
/// <para>
/// Nothing here reveals a hidden value: the output carries no potential, no reputation, and no raw seed
/// material (MAT-11).
/// </para>
/// </remarks>
public sealed record MatchResultV1
{
    /// <summary>Gets the engine version that produced the result.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>Gets the rules version the result was produced under.</summary>
    public required string RuleSetVersion { get; init; }

    /// <summary>Gets the home side's goals.</summary>
    public required int HomeGoals { get; init; }

    /// <summary>Gets the away side's goals.</summary>
    public required int AwayGoals { get; init; }

    /// <summary>Gets the home side's statistics.</summary>
    public required MatchStatisticsV1 Home { get; init; }

    /// <summary>Gets the away side's statistics.</summary>
    public required MatchStatisticsV1 Away { get; init; }

    /// <summary>Gets every event, in sequence order.</summary>
    public required IReadOnlyList<EngineEventV1> Events { get; init; }

    /// <summary>Gets every participant's line, ordered by club and then participant identity.</summary>
    public required IReadOnlyList<MatchPlayerLineV1> PlayerLines { get; init; }

    /// <summary>Gets how many minutes were played, regulation plus stoppage.</summary>
    public required int TotalMinutesPlayed { get; init; }

    /// <summary>Gets the hash of the input snapshot this result was produced from.</summary>
    public required string InputHash { get; init; }

    /// <summary>Gets the hash of this result.</summary>
    public required string OutputHash { get; init; }

    /// <summary>Gets one side's statistics.</summary>
    /// <param name="side">Which side.</param>
    public MatchStatisticsV1 StatsOf(MatchSide side) => side == MatchSide.Home ? Home : Away;

    /// <summary>Gets one side's goals.</summary>
    /// <param name="side">Which side.</param>
    public int GoalsOf(MatchSide side) => side == MatchSide.Home ? HomeGoals : AwayGoals;
}
