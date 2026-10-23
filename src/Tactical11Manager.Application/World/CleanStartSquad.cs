using System.Globalization;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.Squad.Generation;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Application.World;

/// <summary>
/// The squad a club returns to on a clean start: the tier's fair template squad, drawn afresh (`TKO-3`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the world seed, the club and the season, so the preview a manager reads before choosing and the squad the claim writes are
/// the same players' abilities: the preview draws it and throws it away, the claim draws it again and keeps it. Nothing here reads the clock or
/// the database. The ids are new each time, as a generated squad's always are, and they are the one thing that differs.
/// </para>
/// <para>
/// The seed is not the one the world's own squads were drawn from, so a club that starts clean does not get its founding squad back, and its names
/// start from an ordinal of its own so they do not echo the players it has just released.
/// </para>
/// </remarks>
public static class CleanStartSquad
{
    /// <summary>The label that keeps a clean-start squad's draws apart from the world's own.</summary>
    private const string Label = "clean-start";

    /// <summary>How many name ordinals a club's clean-start squads are spread over.</summary>
    private const int OrdinalSpan = 100_000;

    /// <summary>Draws the squad a club returns to.</summary>
    /// <param name="worldSeed">The world's generation seed (`PYR-14`).</param>
    /// <param name="country">The club's country.</param>
    /// <param name="club">The club.</param>
    /// <param name="tier">The tier the club plays in now, which sets the squad's template and so its strength and wages.</param>
    /// <param name="season">The season being played.</param>
    /// <param name="now">The current instant.</param>
    public static IReadOnlyList<GeneratedSquadMember> Draw(
        string worldSeed,
        Country country,
        Club club,
        int tier,
        Season season,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(season);

        var ordinal = (int)(DeterministicDigest.SeedOf(worldSeed, Label, club.Id.ToString("D")) % OrdinalSpan);

        return PlayerGenerator.GenerateTemplateSquad(
            new SquadGenerationRequest(
                string.Create(CultureInfo.InvariantCulture, $"{worldSeed}:{Label}:{season.SequenceNumber}"),
                country.NamePoolKey,
                country.Code,
                country.WorldId,
                club.Id,
                ordinal,
                tier,
                season.Id,
                season.SequenceNumber,
                season.GameYear,
                now));
    }

    /// <summary>Reads how strong a drawn squad is, on the scale the fans' target and the sponsor's realistic objective use.</summary>
    /// <param name="squad">The squad.</param>
    public static int StrengthOf(IReadOnlyList<GeneratedSquadMember> squad)
    {
        ArgumentNullException.ThrowIfNull(squad);

        return TeamStrength.OfSquad(
        [
            .. squad.Select(member => new AiSquadPlayer(
                member.Player.Id,
                member.Player.PrimaryPosition,
                member.Player.SecondaryPositions,
                member.Attributes.ToSet().Values,
                member.State.ConditionBp,
                IsAvailable: true)),
        ]);
    }
}
