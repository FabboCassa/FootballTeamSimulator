namespace Sim.Core.Generation
{
    /// <summary>
    /// Embedded name pools (cartoonish, Italian-flavoured, no real people or clubs).
    /// Will become loadable data alongside BalanceConfig later if needed.
    /// </summary>
    public static class NameDatabase
    {
        public static readonly string[] FirstNames =
        {
            "Mario", "Luca", "Giorgio", "Paolo", "Marco", "Andrea", "Stefano", "Davide",
            "Matteo", "Simone", "Fabio", "Sandro", "Bruno", "Carlo", "Dario", "Elia",
            "Franco", "Gianni", "Ivan", "Lorenzo", "Nicola", "Oscar", "Pietro", "Rocco",
            "Tommaso", "Ugo", "Valerio", "Walter", "Alessio", "Beppe", "Cesare", "Diego",
            "Enzo", "Furio", "Guido", "Jacopo", "Kevin", "Leo", "Massimo", "Nino",
            "Otto", "Pino", "Quirino", "Remo", "Salvo", "Tito", "Vito", "Zeno"
        };

        public static readonly string[] LastNames =
        {
            "Rossini", "Bianchetti", "Verdone", "Espositi", "Russetti", "Ferraroni", "Colombino", "Riccardi",
            "Marinello", "Grecchi", "Brunetti", "Gallinari", "Contini", "Costanzi", "Fontanella", "Morettini",
            "Rizzoli", "Lombardini", "Baronetto", "Fiorelli", "Santorini", "Marianelli", "Rinaldini", "Amatucci",
            "Gattoni", "Pellegrinetti", "Palombi", "Sartorello", "Faraone", "Caruselli", "Ferrarello", "Gentilini",
            "Battaglini", "Vitalucci", "Martorelli", "Serranova", "Leonetti", "Longobardi", "Marchetti", "Olivetti",
            "Crocetti", "Bellucci", "Tarantino", "Beneventi", "Donatoni", "Sorrentini", "Vaccarella", "Zampino",
            "Trotta", "Scalzone", "Pagliuca", "Mancinelli", "Ottaviani", "Quaresima", "Ravanelli", "Sabbatini",
            "Tognazzi", "Ubaldini", "Vendramini", "Zanetti", "Acquaviva", "Bordignon", "Cardarelli", "DelPiave",
            "Fumagalli", "Garibaldini", "Lanzafame", "Montefiori", "Nervetti", "Pasqualoni", "Roversi", "Stellone"
        };

        public static readonly string[] ClubPrefixes =
        {
            "FC", "AC", "US", "SS", "ASD", "Real", "Sporting", "Atletico", "Olympic", "Dynamo", "Inter", "Virtus"
        };

        public static readonly string[] ClubTowns =
        {
            "Pallonia", "Golturno", "Dribblino", "Scarpetta", "Tacchetto", "Crossanova", "Rigoria", "Traversella",
            "Fuorigioco", "Contropiede", "Tribunella", "Pressingrado", "Catenaccio", "Bandierina", "Cucchiaio", "Tackleto",
            "Rovesciada", "Pareggiola", "Vittoriosa", "Sconfittella", "Rimontella", "Capocannona", "Fischiettino", "Recuperone",
            "Stoppata", "Lancialungo", "Tikitakka", "Melinella", "Zonamista", "Liberopoli", "Trequarti", "Anticipone",
            "Diagonalia", "Sovrapposta", "Velocipede", "Marcaturo"
        };
    }
}
