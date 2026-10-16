namespace Tactical11Manager.Domain.Squad;

/// <summary>
/// The positions a role naturally asks for, so a player can be judged against the job a slot names and
/// not only against its band (`INS-10`, `TAC-8`).
/// </summary>
/// <remarks>
/// <para>
/// A band is too coarse for the wide roles. A winger standing in a wide-midfielder slot, or a full back
/// at wing back, is a sensible choice that the band alone would flag, so the fit checks ask two questions:
/// is the player at home in the slot's <see cref="PositionFamily"/>, or is one of his positions on the
/// role's own list.
/// </para>
/// <para>
/// The match engine keeps its own copy (<c>MatchLineup.NaturalPositionsOf</c>), because the engine may not
/// depend on the domain; a test asserts the two lists agree.
/// </para>
/// </remarks>
public static class RolePositions
{
    /// <summary>Gets the positions a role naturally asks for.</summary>
    /// <param name="role">The role.</param>
    public static IReadOnlyList<PlayerPosition> NaturalFor(PlayerRole role) => role switch
    {
        PlayerRole.Goalkeeper => [PlayerPosition.Goalkeeper],
        PlayerRole.CentreBack => [PlayerPosition.CentreBack],
        PlayerRole.FullBack => [PlayerPosition.RightBack, PlayerPosition.LeftBack],
        PlayerRole.WingBack =>
        [
            PlayerPosition.RightBack,
            PlayerPosition.LeftBack,
            PlayerPosition.RightMidfielder,
            PlayerPosition.LeftMidfielder,
            PlayerPosition.RightWinger,
            PlayerPosition.LeftWinger,
        ],
        PlayerRole.DefensiveMidfielder => [PlayerPosition.DefensiveMidfielder, PlayerPosition.CentralMidfielder],
        PlayerRole.CentralMidfielder =>
            [PlayerPosition.CentralMidfielder, PlayerPosition.DefensiveMidfielder, PlayerPosition.AttackingMidfielder],
        PlayerRole.AttackingMidfielder => [PlayerPosition.AttackingMidfielder, PlayerPosition.CentralMidfielder],
        PlayerRole.WideMidfielder =>
        [
            PlayerPosition.RightMidfielder,
            PlayerPosition.LeftMidfielder,
            PlayerPosition.RightWinger,
            PlayerPosition.LeftWinger,
            PlayerPosition.RightBack,
            PlayerPosition.LeftBack,
        ],
        PlayerRole.Winger =>
        [
            PlayerPosition.RightWinger,
            PlayerPosition.LeftWinger,
            PlayerPosition.RightMidfielder,
            PlayerPosition.LeftMidfielder,
            PlayerPosition.AttackingMidfielder,
        ],
        PlayerRole.Striker => [PlayerPosition.Striker, PlayerPosition.AttackingMidfielder],
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    /// <summary>
    /// Whether a player is at home in a slot: one of his positions is in the slot's band or on the role's
    /// natural list (`INS-10`).
    /// </summary>
    /// <param name="primary">The player's primary position.</param>
    /// <param name="secondary">The player's other positions.</param>
    /// <param name="family">The slot's band.</param>
    /// <param name="role">The slot's role.</param>
    public static bool IsAtHome(
        PlayerPosition primary,
        IReadOnlyList<PlayerPosition> secondary,
        PositionFamily family,
        PlayerRole role)
    {
        ArgumentNullException.ThrowIfNull(secondary);

        var natural = NaturalFor(role);

        return IsAtHome(primary) || secondary.Any(IsAtHome);

        bool IsAtHome(PlayerPosition position) =>
            PlayerPositions.FamilyOf(position) == family || natural.Contains(position);
    }

    /// <summary>
    /// How well a player suits a slot, used to choose between candidates: 3 for a player whose primary
    /// position is natural for the role and in the slot's band, 2 for one in the band, 1 for one who only
    /// covers it (a secondary position, or a natural position of another band), 0 for neither.
    /// </summary>
    /// <param name="primary">The player's primary position.</param>
    /// <param name="secondary">The player's other positions.</param>
    /// <param name="family">The slot's band.</param>
    /// <param name="role">The slot's role.</param>
    public static int Suitability(
        PlayerPosition primary,
        IReadOnlyList<PlayerPosition> secondary,
        PositionFamily family,
        PlayerRole role)
    {
        ArgumentNullException.ThrowIfNull(secondary);

        var natural = NaturalFor(role);

        if (PlayerPositions.FamilyOf(primary) == family)
        {
            return natural.Contains(primary) ? 3 : 2;
        }

        return natural.Contains(primary)
            || secondary.Any(position => PlayerPositions.FamilyOf(position) == family || natural.Contains(position))
                ? 1
                : 0;
    }
}
