using Sim.Core.Config;
using Sim.Core.Match.Movement;
using Sim.Core.Tactics;

namespace Sim.Core.Career
{
    /// <summary>
    /// How a side's tactic scales its expected goals in the fast model (watchable-match R17,
    /// real-match R24): 100 plus the side's own GoalsFor reading and the opponent's GoalsAgainst
    /// reading, in percent points. A reading is the formation's entry plus the entry of the
    /// catalogue style the instructions are nearest to, plus, for every axis where the
    /// instructions leave that style's preset, the single-axis difference. Instructions nearer
    /// Balanced than any other style (every v11 set) therefore read the axis tables alone, and
    /// every table is 0 on its neutral level, so a Balanced 4-3-3 side is exactly 100: the v12
    /// calibration alone.
    /// </summary>
    public static class QuickTacticPercent
    {
        private static readonly PlayingStyle[] Styles = new PlayingStyle[PlayingStyles.All.Count];
        private static readonly TacticInstructions[] Presets = new TacticInstructions[PlayingStyles.All.Count];

        static QuickTacticPercent()
        {
            for (int i = 0; i < Styles.Length; i++)
            {
                Styles[i] = PlayingStyles.All[i].Style;
                Presets[i] = PlayingStyles.All[i].Instructions;
            }
        }

        public static int Of(Tactic own, Tactic opponent, WorldBalance w)
        {
            Readings(own, w, out int goalsFor, out _);
            Readings(opponent, w, out _, out int goalsAgainst);
            return 100 + goalsFor + goalsAgainst;
        }

        /// <summary>A side's two readings: its own GoalsFor and the GoalsAgainst it hands its opponent.</summary>
        public static void Readings(in Tactic t, WorldBalance w, out int goalsFor, out int goalsAgainst)
        {
            // An untweaked preset (the usual background club, and every Balanced side) has no axis
            // differences to add, so it skips the catalogue walk and the axis tables altogether.
            int preset = ExactPreset(t.Instructions);
            int nearest = preset >= 0 ? preset : NearestStyle(t.Instructions);
            goalsFor = MovementTactics.Pick(w.QuickStyleGoalsForPercent, (int)Styles[nearest])
                + MovementTactics.Pick(w.QuickFormationGoalsForPercent, (int)t.Formation);
            goalsAgainst = MovementTactics.Pick(w.QuickStyleGoalsAgainstPercent, (int)Styles[nearest])
                + MovementTactics.Pick(w.QuickFormationGoalsAgainstPercent, (int)t.Formation);
            if (preset >= 0) return;

            goalsFor += GoalsFor(t.Instructions, w) - GoalsFor(Presets[nearest], w);
            goalsAgainst += GoalsAgainst(t.Instructions, w) - GoalsAgainst(Presets[nearest], w);
        }

