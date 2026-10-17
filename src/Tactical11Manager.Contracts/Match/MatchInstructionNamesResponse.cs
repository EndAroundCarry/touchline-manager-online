namespace Tactical11Manager.Contracts.Match;

/// <summary>
/// The names a manager gave the match instructions their club played with in one match, which the match centre
/// shows beside the changes those instructions made.
/// </summary>
/// <remarks>
/// The replay is public and immutable, so it names a rule only by its place in the side's list. A rule's name is
/// the manager's own, so it comes from this read instead, which answers for the caller's club and for nobody
/// else's: a caller whose club did not play in the match, or who holds none, gets an empty list.
/// </remarks>
/// <param name="Side">Which end the caller's club played, <c>home</c> or <c>away</c>, or null when it did not play.</param>
/// <param name="Rules">The club's named rules, in the order the manager listed them.</param>
public sealed record MatchInstructionNamesResponse(
    string? Side,
    IReadOnlyList<MatchInstructionNameResponse> Rules);

/// <summary>One match instruction's place and name.</summary>
/// <param name="Ordinal">The rule's 1-based place in the side's list: what a tactical change in the replay carries.</param>
/// <param name="Name">The name the manager gave it.</param>
public sealed record MatchInstructionNameResponse(int Ordinal, string Name);
