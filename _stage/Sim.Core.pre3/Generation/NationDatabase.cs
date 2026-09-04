using System.Collections.Generic;
using Sim.Core.Domain;

namespace Sim.Core.Generation
{
    /// <summary>
    /// The built-in world atlas (task 11.1): 65 nations, from the big five down to the smaller
    /// leagues the roadmap asked for — the rest of Europe, South America, the USA and Mexico,
    /// Japan, South Korea, China, Australia, the Gulf and North/West Africa.
    ///
    /// The five big nations run three tiers each, exactly as the task's acceptance test requires;
    /// the strong second rank runs two; everyone else has a top flight. Club counts are always even
    /// because the double round-robin scheduler needs them to be.
    ///
    /// ORDER IS PART OF THE SAVE FORMAT. A nation's index in this list drives the id ranges its
    /// clubs and players get (<see cref="WorldIds"/>), so a world regenerates identically only as
    /// long as the order is untouched. APPEND new nations at the end; never reorder or remove.
    ///
    /// Real nations, invented clubs and players: see <see cref="CultureDatabase"/>.
    /// </summary>
    public static class NationDatabase
    {
        /// <summary>A fresh copy of the built-in atlas, in id order.</summary>
        public static List<NationProfile> BuiltIn()
        {
            return new List<NationProfile>
            {
                Nation("ENG", "England", Continent.Europe, 100, "english", D("Premier Division", 20), D("First Division", 24), D("Second Division", 24)),
                Nation("ESP", "Spain", Continent.Europe, 98, "spanish", D("Primera Division", 20), D("Segunda Division", 22), D("Tercera Division", 20)),
                Nation("ITA", "Italy", Continent.Europe, 96, "italian", D("Prima Divisione", 20), D("Seconda Divisione", 20), D("Terza Divisione", 20)),
                Nation("GER", "Germany", Continent.Europe, 95, "german", D("Erste Liga", 18), D("Zweite Liga", 18), D("Dritte Liga", 20)),
                Nation("FRA", "France", Continent.Europe, 92, "french", D("Ligue Un", 18), D("Ligue Deux", 18), D("Ligue Trois", 18)),
                Nation("BRA", "Brazil", Continent.SouthAmerica, 92, "portuguese", D("Serie Ouro", 20), D("Serie Prata", 20)),
                Nation("ARG", "Argentina", Continent.SouthAmerica, 88, "spanish", D("Liga Mayor", 28), D("Liga Menor", 22)),
                Nation("POR", "Portugal", Continent.Europe, 84, "portuguese", D("Primeira Divisao", 18), D("Segunda Divisao", 18)),
                Nation("NED", "Netherlands", Continent.Europe, 84, "dutch", D("Eerste Divisie", 18), D("Tweede Divisie", 20)),
                Nation("BEL", "Belgium", Continent.Europe, 78, "dutch", D("Eerste Klasse", 16), D("Tweede Klasse", 16)),
                Nation("TUR", "Turkey", Continent.Europe, 78, "aegean", D("Birinci Lig", 20), D("Ikinci Lig", 18)),
                Nation("MEX", "Mexico", Continent.NorthAmerica, 78, "spanish", D("Liga Nacional", 18)),
                Nation("RUS", "Russia", Continent.Europe, 76, "slavic", D("Vysshaya Liga", 16), D("Pervaya Liga", 18)),
                Nation("USA", "United States", Continent.NorthAmerica, 76, "american", D("National Soccer League", 28)),
                Nation("JPN", "Japan", Continent.Asia, 76, "japanese", D("J Premier", 20), D("J Second", 22)),
                Nation("KSA", "Saudi Arabia", Continent.Asia, 74, "arabic", D("Dawri Awwal", 18)),
                Nation("SCO", "Scotland", Continent.Europe, 72, "english", D("Premier Division", 12), D("First Division", 10)),
                Nation("AUT", "Austria", Continent.Europe, 72, "german", D("Bundesliga Eins", 12), D("Bundesliga Zwei", 16)),
                Nation("SUI", "Switzerland", Continent.Europe, 72, "german", D("Super Liga", 12), D("Challenge Liga", 10)),
                Nation("GRE", "Greece", Continent.Europe, 72, "aegean", D("Proti Kathigoria", 14), D("Defteri Kathigoria", 16)),
                Nation("UKR", "Ukraine", Continent.Europe, 72, "slavic", D("Vyshcha Liha", 16)),
                Nation("COL", "Colombia", Continent.SouthAmerica, 72, "spanish", D("Liga Colombiana", 20)),
                Nation("KOR", "South Korea", Continent.Asia, 72, "korean", D("K Premier", 12), D("K Challenge", 14)),
                Nation("DEN", "Denmark", Continent.Europe, 70, "nordic", D("Superliga", 12), D("Foersteliga", 12)),
                Nation("URU", "Uruguay", Continent.SouthAmerica, 70, "spanish", D("Primera Uruguaya", 16)),
                Nation("CHN", "China", Continent.Asia, 70, "chinese", D("Chao Ji Lianse", 16)),
                Nation("CZE", "Czechia", Continent.Europe, 68, "slavic", D("Prvni Liga", 16)),
                Nation("POL", "Poland", Continent.Europe, 68, "slavic", D("Ekstraliga", 18), D("Pierwsza Liga", 18)),
                Nation("NOR", "Norway", Continent.Europe, 68, "nordic", D("Eliteliga", 16)),
                Nation("SWE", "Sweden", Continent.Europe, 68, "nordic", D("Elitserien", 16)),
                Nation("CHI", "Chile", Continent.SouthAmerica, 68, "spanish", D("Primera Chilena", 16)),
                Nation("MAR", "Morocco", Continent.Africa, 68, "arabic", D("Botola Oula", 16)),
                Nation("SRB", "Serbia", Continent.Europe, 66, "slavic", D("Superliga Srpska", 16)),
                Nation("CRO", "Croatia", Continent.Europe, 66, "slavic", D("Prva Liga", 10)),
                Nation("IRN", "Iran", Continent.Asia, 66, "arabic", D("Liga Bartar", 16)),
                Nation("EGY", "Egypt", Continent.Africa, 66, "arabic", D("Dawri Mumtaz", 18)),
                Nation("AUS", "Australia", Continent.Oceania, 66, "english", D("A Premier", 12)),
                Nation("ROU", "Romania", Continent.Europe, 64, "slavic", D("Liga Intai", 16)),
                Nation("PAR", "Paraguay", Continent.SouthAmerica, 64, "spanish", D("Primera Paraguaya", 12)),
                Nation("PER", "Peru", Continent.SouthAmerica, 64, "spanish", D("Liga Peruana", 18)),
                Nation("ECU", "Ecuador", Continent.SouthAmerica, 64, "spanish", D("Serie Ecuatoriana", 16)),
                Nation("UAE", "United Arab Emirates", Continent.Asia, 64, "arabic", D("Dawri Khaleeji", 14)),
                Nation("HUN", "Hungary", Continent.Europe, 62, "slavic", D("Elso Osztaly", 12)),
                Nation("QAT", "Qatar", Continent.Asia, 62, "arabic", D("Dawri Najm", 12)),
                Nation("TUN", "Tunisia", Continent.Africa, 62, "arabic", D("Rabita Mumtaza", 16)),
                Nation("ALG", "Algeria", Continent.Africa, 62, "arabic", D("Rabita Awwal", 16)),
                Nation("BUL", "Bulgaria", Continent.Europe, 60, "slavic", D("Parva Liga", 14)),
                Nation("IRL", "Ireland", Continent.Europe, 60, "english", D("Premier Division", 10)),
                Nation("RSA", "South Africa", Continent.Africa, 60, "african", D("Premier Division", 16)),
                Nation("FIN", "Finland", Continent.Europe, 58, "nordic", D("Ykkosliiga", 12)),
                Nation("SVK", "Slovakia", Continent.Europe, 58, "slavic", D("Prva Liga", 12)),
                Nation("CYP", "Cyprus", Continent.Europe, 58, "aegean", D("Proto Protathlima", 14)),
                Nation("BOL", "Bolivia", Continent.SouthAmerica, 58, "spanish", D("Primera Boliviana", 16)),
                Nation("NGA", "Nigeria", Continent.Africa, 58, "african", D("Premier Division", 20)),
                Nation("SEN", "Senegal", Continent.Africa, 58, "african", D("Ligue Senegalaise", 14)),
                Nation("SVN", "Slovenia", Continent.Europe, 56, "slavic", D("Prva Liga", 10)),
                Nation("VEN", "Venezuela", Continent.SouthAmerica, 56, "spanish", D("Primera Venezolana", 14)),
                Nation("CAN", "Canada", Continent.NorthAmerica, 56, "american", D("Canadian Premier", 10)),
                Nation("CRC", "Costa Rica", Continent.NorthAmerica, 56, "spanish", D("Primera Tica", 12)),
                Nation("IRQ", "Iraq", Continent.Asia, 56, "arabic", D("Dawri Nukhba", 20)),
                Nation("GHA", "Ghana", Continent.Africa, 56, "african", D("Premier Division", 18)),
                Nation("CIV", "Ivory Coast", Continent.Africa, 56, "african", D("Ligue Ivoirienne", 14)),
                Nation("CMR", "Cameroon", Continent.Africa, 54, "african", D("Elite Un", 16)),
                Nation("ISL", "Iceland", Continent.Europe, 52, "nordic", D("Urvalsdeild", 12)),
                Nation("NZL", "New Zealand", Continent.Oceania, 48, "english", D("National Premier", 10)),
            };
        }

        /// <summary>Convenience: the profile with this code, or null.</summary>
        public static NationProfile? Find(IReadOnlyList<NationProfile> nations, string code)
        {
            foreach (NationProfile nation in nations)
            {
                if (nation.Code == code)
                    return nation;
            }

            return null;
        }

        private static NationProfile Nation(
            string code, string name, Continent continent, int reputation, string cultureId,
            params DivisionProfile[] divisions)
        {
            var profile = new NationProfile
            {
                Code = code,
                Name = name,
                Continent = continent,
                Reputation = reputation,
                CultureId = cultureId
            };

            foreach (DivisionProfile division in divisions)
                profile.Divisions.Add(division);

            return profile;
        }

        private static DivisionProfile D(string name, int clubCount) =>
            new DivisionProfile { Name = name, ClubCount = clubCount };
    }
}
