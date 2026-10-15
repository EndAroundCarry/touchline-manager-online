using TouchlineManager.Application.Match;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>Fires direct free kicks through the set-piece code with no keeper and no wall body: where the ball goes, by taker craft and distance.</summary>
internal static class FreeKickShapeProbe
{
    public static void Run(int kicks)
    {
        Console.WriteLine($"== Free kick flight, {kicks} kicks a cell, no keeper, central spot, home attacking ==");
        Console.WriteLine($"  {"dist m",6} {"skill",5} {"goal%",6} {"post%",6} {"bar%",5} {"over%",6} {"wide%",6} {"wall<=35%",9} {"meanZ@line",10} {"meanY@line",10}");

        foreach (var metres in new[] { 18, 23, 28 })
        {
            foreach (var skill in new[] { 6, 13, 20 })
            {
                var skills = TickPlayerSkills.From(PlayerAttributesV1.Uniform(skill));
                var spotX = SpatialPitch.PitchLength - (int)(metres * 95.238);
                var spotY = SpatialPitch.GoalYCenter - 600;
                var inset = TickSetPieces.FreeKickInsetPoor - ((TickSetPieces.FreeKickInsetPoor - TickSetPieces.FreeKickInsetBest) * TickSetPieces.Craft(skills) / 10_000);
                var aim = new SpatialPoint(SpatialPitch.PitchLength + TickBallCarrierBrain.ShotOvershoot, SpatialPitch.GoalYCenter + (TickSpatialUnits.GoalMouthMaxUnits - SpatialPitch.GoalYCenter - inset));
                var decision = new TickCarrierDecision(TickCarrierAction.Shoot, -1, aim, 0, 0, TickBallCarrierBrain.EffectivePressure(TickSetPieces.FreeKickPressure, skill));
                int goal = 0, post = 0, bar = 0, over = 0, wide = 0, wall = 0;
                long sumZ = 0, sumY = 0, counted = 0;

                for (ulong seed = 1; seed <= (ulong)kicks; seed++)
                {
                    var ball = new TickBallPhysics();

                    ball.PlaceAt(spotX, spotY);
                    TickSetPieces.Execute(TickRestartKind.FreeKick, decision, skills, ball, new Pcg32(seed * 7919));

                    var wallX = spotX + TickSetPieces.WallDistance;
                    var wallZ = -1;
                    var result = TickBallBoundary.InPlay;
                    var z = 0;
                    var y = 0;

                    for (var tick = 0; tick < 80 && result == TickBallBoundary.InPlay; tick++)
                    {
                        z = ball.UnitZ;
                        y = ball.UnitY;
                        result = ball.Step();

                        if (wallZ < 0 && ball.UnitX >= wallX)
                        {
                            wallZ = ball.UnitZ;
                        }
                    }

                    wall += wallZ <= TickBallPhysics.HeadReachZUnits ? 1 : 0;

                    switch (result)
                    {
                        case TickBallBoundary.GoalAwayEnd: goal++; break;
                        case TickBallBoundary.HitPost: post++; break;
                        case TickBallBoundary.HitCrossbar: bar++; break;
                        case TickBallBoundary.OutGoalLine: if (ball.UnitZ > TickSpatialUnits.CrossbarZUnits && ball.UnitY > 3123 && ball.UnitY < 3877) { over++; } else { wide++; } break;
                        default: wide++; break;
                    }

                    if (result is TickBallBoundary.GoalAwayEnd or TickBallBoundary.OutGoalLine or TickBallBoundary.HitPost or TickBallBoundary.HitCrossbar)
                    {
                        sumZ += ball.UnitZ;
                        sumY += ball.UnitY;
                        counted++;
                    }
                }

                double P(int n) => 100.0 * n / kicks;

                Console.WriteLine($"  {metres,6} {skill,5} {P(goal),6:F1} {P(post),6:F1} {P(bar),5:F1} {P(over),6:F1} {P(wide),6:F1} {P(wall),9:F1} {(double)sumZ / Math.Max(1, counted),10:F1} {(double)sumY / Math.Max(1, counted),10:F0}");
            }
        }
    }
}
