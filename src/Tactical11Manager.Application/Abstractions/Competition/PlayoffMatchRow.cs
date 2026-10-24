using Tactical11Manager.Domain.Competition;

namespace Tactical11Manager.Application.Abstractions.Competition;

/// <summary>One kick of a stored play-off shoot-out, with the players named (`PR-6`).</summary>
/// <param name="Number">The kick's place in the shoot-out, from 1.</param>
/// <param name="Round">The round, from 1.</param>
/// <param name="Side">The side that took it, <c>home</c> or <c>away</c>.</param>
/// <param name="TakerName">The player who took it.</param>
/// <param name="GoalkeeperName">The goalkeeper who faced it, or null when the other side had none on the pitch.</param>
/// <param name="Outcome">How it ended: <c>scored</c>, <c>saved</c> or <c>wide</c>.</param>
public sealed record PlayoffKickRow(
    int Number,
    int Round,
    string Side,
    string TakerName,
    string? GoalkeeperName,
    string Outcome);

/// <summary>How the promotion play-off tie a fixture belongs to was settled (`PR-6`).</summary>
/// <param name="Stage">Which match of the bracket the fixture is.</param>
/// <param name="DecidedBy">How the tie was settled, or null while it is undecided.</param>
/// <param name="WinnerClubId">The club that goes through, or null while it is undecided.</param>
/// <param name="ShootoutHomeScore">The home side's shoot-out goals, when the tie went to penalties.</param>
/// <param name="ShootoutAwayScore">The away side's shoot-out goals, when the tie went to penalties.</param>
/// <param name="FirstSide">The side that kicked first, when the tie went to penalties.</param>
/// <param name="Kicks">The shoot-out's kicks in order; empty unless the tie went to penalties.</param>
public sealed record PlayoffMatchRow(
    PlayoffStage Stage,
    PlayoffDecision? DecidedBy,
    Guid? WinnerClubId,
    int? ShootoutHomeScore,
    int? ShootoutAwayScore,
    string? FirstSide,
    IReadOnlyList<PlayoffKickRow> Kicks);
