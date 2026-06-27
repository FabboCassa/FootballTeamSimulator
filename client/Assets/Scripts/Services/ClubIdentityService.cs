using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Identity;
using UnityEngine;

namespace Fts.Services
{
    /// <summary>
    /// Host bridge for the club-identity / art layer (task 6.1b). Wraps the pure Sim.Core
    /// <see cref="ClubIdentityGenerator"/>: a club's colours + crest are generated deterministically
    /// from (clubId, <see cref="CareerState.Seed"/>) and cached, so every screen reads the same
    /// identity and nothing is stored (no save bump). Exposes a render-ready <see cref="ClubVisual"/>
    /// (Unity colours + crest ints) so the dumb FTS.Views layer never sees a Sim.Core type.
    /// Lives in the Game scope; never touches the engine, so golden masters stay safe.
    /// </summary>
    public sealed class ClubIdentityService
    {
        private readonly CareerState _career;
        private readonly IdentityBalance _cfg = new BalanceConfig().Identity;
        private readonly Dictionary<int, ClubVisual> _cache = new Dictionary<int, ClubVisual>();

        public ClubIdentityService(CareerState career)
        {
            _career = career;
        }

        /// <summary>The render-ready visual identity for a club (cached per club id).</summary>
        public ClubVisual Visual(int clubId)
        {
            if (_cache.TryGetValue(clubId, out ClubVisual cached))
                return cached;

            ClubIdentity id = ClubIdentityGenerator.Generate(clubId, _career.Seed, _cfg);
            var visual = new ClubVisual(
                (int)id.Crest.Shape,
                (int)id.Crest.Pattern,
                ToColor(id.Colors.Primary),
                ToColor(id.Colors.Secondary),
                ToColor(id.Colors.Accent),
                ToColor(id.Crest.Emblem));

            _cache[clubId] = visual;
            return visual;
        }

        /// <summary>The user club's visual identity.</summary>
        public ClubVisual UserVisual() => Visual(_career.UserClubId);

        private static Color ToColor(RgbColor c) => new Color(c.R / 255f, c.G / 255f, c.B / 255f);
    }
}
