using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Result of a physical or technical duel on the pitch.
/// </summary>
public readonly record struct DuelResult(
    bool AttackerWon,
    bool WasFoul,
    bool ResultedInCard,
    bool IsRedCard,
    int QualityRatingBasisPoints);

/// <summary>
/// Resolves on-pitch contests between players: 1v1 ground duels, aerial duels,
/// tackles, and loose ball scrambles.
/// </summary>
public static class DuelResolver
{
    private const int HomeAdvantageDuelBonus = 400; // +4% bonus for home ground
    private const int BaseFoulProbabilityBasisPoints = 1200; // 12% chance a lost tackle is a foul
    private const int YellowCardFoulShare = 1800; // 18% of fouls lead to booking
    private const int RedCardFoulShare = 150; // 1.5% of fouls lead to straight red (cynical / last man)

    /// <summary>
    /// Resolves a 1v1 ground dribble vs tackle contest.
    /// </summary>
    public static DuelResult ResolveGroundDuel(
        MatchParticipantV1 attacker,
        MatchParticipantV1 defender,
        bool attackerIsHome,
        Pcg32 random,
        MatchTacklingStyle tacklingStyle = MatchTacklingStyle.Normal)
    {
        var attackerScore =
            (attacker.Attributes.ValueOf(MatchAttributeName.Dribbling) * 4) +
            (attacker.Attributes.ValueOf(MatchAttributeName.Agility) * 3) +
            (attacker.Attributes.ValueOf(MatchAttributeName.Pace) * 3);

        var defenderScore =
            (defender.Attributes.ValueOf(MatchAttributeName.Tackling) * 4) +
            (defender.Attributes.ValueOf(MatchAttributeName.Positioning) * 3) +
            (defender.Attributes.ValueOf(MatchAttributeName.Strength) * 3);

        if (attackerIsHome)
        {
            attackerScore += HomeAdvantageDuelBonus / 100;
        }
        else
        {
            defenderScore += HomeAdvantageDuelBonus / 100;
        }

        // Tackling instruction modifier
        if (tacklingStyle == MatchTacklingStyle.Aggressive)
        {
            defenderScore += 8;
        }
        else if (tacklingStyle == MatchTacklingStyle.StayOnFeet)
        {
            defenderScore -= 5;
        }

        var diff = attackerScore - defenderScore;
        var winProb = Math.Clamp(5000 + (diff * 200), 1500, 8500); // 15% - 85% range, mostly skill-based

        var attackerWon = random.RollBasisPoints(winProb);
        var wasFoul = false;
        var wasCard = false;
        var isRed = false;

        if (attackerWon)
        {
            // Defender failed tackle: chance of committing foul
            var foulChance = BaseFoulProbabilityBasisPoints;
            if (tacklingStyle == MatchTacklingStyle.Aggressive)
            {
                foulChance += 800;
            }

            wasFoul = random.RollBasisPoints(foulChance);
            if (wasFoul)
            {
                wasCard = random.RollBasisPoints(YellowCardFoulShare);
                if (!wasCard)
                {
                    isRed = random.RollBasisPoints(RedCardFoulShare);
                }
            }
        }

        return new DuelResult(attackerWon, wasFoul, wasCard, isRed, winProb);
    }

    /// <summary>
    /// Resolves an aerial contest (headers from crosses, corners, goal kicks).
    /// </summary>
    public static DuelResult ResolveAerialDuel(
        MatchParticipantV1 attacker,
        MatchParticipantV1 defender,
        bool attackerIsHome,
        Pcg32 random)
    {
        var attackerScore =
            (attacker.Attributes.ValueOf(MatchAttributeName.JumpingReach) * 5) +
            (attacker.Attributes.ValueOf(MatchAttributeName.Heading) * 3) +
            (attacker.Attributes.ValueOf(MatchAttributeName.Strength) * 2);

        var defenderScore =
            (defender.Attributes.ValueOf(MatchAttributeName.JumpingReach) * 5) +
            (defender.Attributes.ValueOf(MatchAttributeName.Heading) * 3) +
            (defender.Attributes.ValueOf(MatchAttributeName.Strength) * 2);

        if (attackerIsHome)
        {
            attackerScore += HomeAdvantageDuelBonus / 100;
        }
        else
        {
            defenderScore += HomeAdvantageDuelBonus / 100;
        }

        var diff = attackerScore - defenderScore;
        var winProb = Math.Clamp(5000 + (diff * 180), 1500, 8500);

        var attackerWon = random.RollBasisPoints(winProb);
        return new DuelResult(attackerWon, false, false, false, winProb);
    }
}
