using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>
/// Resolves a player's dynamic spatial pitch position based on base formation coordinates,
/// ball position, attacking/defending phases, and tactical instructions.
/// </summary>
public static class TacticalFormationResolver
{
    public static SpatialPoint ResolvePosition(
        MatchSlotV1 slot,
        bool isHome,
        bool hasPossession,
        SpatialPoint ballPosition,
        MatchInstructionsV1 instructions)
    {
        var anchor = SpatialPitch.Orient(slot.X, slot.Y, isHome);

        // Calculate team movement offsets based on ball position and tactical phase
        var ballProgression = (double)ballPosition.X / SpatialPitch.PitchLength;
        if (!isHome)
        {
            ballProgression = 1.0 - ballProgression;
        }

        var xOffset = 0;
        var yOffset = 0;

        if (hasPossession)
        {
            // Attacking phase: team shifts forward into space
            var mentalityShift = instructions.Mentality switch
            {
                MatchMentality.Attacking => 700,
                MatchMentality.Positive => 400,
                MatchMentality.Defensive => -400,
                MatchMentality.Cautious => -200,
                _ => 200,
            };

            var lineAdvance = (int)((ballProgression - 0.5) * 1200);
            xOffset = isHome ? (mentalityShift + lineAdvance) : -(mentalityShift + lineAdvance);

            // Width stretch in possession
            var widthScale = instructions.Width switch
            {
                MatchWidth.Wide => 1.25,
                MatchWidth.Narrow => 0.85,
                _ => 1.0,
            };
            yOffset = (int)((anchor.Y - SpatialPitch.GoalYCenter) * (widthScale - 1.0));
        }
        else
        {
            // Defensive phase: team forms a compact block
            var defensiveLineShift = instructions.DefensiveLine switch
            {
                MatchDefensiveLine.High => 400,
                MatchDefensiveLine.Deep => -600,
                _ => 0,
            };

            var ballAttraction = (int)((ballProgression - 0.5) * 800);
            xOffset = isHome ? (defensiveLineShift + ballAttraction) : -(defensiveLineShift + ballAttraction);

            // Compression towards center defensively
            yOffset = -(int)((anchor.Y - SpatialPitch.GoalYCenter) * 0.15);
        }

        // Goalkeepers stay mostly near their line
        if (slot.Family == MatchPositionFamily.Goalkeeper)
        {
            xOffset = Math.Clamp(xOffset, isHome ? 0 : -400, isHome ? 400 : 0);
            yOffset = Math.Clamp(yOffset, -300, 300);
        }

        return new SpatialPoint(
            Math.Clamp(anchor.X + xOffset, 150, SpatialPitch.PitchLength - 150),
            Math.Clamp(anchor.Y + yOffset, 150, SpatialPitch.PitchWidth - 150));
    }
}
