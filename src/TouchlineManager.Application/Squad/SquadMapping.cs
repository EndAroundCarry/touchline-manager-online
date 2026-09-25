using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>
/// Maps squad read snapshots to their transport projections, and performs the conversions the API layer
/// must not do itself.
/// </summary>
/// <remarks>
/// <para>
/// One place, so a response shape cannot be assembled differently by two endpoints that both return a
/// player, and so no storage unit reaches a client: <see cref="ToUserFacingState"/> is the only
/// basis-point conversion in the product (`TRN-8`), and the hidden potential and reputation the player row
/// carries are simply never read here. That last property is what the architecture test over
/// <c>Contracts</c> backs up.
/// </para>
/// <para>
/// Age and remaining contract seasons are derived here rather than queried, because both are functions of
/// the game year and the current season number, and the game year is what makes a player age at rollover
/// rather than on a birthday (`TIME-3`, `CON-8`).
/// </para>
/// </remarks>
public static class SquadMapping
{
    /// <summary>One hundredth of a state measure, which is the step basis points convert to.</summary>
    private const int BasisPointsPerPoint = WorldRuleSet.StateBasisPointsMax / 100;

    /// <summary>Carries the access verdict over into a read outcome.</summary>
    /// <param name="outcome">The access verdict.</param>
    public static SquadReadOutcome ToReadOutcome(this ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.Granted => SquadReadOutcome.Found,
        ClubAccessOutcome.WorldNotSeeded => SquadReadOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => SquadReadOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => SquadReadOutcome.NoClub,
        ClubAccessOutcome.ClubNotManaged => SquadReadOutcome.ClubNotManaged,
        _ => SquadReadOutcome.ClubNotFound,
    };

    /// <summary>
    /// Converts a basis-point state measure to its user-facing value (`TRN-8`).
    /// </summary>
    /// <param name="basisPoints">The stored value, 0–10,000.</param>
    /// <returns>The value on the 0–100 scale the client displays.</returns>
    public static int ToUserFacingState(int basisPoints)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(basisPoints, WorldRuleSet.StateBasisPointsMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(basisPoints, WorldRuleSet.StateBasisPointsMax);

        return (int)Math.Round(
            basisPoints / (double)BasisPointsPerPoint,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Counts the seasons a contract still covers, from the season being played (`CON-8`).
    /// </summary>
    /// <param name="endSeasonNumber">The contract's last season.</param>
    /// <param name="currentSeasonNumber">The season in progress.</param>
    /// <returns>The remaining seasons, never negative: an expired contract has zero left, not a debt.</returns>
    public static int SeasonsRemaining(int endSeasonNumber, int currentSeasonNumber) =>
        Math.Max(0, endSeasonNumber - currentSeasonNumber + 1);

    /// <summary>Gets a player's age in a game year (`TIME-3`).</summary>
    /// <param name="birthGameYear">The game year the player was born in.</param>
    /// <param name="gameYear">The game year of the season being played.</param>
    public static int AgeIn(int birthGameYear, int gameYear) => gameYear - birthGameYear;

    /// <summary>Projects a player's state to its user-facing values.</summary>
    /// <param name="state">The stored state.</param>
    public static PlayerStateResponse ToResponse(this SquadStateRow state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new PlayerStateResponse(
            ToUserFacingState(state.ConditionBp),
            ToUserFacingState(state.FatigueBp),
            ToUserFacingState(state.MoraleBp),
            ToUserFacingState(state.MatchSharpnessBp));
    }

    /// <summary>Projects an unavailability record.</summary>
    /// <param name="availability">The stored record.</param>
    public static PlayerAvailabilityResponse ToResponse(this SquadAvailabilityRow availability)
    {
        ArgumentNullException.ThrowIfNull(availability);

        return new PlayerAvailabilityResponse(
            availability.Id,
            availability.Type.ToCode(),
            availability.Severity.ToCode(),
            availability.RemainingFixtures,
            availability.StartedAt);
    }

    /// <summary>Projects a contract as a squad row shows it.</summary>
    /// <param name="contract">The stored contract.</param>
    /// <param name="currentSeasonNumber">The season the remaining term is counted from.</param>
    public static PlayerContractSummaryResponse ToSummary(
        this SquadContractRow contract,
        int currentSeasonNumber)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return new PlayerContractSummaryResponse(
            contract.Id,
            contract.StartSeasonNumber,
            contract.EndSeasonNumber,
            SeasonsRemaining(contract.EndSeasonNumber, currentSeasonNumber),
            contract.WeeklyWageMinor,
            contract.SquadStatus.ToCode(),
            contract.Status.ToCode());
    }

    /// <summary>Projects a registration.</summary>
    /// <param name="registration">The stored registration.</param>
    public static PlayerRegistrationResponse ToResponse(this SquadRegistrationRow registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new PlayerRegistrationResponse(
            registration.Id,
            registration.ClubId,
            registration.Status.ToCode(),
            registration.EffectiveFixtureBoundaryRound);
    }

    /// <summary>Projects the attribute set into its four families.</summary>
    /// <param name="attributes">The stored attributes.</param>
    public static PlayerAttributesResponse ToResponse(this PlayerAttributeSet attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return new PlayerAttributesResponse(
            new TechnicalAttributesResponse(
                attributes.Finishing,
                attributes.Passing,
                attributes.Crossing,
                attributes.Dribbling,
                attributes.FirstTouch,
                attributes.Tackling,
                attributes.Marking,
                attributes.Heading,
                attributes.Technique,
                attributes.SetPieces),
            new MentalAttributesResponse(
                attributes.Decisions,
                attributes.Vision,
                attributes.Positioning,
                attributes.Composure,
                attributes.Anticipation,
                attributes.WorkRate,
                attributes.Aggression,
                attributes.Leadership),
            new PhysicalAttributesResponse(
                attributes.Pace,
                attributes.Acceleration,
                attributes.Stamina,
                attributes.Strength,
                attributes.Agility,
                attributes.JumpingReach),
            new GoalkeepingAttributesResponse(
                attributes.Handling,
                attributes.Reflexes,
                attributes.OneOnOnes,
                attributes.AerialAbility));
    }

    /// <summary>Projects a club's squad.</summary>
    /// <param name="squad">The stored squad.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static SquadResponse ToResponse(this SquadSnapshot squad, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(squad);

        var players = squad.Players
            .Select(player => ToResponse(player, squad.SeasonNumber, squad.GameYear))
            .ToList();

        var goalkeepers = squad.Players.Count(
            player => player.PrimaryPosition == PlayerPosition.Goalkeeper);
        var families = squad.Players.Select(player => PlayerPositions.FamilyOf(player.PrimaryPosition));

        return new SquadResponse(
            squad.ClubId,
            squad.ClubName,
            squad.ClubShortName,
            squad.CountryCode,
            squad.SeasonNumber,
            new SquadSummaryResponse(
                squad.Players.Count,
                goalkeepers,
                SquadLegality.MeetsMinimum(squad.Players.Count),
                SquadLegality.HasMinimumGoalkeepers(families),
                squad.Players.Sum(player => player.Contract.WeeklyWageMinor)),
            players,
            serverTime);
    }

