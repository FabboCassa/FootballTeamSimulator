using System.Collections.Generic;
using UnityEngine;

namespace Fts.Services.Online
{
    /// <summary>
    /// Local, per-league memory for the auction screen: the players you are FOLLOWING (a shortlist you can
    /// build before ever bidding) and the lots you have ALREADY BID ON (so the screen can tell "you were
    /// outbid" from "you never went for him" — the server's lot only carries the current leader).
    ///
    /// Deliberately client-side and PlayerPrefs-backed: it is a personal note, not shared state, and it
    /// survives leaving the screen or restarting the app. Favourites are keyed by PLAYER (a lot only lives
    /// for one window, the player comes back), bids by LOT.
    /// </summary>
    public static class AuctionWatchlist
    {
        private const string FavoritePrefix = "fts.auction.fav.";
        private const string BidPrefix = "fts.auction.bid.";
        private const int MaxBidsRemembered = 300;

        // --- favourites (player external ids) -------------------------------------------------------

        public static bool IsFavorite(string leagueId, int playerExternalId) =>
            Load(FavoritePrefix + leagueId).Contains(playerExternalId.ToString());

        /// <summary>Adds or removes the player; returns the state AFTER the toggle.</summary>
        public static bool ToggleFavorite(string leagueId, int playerExternalId)
        {
            string key = FavoritePrefix + leagueId;
            List<string> ids = Load(key);
            string id = playerExternalId.ToString();
            bool nowFavorite;
            if (ids.Contains(id)) { ids.Remove(id); nowFavorite = false; }
            else { ids.Add(id); nowFavorite = true; }
            Save(key, ids);
            return nowFavorite;
        }

        // --- lots I have bid on ---------------------------------------------------------------------

        public static bool HasBid(string leagueId, string auctionId) =>
            !string.IsNullOrEmpty(auctionId) && Load(BidPrefix + leagueId).Contains(auctionId);

        public static void RecordBid(string leagueId, string auctionId)
        {
            if (string.IsNullOrEmpty(auctionId)) return;
            string key = BidPrefix + leagueId;
            List<string> ids = Load(key);
            if (ids.Contains(auctionId)) return;
            ids.Add(auctionId);
            // Keep the note short — old windows are of no interest.
            while (ids.Count > MaxBidsRemembered) ids.RemoveAt(0);
            Save(key, ids);
        }

        // --- storage ---------------------------------------------------------------------------------

        private static List<string> Load(string key)
        {
            string raw = PlayerPrefs.GetString(key, string.Empty);
            var list = new List<string>();
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (string part in raw.Split(','))
                if (!string.IsNullOrEmpty(part)) list.Add(part);
            return list;
        }

        private static void Save(string key, List<string> ids)
        {
            PlayerPrefs.SetString(key, string.Join(",", ids));
            PlayerPrefs.Save();
        }
    }
}
