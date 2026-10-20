using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Fans;

/// <summary>
/// How strong the fans think a team is: the mean ability of its best eleven, in hundredths of an attribute point (`FAN-3`).
/// </summary>
/// <remarks>
/// <para>
/// An outfield player's ability is the mean of his twenty-four outfield attributes and a goalkeeper's is the mean of his four
/// goalkeeping ones, so a side whose every attribute is 13 is exactly 13.00 whatever its shape. The mean of all twenty-eight that the
/// AI policy and the snapshot builder use to separate two candidates would give a keeper about a fifth of his real worth, because
/// twenty-four of his twenty-eight attributes are the ones a keeper never uses.
/// </para>
/// <para>
/// The eleven is the one <see cref="AiClubPolicy.ChooseLineup"/> would field: the most suitable available player for each slot of the
/// shape, then the fittest, then the best. A manager's own sheet can differ, so the lock passes the sheet's starters to
/// <see cref="OfEleven"/> instead. Pure integer arithmetic, so the same squad always gives the same number.
/// </para>
/// </remarks>
public static class TeamStrength
{
    /// <summary>How many strength units make one attribute point.</summary>
    public const int PerAttributePoint = 100;

    /// <summary>The first goalkeeping attribute's index in the canonical order (handling, reflexes, one on ones, aerial ability).</summary>
    private const int FirstGoalkeepingAttribute = (int)AttributeName.Handling;

    /// <summary>The shape the fans picture a side in when nobody has said which it plays.</summary>
    private const FormationPreset DefaultShape = FormationPreset.FourFourTwo;

    /// <summary>One player's ability, in hundredths of an attribute point.</summary>
    /// <param name="attributes">The twenty-eight attributes, in canonical order.</param>
    /// <param name="goalkeeper">Whether he is judged as a goalkeeper.</param>
    /// <returns>The mean of the attributes his job uses, or 0 when there are not twenty-eight.</returns>
    public static int AbilityOf(IReadOnlyList<int> attributes, bool goalkeeper)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (attributes.Count != AttributeNames.Count)
        {
            return 0;
        }

        var from = goalkeeper ? FirstGoalkeepingAttribute : 0;
        var to = goalkeeper ? AttributeNames.Count : FirstGoalkeepingAttribute;
        var total = 0;

        for (var index = from; index < to; index++)
        {
            total += attributes[index];
        }

        return MeanHundredths(total, to - from);
    }

    /// <summary>The strength of an eleven already chosen: the mean of their abilities.</summary>
    /// <param name="abilities">Each player's <see cref="AbilityOf"/>.</param>
    /// <returns>The mean in hundredths of an attribute point, or 0 for an empty list.</returns>
    public static int OfEleven(IEnumerable<int> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        var total = 0;
        var count = 0;

        foreach (var ability in abilities)
        {
            total += ability;
            count++;
        }

        return count == 0 ? 0 : (total + (count / 2)) / count;
    }

    /// <summary>The strength of a squad in the default shape.</summary>
    /// <param name="squad">The squad. Unavailable players are left out.</param>
    public static int OfSquad(IReadOnlyList<AiSquadPlayer> squad) =>
        OfSquad(squad, FormationLayouts.DefaultSlots(DefaultShape));

    /// <summary>The strength of a squad in a given shape.</summary>
    /// <param name="squad">The squad. Unavailable players are left out.</param>
    /// <param name="layout">The eleven slots of the shape the side plays.</param>
    /// <returns>
    /// The mean ability of the eleven the AI ordering would field. A squad that cannot fill the shape is read as its best available
    /// goalkeeper and its ten best outfield players, or as fewer when there are fewer; an empty squad is 0.
    /// </returns>
    public static int OfSquad(IReadOnlyList<AiSquadPlayer> squad, IReadOnlyList<FormationSlot> layout)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(layout);

        var byId = squad.ToDictionary(player => player.PlayerId);
        var lineup = AiClubPolicy.ChooseLineup(layout, squad);

        if (lineup.Count == layout.Count)
        {
            return OfEleven(layout.Select(slot =>
            {
                var player = byId[lineup[slot.SlotNumber]];

                return AbilityOf(player.Attributes, slot.PositionFamily == PositionFamily.Goalkeeper);
            }));
        }

        var available = squad.Where(player => player.IsAvailable).ToList();
        var keeper = available
            .Where(player => player.PrimaryPosition == PlayerPosition.Goalkeeper)
            .Select(player => (Player: player, Ability: AbilityOf(player.Attributes, true)))
            .OrderByDescending(entry => entry.Ability)
            .ThenBy(entry => entry.Player.PlayerId)
            .Take(1)
            .Select(entry => entry.Ability);
        var outfield = available
            .Where(player => player.PrimaryPosition != PlayerPosition.Goalkeeper)
            .Select(player => (Player: player, Ability: AbilityOf(player.Attributes, false)))
            .OrderByDescending(entry => entry.Ability)
            .ThenBy(entry => entry.Player.PlayerId)
            .Take(layout.Count - 1)
            .Select(entry => entry.Ability);

        return OfEleven(keeper.Concat(outfield));
    }

    private static int MeanHundredths(int total, int count) => ((total * PerAttributePoint) + (count / 2)) / count;
}