    /// <summary>Projects one player's profile.</summary>
    /// <param name="player">The stored profile.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static PlayerResponse ToResponse(this PlayerSnapshot player, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(player);

        return new PlayerResponse(
            player.Id,
            player.ClubId,
            player.FullName,
            player.ShortName,
            player.NationalityCode,
            AgeIn(player.BirthGameYear, player.GameYear),
            player.BirthGameYear,
            player.PreferredFoot.ToCode(),
            player.HeightCm,
            player.WeightKg,
            player.PrimaryPosition.ToCode(),
            [.. player.SecondaryPositions.Select(position => position.ToCode())],
            player.Status.ToCode(),
            player.Attributes.ToResponse(),
            player.State.ToResponse(),
            player.Contract?.ToSummary(player.SeasonNumber),
            player.Registration?.ToResponse(),
            [.. player.Availability.Select(record => record.ToResponse())],
            serverTime);
    }

    /// <summary>Projects a club's contract list.</summary>
    /// <param name="contracts">The stored contracts.</param>
    /// <param name="serverTime">When the response was produced.</param>
    public static ContractsResponse ToResponse(this ContractsSnapshot contracts, DateTimeOffset serverTime)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return new ContractsResponse(
            contracts.ClubId,
            contracts.ClubName,
            contracts.SeasonNumber,
            contracts.Contracts.Sum(contract => contract.WeeklyWageMinor),
            [.. contracts.Contracts.Select(contract => ToResponse(contract, contracts.SeasonNumber, contracts.GameYear))],
            serverTime);
    }

    private static SquadPlayerResponse ToResponse(
        SquadPlayerRow player,
        int currentSeasonNumber,
        int gameYear) =>
        new(
            player.Id,
            player.FullName,
            player.ShortName,
            player.NationalityCode,
            AgeIn(player.BirthGameYear, gameYear),
            player.PreferredFoot.ToCode(),
            player.PrimaryPosition.ToCode(),
            [.. player.SecondaryPositions.Select(position => position.ToCode())],
            player.State.ToResponse(),
            player.Contract.ToSummary(currentSeasonNumber),
            [.. player.Availability.Select(record => record.ToResponse())]);

    private static PlayerContractResponse ToResponse(
        ContractRow contract,
        int currentSeasonNumber,
        int gameYear) =>
        new(
            contract.Id,
            contract.PlayerId,
            contract.PlayerName,
            contract.PlayerShortName,
            contract.PrimaryPosition.ToCode(),
            AgeIn(contract.BirthGameYear, gameYear),
            contract.StartSeasonNumber,
            contract.EndSeasonNumber,
            SeasonsRemaining(contract.EndSeasonNumber, currentSeasonNumber),
            contract.WeeklyWageMinor,
            contract.SquadStatus.ToCode(),
            contract.Status.ToCode());
}