        private static int ExactPreset(in TacticInstructions instructions)
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Same(instructions, Presets[i])) return i;
            return -1;
        }

        private static bool Same(in TacticInstructions a, in TacticInstructions b) =>
            a.Mentality == b.Mentality && a.PassingDirectness == b.PassingDirectness && a.Tempo == b.Tempo
            && a.Width == b.Width && a.BuildUp == b.BuildUp && a.FinalThirdApproach == b.FinalThirdApproach
            && a.ShotSelection == b.ShotSelection && a.DribbleFrequency == b.DribbleFrequency
            && a.OnBallLost == b.OnBallLost && a.OnBallWon == b.OnBallWon
            && a.LineOfEngagement == b.LineOfEngagement && a.DefensiveLine == b.DefensiveLine
            && a.Pressing == b.Pressing && a.MarkingScheme == b.MarkingScheme && a.PressDirection == b.PressDirection;

        /// <summary>
        /// The catalogue entry with the fewest axes away from <paramref name="instructions"/>; ties go to
        /// the earlier entry, so Balanced (the first) wins any tie.
        /// </summary>
        public static int NearestStyle(in TacticInstructions instructions)
        {
            int best = 0, bestDistance = int.MaxValue;
            for (int i = 0; i < Presets.Length && bestDistance > 0; i++)
            {
                int distance = Distance(instructions, Presets[i]);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static int Distance(in TacticInstructions a, in TacticInstructions b) =>
            (a.Mentality != b.Mentality ? 1 : 0) + (a.PassingDirectness != b.PassingDirectness ? 1 : 0)
            + (a.Tempo != b.Tempo ? 1 : 0) + (a.Width != b.Width ? 1 : 0)
            + (a.BuildUp != b.BuildUp ? 1 : 0) + (a.FinalThirdApproach != b.FinalThirdApproach ? 1 : 0)
            + (a.ShotSelection != b.ShotSelection ? 1 : 0) + (a.DribbleFrequency != b.DribbleFrequency ? 1 : 0)
            + (a.OnBallLost != b.OnBallLost ? 1 : 0) + (a.OnBallWon != b.OnBallWon ? 1 : 0)
            + (a.LineOfEngagement != b.LineOfEngagement ? 1 : 0) + (a.DefensiveLine != b.DefensiveLine ? 1 : 0)
            + (a.Pressing != b.Pressing ? 1 : 0) + (a.MarkingScheme != b.MarkingScheme ? 1 : 0)
            + (a.PressDirection != b.PressDirection ? 1 : 0);

        private static int GoalsFor(in TacticInstructions i, WorldBalance w) =>
            MovementTactics.Pick(w.QuickMentalityGoalsForPercent, (int)i.Mentality)
            + MovementTactics.Pick(w.QuickPassingDirectnessGoalsForPercent, (int)i.PassingDirectness)
            + MovementTactics.Pick(w.QuickTempoGoalsForPercent, (int)i.Tempo)
            + MovementTactics.Pick(w.QuickWidthGoalsForPercent, (int)i.Width)
            + MovementTactics.Pick(w.QuickBuildUpGoalsForPercent, (int)i.BuildUp)
            + MovementTactics.Pick(w.QuickFinalThirdGoalsForPercent, (int)i.FinalThirdApproach)
            + MovementTactics.Pick(w.QuickShotSelectionGoalsForPercent, (int)i.ShotSelection)
            + MovementTactics.Pick(w.QuickDribbleGoalsForPercent, (int)i.DribbleFrequency)
            + MovementTactics.Pick(w.QuickOnBallLostGoalsForPercent, (int)i.OnBallLost)
            + MovementTactics.Pick(w.QuickOnBallWonGoalsForPercent, (int)i.OnBallWon)
            + MovementTactics.Pick(w.QuickLineOfEngagementGoalsForPercent, (int)i.LineOfEngagement)
            + MovementTactics.Pick(w.QuickDefensiveLineGoalsForPercent, (int)i.DefensiveLine)
            + MovementTactics.Pick(w.QuickPressingGoalsForPercent, (int)i.Pressing)
            + MovementTactics.Pick(w.QuickMarkingSchemeGoalsForPercent, (int)i.MarkingScheme)
            + MovementTactics.Pick(w.QuickPressDirectionGoalsForPercent, (int)i.PressDirection);

        private static int GoalsAgainst(in TacticInstructions i, WorldBalance w) =>
            MovementTactics.Pick(w.QuickMentalityGoalsAgainstPercent, (int)i.Mentality)
            + MovementTactics.Pick(w.QuickPassingDirectnessGoalsAgainstPercent, (int)i.PassingDirectness)
            + MovementTactics.Pick(w.QuickTempoGoalsAgainstPercent, (int)i.Tempo)
            + MovementTactics.Pick(w.QuickWidthGoalsAgainstPercent, (int)i.Width)
            + MovementTactics.Pick(w.QuickBuildUpGoalsAgainstPercent, (int)i.BuildUp)
            + MovementTactics.Pick(w.QuickFinalThirdGoalsAgainstPercent, (int)i.FinalThirdApproach)
            + MovementTactics.Pick(w.QuickShotSelectionGoalsAgainstPercent, (int)i.ShotSelection)
            + MovementTactics.Pick(w.QuickDribbleGoalsAgainstPercent, (int)i.DribbleFrequency)
            + MovementTactics.Pick(w.QuickOnBallLostGoalsAgainstPercent, (int)i.OnBallLost)
            + MovementTactics.Pick(w.QuickOnBallWonGoalsAgainstPercent, (int)i.OnBallWon)
            + MovementTactics.Pick(w.QuickLineOfEngagementGoalsAgainstPercent, (int)i.LineOfEngagement)
            + MovementTactics.Pick(w.QuickDefensiveLineGoalsAgainstPercent, (int)i.DefensiveLine)
            + MovementTactics.Pick(w.QuickPressingGoalsAgainstPercent, (int)i.Pressing)
            + MovementTactics.Pick(w.QuickMarkingSchemeGoalsAgainstPercent, (int)i.MarkingScheme)
            + MovementTactics.Pick(w.QuickPressDirectionGoalsAgainstPercent, (int)i.PressDirection);
    }
}
