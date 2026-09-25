using TouchlineManager.Domain.Squad;
using TouchlineManager.MatchEngine.Model;
using StoredEventType = TouchlineManager.Domain.Match.MatchEventType;
using StoredShotZone = TouchlineManager.Domain.Match.MatchShotZone;
using StoredSubstitutionCause = TouchlineManager.Domain.Match.MatchSubstitutionCause;

namespace TouchlineManager.Application.Match;

/// <summary>
/// Maps the domain's vocabulary onto the engine's (`MAT-1`, DEP-2).
/// </summary>
/// <remarks>
/// <para>
/// The engine may not depend on the domain, so the two define the same words separately and the
/// application layer is the only place that knows both. The mappings are written out member by member
/// rather than cast, even though the numeric values agree today: a cast would keep compiling if one side
/// reordered its members, and the result would be a snapshot that simulated correctly and meant something
/// else. A switch expression with a throwing arm turns that into a compile-time or runtime refusal.
/// </para>
/// <para>
/// A test asserts each mapping agrees with the engine's own value, so the deliberate duplication is
/// checked rather than trusted.
/// </para>
/// </remarks>
public static class EngineVocabulary
{
    /// <summary>Maps a domain position to the engine's.</summary>
    /// <param name="position">The position.</param>
    public static MatchPosition Position(PlayerPosition position) => position switch
    {
        PlayerPosition.Goalkeeper => MatchPosition.Goalkeeper,
        PlayerPosition.RightBack => MatchPosition.RightBack,
        PlayerPosition.CentreBack => MatchPosition.CentreBack,
        PlayerPosition.LeftBack => MatchPosition.LeftBack,
        PlayerPosition.DefensiveMidfielder => MatchPosition.DefensiveMidfielder,
        PlayerPosition.CentralMidfielder => MatchPosition.CentralMidfielder,
        PlayerPosition.AttackingMidfielder => MatchPosition.AttackingMidfielder,
        PlayerPosition.RightWinger => MatchPosition.RightWinger,
        PlayerPosition.LeftWinger => MatchPosition.LeftWinger,
        PlayerPosition.Striker => MatchPosition.Striker,
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position."),
    };

    /// <summary>Maps a domain position family to the engine's.</summary>
    /// <param name="family">The family.</param>
    public static MatchPositionFamily Family(PositionFamily family) => family switch
    {
        PositionFamily.Goalkeeper => MatchPositionFamily.Goalkeeper,
        PositionFamily.Defence => MatchPositionFamily.Defence,
        PositionFamily.Midfield => MatchPositionFamily.Midfield,
        PositionFamily.Attack => MatchPositionFamily.Attack,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown position family."),
    };

