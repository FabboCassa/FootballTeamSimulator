using System.Collections.Generic;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Tactics
{
    /// <summary>
    /// How an AI club picks its style and formation (real-match spec R22). Each style is a point
    /// in a small trait space read off its preset - directness, pressing, attacking intent,
    /// width. Each club gets an ideal point from what its squad does well relative to its league,
    /// from how well it passes in absolute terms (only good passers keep the ball, so weaker,
    /// lower-tier squads go direct) and from its manager's <see cref="ManagerProfile"/>; the style
    /// nearest that point wins. Integer-only and RNG-free: the same league always gets the same
    /// choices.
    /// </summary>
    public static class AiStyleChooser
    {
        /// <summary>No style may be used by more than this share of a league's clubs.</summary>
        public const int MaxStyleSharePercent = 30;

        /// <summary>A league of at least this many clubs shows at least this many styles.</summary>
        public const int MinStylesInUse = 6;

        /// <summary>Tiki-taka needs first-choice passers at least this good.</summary>
        public const int TikiTakaMinPassing = 66;

        /// <summary>Passing level at which keeping the ball is neither encouraged nor discouraged.</summary>
        private const int NeutralPassing = 70;

        /// <summary>Halves the absolute passing gap so it bends, but does not dictate, the choice.</summary>
        private const int AbsolutePassingDivisor = 2;

        // Ideals are in tenths of a trait point; one manager lean point moves them by these.
        private const int DirectnessPerLean = 8;
        private const int PressingPerLean = 7;
        private const int AttackPerLean = 4;
        private const int WidthPerLean = 3;

        /// <summary>Tenths per directness point, so the [-2, 2] trait spans the ideal's [-30, 30].</summary>
        private const int DirectnessScale = 15;

        /// <summary>A coach picks a shape his players know: each one fielded out of his natural role costs this.</summary>
        private const int OutOfPositionPenalty = 10;

        /// <summary>Attacking intent decides more than the other traits (a weak side should not attack).</summary>
        private const int AttackDistanceWeight = 2;

        /// <summary>
        /// The fitness of <paramref name="style"/> for a squad within a league with the given mean,
        /// under a manager's profile: higher is better, <see cref="int.MinValue"/> when the squad
        /// cannot play it.
        /// </summary>
        public static int Score(PlayingStyle style, SquadStrengths squad, SquadStrengths leagueMean, ManagerProfile profile)
        {
            if (style == PlayingStyle.TikiTaka && squad.Passing < TikiTakaMinPassing) return int.MinValue;

            TacticInstructions t = PlayingStyles.Get(style).Instructions;
            // Tempo is left out: the presets pair a fast tempo with short passing (quick circulation)
            // and a slow one with the long ball, so it does not mark how direct a style is.
            int directness = (int)t.PassingDirectness + (int)t.BuildUp - 2;              // [-2, 2]
            int press = (int)t.Pressing + (int)t.LineOfEngagement - 2;                   // [-2, 2]
            int attack = (int)t.Mentality - 1;                                           // [-1, 1]
            int width = (int)t.Width - 1;                                                // [-1, 1]

            int zPass = squad.Passing - leagueMean.Passing;
            int zFwd = squad.Forwards - leagueMean.Forwards;

            int idealDirectness = Clamp(zFwd - zPass + (NeutralPassing - squad.Passing) / AbsolutePassingDivisor
                                        - DirectnessPerLean * profile.Possession, 30);
            int idealPress = Clamp(2 * (squad.Stamina - leagueMean.Stamina) + PressingPerLean * profile.Pressing, 20);
            int idealAttack = Clamp(squad.Overall - leagueMean.Overall + (zFwd - (squad.Defending - leagueMean.Defending)) / 2
                                    + AttackPerLean * profile.Attack, 10);
            int idealWidth = Clamp(2 * (squad.Wide - leagueMean.Wide) + WidthPerLean * profile.Attack, 10);

            return -(Square(DirectnessScale * directness - idealDirectness)
                     + Square(10 * press - idealPress)
                     + AttackDistanceWeight * Square(10 * attack - idealAttack)
                     + Square(10 * width - idealWidth));
        }

        private static int Clamp(int value, int bound) => value < -bound ? -bound : value > bound ? bound : value;

        private static int Square(int value) => value * value;

        /// <summary>
        /// The style's suggested formation the squad fills best (sum of the best available
        /// eleven's role overalls, less <see cref="OutOfPositionPenalty"/> per player out of his
        /// natural role); ties keep the style's own order.
        /// </summary>
        public static Formation BestFormation(Club club, PlayingStyle style)
        {
            IReadOnlyList<Formation> suggested = PlayingStyles.Get(style).SuggestedFormations;
            Formation best = suggested[0];
            int bestFit = int.MinValue;
            foreach (Formation formation in suggested)
            {
                int fit = 0;
                foreach (LineupSlot slot in LineupSelector.BestAvailable(club, Formations.Roles(formation)).Slots)
                    fit += PlayerRating.OverallFor(slot.Player, slot.Role)
                           - (slot.Player.Role == slot.Role ? 0 : OutOfPositionPenalty);
                if (fit > bestFit)
                {
                    best = formation;
                    bestFit = fit;
                }
            }
            return best;
        }

        /// <summary>
        /// Sets every club's <see cref="Club.Style"/> and <see cref="Club.Formation"/> from its squad
        /// and its coach's profile. Clubs choose in league order (strongest first); a style already
        /// at <see cref="MaxStyleSharePercent"/> of the league is skipped for the next best, and the
        /// last clubs take their best unused style when the league would otherwise end up with
        /// fewer than <see cref="MinStylesInUse"/>.
        /// </summary>
        public static void AssignLeague(League league)
        {
            int clubCount = league.Clubs.Count;
            if (clubCount == 0) return;

            var squads = new List<SquadStrengths>(clubCount);
            foreach (Club club in league.Clubs) squads.Add(SquadStrengths.Of(club));
            SquadStrengths mean = SquadStrengths.Mean(squads);

            int cap = clubCount * MaxStyleSharePercent / 100;
            if (cap < 1) cap = 1;
            var used = new Dictionary<PlayingStyle, int>();

            for (int c = 0; c < clubCount; c++)
            {
                Club club = league.Clubs[c];
                bool mustBeNew = MinStylesInUse - used.Count >= clubCount - c;
                PlayingStyle style = PickStyle(squads[c], mean, club.Coach.Profile, used, mustBeNew ? 1 : cap);
                used[style] = (used.TryGetValue(style, out int n) ? n : 0) + 1;
                club.Style = style;
                club.Formation = BestFormation(club, style);
            }
        }

        /// <summary>The best-scoring playable style used fewer than <paramref name="cap"/> times so far.</summary>
        private static PlayingStyle PickStyle(SquadStrengths squad, SquadStrengths mean, ManagerProfile profile,
            Dictionary<PlayingStyle, int> used, int cap)
        {
            PlayingStyle best = PlayingStyle.Balanced;
            int bestScore = 0;
            bool found = false;
            foreach (StyleDefinition def in PlayingStyles.All)
            {
                if (used.TryGetValue(def.Style, out int n) && n >= cap) continue;
                int score = Score(def.Style, squad, mean, profile);
                if (score == int.MinValue) continue;
                if (!found || score > bestScore)
                {
                    best = def.Style;
                    bestScore = score;
                    found = true;
                }
            }
            return best;
        }
    }
}
