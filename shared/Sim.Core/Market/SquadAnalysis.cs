using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Generation;
using Sim.Core.Match;

namespace Sim.Core.Market
{
    /// <summary>A role a club wants to strengthen, and the rating its current best option holds there.</summary>
    public readonly struct RoleNeed
    {
        public readonly PositionRole Role;
        /// <summary>Overall of the club's current best player in this role (0 if it has none). A signing must beat this by <see cref="TransferBalance.UpgradeMinPoints"/>.</summary>
        public readonly int CurrentBest;
        /// <summary>True when the club is short of players in this role (below template depth), not just lacking quality.</summary>
        public readonly bool DepthShortage;

        public RoleNeed(PositionRole role, int currentBest, bool depthShortage)
        {
            Role = role;
            CurrentBest = currentBest;
            DepthShortage = depthShortage;
        }
    }

    /// <summary>
    /// Squad-need analysis for the AI transfer market (task 5.2): which roles a club wants to
    /// buy, who counts as a regular starter (best XI — never sold for peanuts), the club's
    /// quality standard, and whether a given player can legally be sold (keeps a legal squad).
    ///
    /// PURE and deterministic (no RNG): a stable function of the club's current squad, reusing
    /// <see cref="LineupSelector.BestEleven(Club)"/> for the starter set and
    /// <see cref="PlayerRating"/> for ratings. Magnitudes come from <see cref="TransferBalance"/>;
    /// the role targets come from <see cref="SquadTemplate.Default"/>.
    /// </summary>
    public sealed class SquadAnalysis
    {
        /// <summary>Player ids in the club's best eleven — its spine.</summary>
        public IReadOnlyCollection<int> StarterIds { get; }

        /// <summary>Average overall of the best eleven — the bar new signings should clear.</summary>
        public int Standard { get; }

        /// <summary>Roles the club wants to strengthen, in priority order (depth shortages first, then biggest quality gaps).</summary>
        public IReadOnlyList<RoleNeed> Needs { get; }

        private readonly Club _club;
        private readonly HashSet<int> _starters;
        private readonly Dictionary<PositionRole, int> _depth;
        private readonly Dictionary<PositionRole, int> _templateCounts;

        private SquadAnalysis(Club club, HashSet<int> starters, int standard,
                              List<RoleNeed> needs, Dictionary<PositionRole, int> depth,
                              Dictionary<PositionRole, int> templateCounts)
        {
            _club = club;
            _starters = starters;
            Standard = standard;
            Needs = needs;
            _depth = depth;
            _templateCounts = templateCounts;
            StarterIds = starters;
        }

        public bool IsStarter(int playerId) => _starters.Contains(playerId);

        /// <summary>
        /// How important the player is to this club: a best-XI member is a <see cref="PlayerImportance.Starter"/>,
        /// a player beyond the template depth in his role is <see cref="PlayerImportance.Surplus"/>, otherwise
        /// a useful <see cref="PlayerImportance.Squad"/> player. Drives the asking price and the no-peanuts floor.
        /// </summary>
        public PlayerImportance ImportanceOf(Player player)
        {
            if (_starters.Contains(player.Id)) return PlayerImportance.Starter;
            int have = _depth.TryGetValue(player.Role, out int d) ? d : 0;
            int template = _templateCounts.TryGetValue(player.Role, out int t) ? t : 0;
            return have > template ? PlayerImportance.Surplus : PlayerImportance.Squad;
        }

        /// <summary>
        /// True if the club may legally part with this player: it keeps at least
        /// <see cref="TransferBalance.MinSquadSize"/> players overall and
        /// <see cref="TransferBalance.MinPerRoleDepth"/> in the player's role.
        /// </summary>
        public bool CanSell(Player player, TransferBalance cfg)
        {
            if (_club.Squad.Players.Count <= cfg.MinSquadSize) return false;
            int depth = _depth.TryGetValue(player.Role, out int d) ? d : 0;
            return depth > cfg.MinPerRoleDepth;
        }

        public static SquadAnalysis Analyze(Club club, TransferBalance cfg)
        {
            // Starters + quality standard from the best eleven.
            var starters = new HashSet<int>();
            long ratingSum = 0;
            int ratingCount = 0;
            Lineup xi = LineupSelector.BestEleven(club);
            foreach (LineupSlot slot in xi.Slots)
            {
                starters.Add(slot.Player.Id);
                ratingSum += PlayerRating.OverallFor(slot.Player, slot.Role);
                ratingCount++;
            }
            int standard = ratingCount > 0 ? (int)(ratingSum / ratingCount) : 0;

            // Per-role depth (by natural role).
            var depth = new Dictionary<PositionRole, int>();
            foreach (Player p in club.Squad.Players)
            {
                depth.TryGetValue(p.Role, out int d);
                depth[p.Role] = d + 1;
            }

            var needs = new List<RoleNeed>();
            var templateCounts = new Dictionary<PositionRole, int>();
            foreach (var (role, templateCount) in SquadTemplate.Default)
            {
                templateCounts[role] = templateCount;
                int best = 0;
                foreach (Player p in club.Squad.Players)
                {
                    int r = PlayerRating.OverallFor(p, role);
                    if (r > best) best = r;
                }

                depth.TryGetValue(role, out int have);
                bool depthShortage = have < templateCount;
                bool qualityGap = best < standard - cfg.NeedQualityGapPoints;

                if (depthShortage || qualityGap)
                    needs.Add(new RoleNeed(role, best, depthShortage));
            }

            // Priority: depth shortages first, then the biggest quality gaps (lowest CurrentBest).
            needs.Sort((a, b) =>
            {
                if (a.DepthShortage != b.DepthShortage) return a.DepthShortage ? -1 : 1;
                int byGap = a.CurrentBest.CompareTo(b.CurrentBest);
                if (byGap != 0) return byGap;
                return ((int)a.Role).CompareTo((int)b.Role); // stable tie-break
            });

            return new SquadAnalysis(club, starters, standard, needs, depth, templateCounts);
        }
    }
}