    /// <summary>Maps a domain role to the engine's.</summary>
    /// <param name="role">The role.</param>
    public static MatchRole Role(PlayerRole role) => role switch
    {
        PlayerRole.Goalkeeper => MatchRole.Goalkeeper,
        PlayerRole.CentreBack => MatchRole.CentreBack,
        PlayerRole.FullBack => MatchRole.FullBack,
        PlayerRole.WingBack => MatchRole.WingBack,
        PlayerRole.DefensiveMidfielder => MatchRole.DefensiveMidfielder,
        PlayerRole.CentralMidfielder => MatchRole.CentralMidfielder,
        PlayerRole.AttackingMidfielder => MatchRole.AttackingMidfielder,
        PlayerRole.Winger => MatchRole.Winger,
        PlayerRole.Striker => MatchRole.Striker,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    /// <summary>Maps the eight team instructions to the engine's.</summary>
    /// <param name="instructions">The instructions.</param>
    public static MatchInstructionsV1 Instructions(TeamInstructionSet instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        return new MatchInstructionsV1
        {
            Mentality = instructions.Mentality switch
            {
                Mentality.Defensive => MatchMentality.Defensive,
                Mentality.Cautious => MatchMentality.Cautious,
                Mentality.Balanced => MatchMentality.Balanced,
                Mentality.Positive => MatchMentality.Positive,
                Mentality.Attacking => MatchMentality.Attacking,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.Mentality, "Unknown mentality."),
            },
            Tempo = instructions.Tempo switch
            {
                Tempo.Low => MatchTempo.Low,
                Tempo.Normal => MatchTempo.Normal,
                Tempo.High => MatchTempo.High,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.Tempo, "Unknown tempo."),
            },
            Passing = instructions.Passing switch
            {
                PassingStyle.ShortPassing => MatchPassingStyle.ShortPassing,
                PassingStyle.MixedPassing => MatchPassingStyle.MixedPassing,
                PassingStyle.DirectPassing => MatchPassingStyle.DirectPassing,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.Passing, "Unknown passing style."),
            },
            Width = instructions.Width switch
            {
                Width.Narrow => MatchWidth.Narrow,
                Width.Normal => MatchWidth.Normal,
                Width.Wide => MatchWidth.Wide,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.Width, "Unknown width."),
            },
            Pressing = instructions.Pressing switch
            {
                Pressing.LowBlock => MatchPressing.LowBlock,
                Pressing.MidBlock => MatchPressing.MidBlock,
                Pressing.HighPress => MatchPressing.HighPress,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.Pressing, "Unknown pressing scheme."),
            },
            DefensiveLine = instructions.DefensiveLine switch
            {
                DefensiveLine.Deep => MatchDefensiveLine.Deep,
                DefensiveLine.Normal => MatchDefensiveLine.Normal,
                DefensiveLine.High => MatchDefensiveLine.High,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.DefensiveLine, "Unknown defensive line."),
            },
            Tackling = instructions.Tackling switch
            {
                TacklingStyle.StayOnFeet => MatchTacklingStyle.StayOnFeet,
                TacklingStyle.Normal => MatchTacklingStyle.Normal,
                TacklingStyle.Aggressive => MatchTacklingStyle.Aggressive,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.Tackling, "Unknown tackling style."),
            },
            TimeWasting = instructions.TimeWasting switch
            {
                TimeWasting.Off => MatchTimeWasting.Off,
                TimeWasting.Situational => MatchTimeWasting.Situational,
                TimeWasting.On => MatchTimeWasting.On,
                _ => throw new ArgumentOutOfRangeException(nameof(instructions), instructions.TimeWasting, "Unknown time-wasting setting."),
            },
        };
    }

    /// <summary>Maps a domain event type to the stored one, which mirrors the engine's by value (`MAT-8`).</summary>
    /// <param name="type">The engine's event type.</param>
    public static StoredEventType EventType(EngineEventType type) => type switch
    {
        EngineEventType.KickOff => StoredEventType.KickOff,
        EngineEventType.HalfTime => StoredEventType.HalfTime,
        EngineEventType.SecondHalfStart => StoredEventType.SecondHalfStart,
        EngineEventType.FullTime => StoredEventType.FullTime,
        EngineEventType.Goal => StoredEventType.Goal,
        EngineEventType.PenaltyAwarded => StoredEventType.PenaltyAwarded,
        EngineEventType.PenaltyGoal => StoredEventType.PenaltyGoal,
        EngineEventType.PenaltyMissed => StoredEventType.PenaltyMissed,
        EngineEventType.ShotSaved => StoredEventType.ShotSaved,
        EngineEventType.ShotBlocked => StoredEventType.ShotBlocked,
        EngineEventType.ShotOffTarget => StoredEventType.ShotOffTarget,
        EngineEventType.Woodwork => StoredEventType.Woodwork,
        EngineEventType.Foul => StoredEventType.Foul,
        EngineEventType.YellowCard => StoredEventType.YellowCard,
        EngineEventType.SecondYellowCard => StoredEventType.SecondYellowCard,
        EngineEventType.RedCard => StoredEventType.RedCard,
        EngineEventType.Offside => StoredEventType.Offside,
        EngineEventType.Corner => StoredEventType.Corner,
        EngineEventType.Injury => StoredEventType.Injury,
        EngineEventType.Substitution => StoredEventType.Substitution,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown engine event type."),
    };

    /// <summary>Maps a shot zone to the stored one.</summary>
    /// <param name="zone">The zone.</param>
    public static StoredShotZone Zone(ShotZone zone) => zone switch
    {
        ShotZone.Central => StoredShotZone.Central,
        ShotZone.InsideLeft => StoredShotZone.InsideLeft,
        ShotZone.InsideRight => StoredShotZone.InsideRight,
        ShotZone.WideLeft => StoredShotZone.WideLeft,
        ShotZone.WideRight => StoredShotZone.WideRight,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unknown shot zone."),
    };

    /// <summary>Maps a substitution reason to the stored one.</summary>
    /// <param name="reason">The reason.</param>
    public static StoredSubstitutionCause SubstitutionCause(MatchSubstitutionReason reason) => reason switch
    {
        MatchSubstitutionReason.Injury => StoredSubstitutionCause.Injury,
        MatchSubstitutionReason.Fatigue => StoredSubstitutionCause.Fatigue,
        MatchSubstitutionReason.Tactical => StoredSubstitutionCause.Tactical,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown substitution reason."),
    };
}
