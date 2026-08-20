using System.Collections.Generic;

namespace Sim.Core.Generation
{
    /// <summary>
    /// The built-in naming cultures (task 11.1). Sixteen flavours cover the nations the roadmap
    /// asks for: the big five, the rest of Europe, South America, North America, Africa, the Gulf
    /// and East Asia.
    ///
    /// EVERY name here is invented. Given names are ordinary and generic; surnames and town names
    /// are deliberately fabricated so no generated club or player can be mistaken for a real one —
    /// the same rule the two-division world already followed, just extended to the whole planet.
    /// The user's plan is that real names arrive later as PLAYER-SUPPLIED MODS, which is why this
    /// is data behind <see cref="WorldGenerationOptions.Cultures"/> rather than hard-wired lookups.
    /// </summary>
    public static class CultureDatabase
    {
        /// <summary>A fresh dictionary of the built-in cultures, keyed by <see cref="NameCulture.Id"/>.</summary>
        public static Dictionary<string, NameCulture> BuiltIn()
        {
            return new Dictionary<string, NameCulture>
            {
                { "english", English() },
                { "american", American() },
                { "spanish", Spanish() },
                { "portuguese", Portuguese() },
                { "italian", Italian() },
                { "french", French() },
                { "german", German() },
                { "dutch", Dutch() },
                { "nordic", Nordic() },
                { "slavic", Slavic() },
                { "aegean", Aegean() },
                { "arabic", Arabic() },
                { "african", African() },
                { "japanese", Japanese() },
                { "korean", Korean() },
                { "chinese", Chinese() },
            };
        }

        private static NameCulture English() => new NameCulture
        {
            Id = "english",
            FirstNames = new[]
            {
                "Alfie", "Archie", "Callum", "Dean", "Eddie", "Finlay", "Gareth", "Harvey", "Isaac", "Jarrod",
                "Keegan", "Liam", "Marcus", "Nathan", "Owen", "Perry", "Quinn", "Reece", "Spencer", "Tobias",
                "Vaughn", "Wesley", "Xander", "Zachary", "Bradley", "Curtis", "Damon", "Elliot"
            },
            LastNames = new[]
            {
                "Ashcombe", "Barlowe", "Cranfield", "Denholm", "Ellwood", "Fairbrass", "Garrick", "Hollingsworth",
                "Ilkeston", "Jarvie", "Kimberley", "Larkspur", "Medlock", "Netherton", "Oakhampton", "Pendlebury",
                "Quarrington", "Ravenscroft", "Stapleford", "Thornbury", "Underhill", "Vellacott", "Wrentham",
                "Yardley", "Bracewell", "Cheshunt", "Draycott", "Fenmore", "Glenholme", "Harkness", "Milburn",
                "Rothwell"
            },
            Towns = new[]
            {
                "Ashenvale", "Barrowden", "Clatterford", "Downholm", "Eastmarch", "Fenwick", "Grimsdale",
                "Harrowgate", "Ivelow", "Kingsmoor", "Larkhaven", "Marshfield", "Northcott", "Oldmere",
                "Pellingham", "Quarrow", "Rookswood", "Stonebeck", "Thistleton", "Uppercross", "Vinemoor",
                "Westerly", "Wychbourne", "Yarrowmead"
            },
            ClubPrefixes = new[]
            {
                "FC", "AFC", "United", "City", "Town", "Rovers", "Athletic", "Wanderers"
            }
        };

        private static NameCulture American() => new NameCulture
        {
            Id = "american",
            FirstNames = new[]
            {
                "Austin", "Brayden", "Chase", "Cody", "Dalton", "Easton", "Grant", "Hunter", "Jaylen", "Kaden",
                "Landon", "Mason", "Nolan", "Parker", "Reid", "Shane", "Trevor", "Tyler", "Wyatt", "Zane",
                "Brandon", "Colton", "Devin", "Elias", "Garrett", "Jonah", "Preston", "Riley"
            },
            LastNames = new[]
            {
                "Alderman", "Brantley", "Caldwell", "Duvall", "Emerson", "Fairchild", "Granger", "Hollister",
                "Ingram", "Jennings", "Kirkland", "Lockhart", "Marsden", "Nolan", "Ogletree", "Pemberton",
                "Quinlan", "Ridgeway", "Sinclair", "Thatcher", "Underwood", "Vandiver", "Whitaker", "Yates",
                "Barrington", "Colefax", "Delacroix", "Fitzgerald", "Halloway", "Merriweather", "Sutherland",
                "Winslow"
            },
            Towns = new[]
            {
                "Ashbury", "Bridgeton", "Cedarcrest", "Dunmore", "Elkhorn", "Fairhaven", "Goldridge", "Harborview",
                "Ironvale", "Junction", "Kestrel", "Lakemont", "Millbrook", "Northgate", "Oakhollow", "Pinecrest",
                "Quarryville", "Redstone", "Silverton", "Timberline", "Union", "Valemont", "Westbrook",
                "Yellowpine"
            },
            ClubPrefixes = new[]
            {
                "FC", "SC", "Union", "Athletic", "Republic", "Dynamo", "Sporting", "Rapids"
            }
        };

        private static NameCulture Spanish() => new NameCulture
        {
            Id = "spanish",
            FirstNames = new[]
            {
                "Adrian", "Bruno", "Cristian", "Diego", "Emilio", "Fernando", "Gonzalo", "Hector", "Ignacio",
                "Javier", "Leandro", "Mateo", "Nicolas", "Octavio", "Pablo", "Rodrigo", "Santiago", "Tomas",
                "Ulises", "Valentin", "Ximeno", "Alonso", "Benito", "Cesar", "Damian", "Ezequiel", "Facundo",
                "Gaston"
            },
            LastNames = new[]
            {
                "Alcantara", "Bermudez", "Carrizo", "Delgadillo", "Escalante", "Fuentealba", "Guzman", "Hurtado",
                "Izaguirre", "Jauregui", "Ledesma", "Manrique", "Navarrete", "Olmedo", "Peralta", "Quintanilla",
                "Reinoso", "Sandoval", "Tabares", "Urrutia", "Valdivieso", "Zambrano", "Arriaga", "Cabrera",
                "Duarte", "Espinar", "Galarza", "Herrera", "Montalvo", "Nieves", "Robledo", "Salcedo"
            },
            Towns = new[]
            {
                "Altamira", "Bellavista", "Campoverde", "Doradillo", "Encinares", "Fuentelmonte", "Granalta",
                "Higueras", "Islabaja", "Jaraval", "Lomanegra", "Miralcampo", "Nogalar", "Olivares", "Penalta",
                "Quebrada", "Riosalto", "Sierrablanca", "Torrelinda", "Valdesierra", "Ventanas", "Yeguada",
                "Zafrilla", "Cerroazul"
            },
            ClubPrefixes = new[]
            {
                "CF", "CD", "Real", "Atletico", "Deportivo", "Union", "Racing", "Sporting"
            }
        };

        private static NameCulture Portuguese() => new NameCulture
        {
            Id = "portuguese",
            FirstNames = new[]
            {
                "Anderson", "Bruno", "Caio", "Danilo", "Edilson", "Fabricio", "Gilberto", "Hernani", "Ivo",
                "Joaquim", "Leonardo", "Murilo", "Nelinho", "Otavio", "Paulinho", "Rafael", "Sergio", "Thiago",
                "Vinicius", "Wagner", "Alexandre", "Benedito", "Cassio", "Douglas", "Everaldo", "Gustavo", "Josue",
                "Rodolfo"
            },
            LastNames = new[]
            {
                "Albuquerque", "Bittencourt", "Carvalhais", "Damasceno", "Estevao", "Figueiredo", "Goncalvez",
                "Henriques", "Itamar", "Junqueira", "Lacerda", "Marinho", "Nascimento", "Oliveiral", "Paiva",
                "Queiroga", "Rezende", "Siqueira", "Tavares", "Uchoa", "Valadares", "Xavier", "Zamith",
                "Bernardes", "Corveiro", "Delfino", "Fontoura", "Guimaral", "Machadinho", "Nogueiral", "Pontelli",
                "Serrado"
            },
            Towns = new[]
            {
                "Alvorada", "Boavila", "Caissara", "Douradinho", "Estrelinha", "Formosinha", "Guaraval", "Itapema",
                "Jaborandi", "Lagoinha", "Morrobelo", "Novagua", "Ouroverde", "Pedralta", "Quintal", "Riachinho",
                "Serrinha", "Tijucal", "Uruacu", "Varjota", "Xaruma", "Barrancal", "Coqueiral", "Palmelo"
            },
            ClubPrefixes = new[]
            {
                "SC", "EC", "Clube", "Uniao", "Atletico", "Gremio", "Nacional", "Portuaria"
            }
        };

        private static NameCulture Italian() => new NameCulture
        {
            Id = "italian",
            FirstNames = new[]
            {
                "Alessio", "Bruno", "Corrado", "Davide", "Emanuele", "Fabrizio", "Gianluca", "Ivano", "Lorenzo",
                "Massimo", "Nicolo", "Orlando", "Pierluigi", "Riccardo", "Samuele", "Tiziano", "Umberto",
                "Valerio", "Alberto", "Cristiano", "Domenico", "Ettore", "Federico", "Giacomo", "Leonardo",
                "Michele", "Ruggero", "Sandro"
            },
            LastNames = new[]
            {
                "Amatucci", "Barbagallo", "Cavalieri", "Delfrate", "Ercolani", "Fiorentini", "Gallinaro",
                "Iachini", "Lombardo", "Mazzanti", "Novelli", "Ottaviano", "Palmieri", "Quaranta", "Ricciardi",
                "Serafini", "Tortorella", "Uberti", "Vallesi", "Zampieri", "Bandinelli", "Colacino", "Durighello",
                "Fontanaro", "Grimaldi", "Maroncelli", "Peluso", "Ronchetti", "Salvemini", "Trevisan", "Venturato",
                "Zoccola"
            },
            Towns = new[]
            {
                "Altavilla", "Borgorosso", "Castelmare", "Dolcedico", "Ellerino", "Fontanelle", "Grottalta",
                "Isolabella", "Lucignana", "Montesolo", "Nocedoro", "Olivastro", "Pietrachiara", "Quarnera",
                "Roccalbina", "Sassoverde", "Torreluna", "Ulivara", "Valmontana", "Zafferana", "Bellacosta",
                "Corvara", "Melograno", "Praticello"
            },
            ClubPrefixes = new[]
            {
                "FC", "AC", "US", "SS", "Virtus", "Real", "Atletico", "Pro"
            }
        };

        private static NameCulture French() => new NameCulture
        {
            Id = "french",
            FirstNames = new[]
            {
                "Adrien", "Baptiste", "Cedric", "Damien", "Etienne", "Florian", "Gaetan", "Hugo", "Jerome",
                "Kevin", "Loic", "Mathis", "Nicolas", "Olivier", "Pierrick", "Quentin", "Romain", "Sylvain",
                "Thibault", "Ugo", "Valentin", "Xavier", "Yannick", "Aurelien", "Bastien", "Corentin", "Emeric",
                "Fabien"
            },
            LastNames = new[]
            {
                "Aubertin", "Beaulieu", "Chauvelin", "Delacourt", "Estivals", "Fournelle", "Gauthiez", "Hardouin",
                "Imbert", "Jouvenel", "Lambertin", "Marchandeau", "Noirot", "Ollivier", "Perrenoud", "Quesnel",
                "Rouvier", "Sabatier", "Thevenot", "Urbain", "Vaillancourt", "Wattelier", "Bonnefoy", "Cazenave",
                "Duchemin", "Fressange", "Gaubert", "Lavernier", "Montfleury", "Rochefort", "Sansonnet",
                "Vercoutre"
            },
            Towns = new[]
            {
                "Aubevigne", "Belfontaine", "Chateaulys", "Dompierre", "Ecluseval", "Fontenoy", "Grandbourg",
                "Hautecombe", "Isleneuve", "Joncourt", "Lavardun", "Montclair", "Neuveville", "Orsanne",
                "Pontarlieu", "Quercy", "Rochevert", "Sablonne", "Tourvieille", "Ussanne", "Valcourt", "Vieuxpont",
                "Yvrandes", "Beauclair"
            },
            ClubPrefixes = new[]
            {
                "FC", "AS", "RC", "Olympique", "Stade", "Union", "Racing", "Sporting"
            }
        };

        private static NameCulture German() => new NameCulture
        {
            Id = "german",
            FirstNames = new[]
            {
                "Andreas", "Bastian", "Christoph", "Dennis", "Erik", "Fabian", "Gregor", "Hendrik", "Ingo",
                "Jannik", "Kilian", "Lennart", "Marius", "Nils", "Oliver", "Philipp", "Rainer", "Sebastian",
                "Torben", "Ulrich", "Valentin", "Wolfgang", "Yannick", "Bernd", "Clemens", "Dietmar", "Frank",
                "Gerrit"
            },
            LastNames = new[]
            {
                "Achterberg", "Baumgartl", "Cronenberg", "Deichmann", "Ebersbach", "Fahrenkamp", "Gierlichs",
                "Hohenstein", "Illgner", "Jaschke", "Kirchhoff", "Lindhorst", "Muehlbauer", "Nordhausen",
                "Oberlander", "Pfeiffenberg", "Quandt", "Riedinger", "Steinbrecher", "Trautwein", "Uhlenbrock",
                "Vollmering", "Wendhausen", "Ziegenbein", "Bergmeier", "Dornbusch", "Falkenrath", "Grunewald",
                "Hasselbach", "Kaltenbrunn", "Rothenberg", "Weissmantel"
            },
            Towns = new[]
            {
                "Adlerbach", "Buchenstein", "Dornhausen", "Eichenfeld", "Falkenau", "Grunwalde", "Hainberg",
                "Ilmenstadt", "Kaltenau", "Lindenthal", "Moorbrunn", "Nebelstein", "Ostheide", "Pappelfurt",
                "Quellbach", "Rehsteig", "Sonnenfels", "Tannenau", "Uferheim", "Vogelsang", "Wiesenbrunn",
                "Zellerhof", "Birkenmoor", "Steinhorst"
            },
            ClubPrefixes = new[]
            {
                "FC", "SV", "TSV", "VfB", "SC", "Borussia", "Eintracht", "Union"
            }
        };

        private static NameCulture Dutch() => new NameCulture
        {
            Id = "dutch",
            FirstNames = new[]
            {
                "Bram", "Cas", "Daan", "Erwin", "Ferdi", "Gijs", "Hidde", "Jelle", "Koen", "Lars", "Maarten",
                "Niels", "Olivier", "Pepijn", "Quinten", "Ruben", "Sander", "Thijs", "Vincent", "Wouter",
                "Bastiaan", "Dirk", "Eelco", "Freek", "Guus", "Joost", "Marnix", "Sjoerd"
            },
            LastNames = new[]
            {
                "Aalbers", "Boermans", "Cuijpers", "Doornbos", "Elshout", "Feenstra", "Grondsma", "Hoogland",
                "Ijsselmuiden", "Jonkhout", "Kruithof", "Lubbers", "Meulenbelt", "Nieuwenhuis", "Oosterling",
                "Pothoven", "Quist", "Rijkaards", "Slingerland", "Terlouw", "Uijlenbroek", "Verschoor", "Wielinga",
                "Zoetermeer", "Boskamp", "Dijkgraaf", "Haverkamp", "Meerburg", "Roozendaal", "Stuiver",
                "Vlietstra", "Wolthuis"
            },
            Towns = new[]
            {
                "Aalderveen", "Bergwijk", "Culemhoek", "Doornbeek", "Eikenbos", "Vlietdam", "Gaastmeer",
                "Hoogduin", "Ijsselveen", "Kampenzand", "Lindehorst", "Meerdijk", "Nieuwsloot", "Oudewaard",
                "Polderzicht", "Rietvoorn", "Schelphaven", "Terwolde", "Uithoorn", "Veenkade", "Waterschans",
                "Zandvliet", "Bloemhorst", "Duinkerk"
            },
            ClubPrefixes = new[]
            {
                "FC", "SC", "VV", "AZ", "Sparta", "DVV", "Go", "Excelsior"
            }
        };

        private static NameCulture Nordic() => new NameCulture
        {
            Id = "nordic",
            FirstNames = new[]
            {
                "Anders", "Birger", "Casper", "Emil", "Fredrik", "Gunnar", "Halvor", "Ivar", "Jesper", "Kasper",
                "Lasse", "Mads", "Nikolai", "Oskar", "Petter", "Rasmus", "Sigurd", "Torbjorn", "Ulf", "Vegard",
                "Aksel", "Bjarne", "Eskil", "Hakon", "Joakim", "Leif", "Sindre", "Trygve"
            },
            LastNames = new[]
            {
                "Aagaard", "Bjornstad", "Dahlgren", "Ekstrand", "Falkenberg", "Gullbrandsen", "Halvorsen",
                "Ingebrigtsen", "Jakobsen", "Kvistad", "Lindqvist", "Molvaer", "Nordahl", "Ostberg", "Pettersen",
                "Ranheim", "Sandberg", "Thorvaldsen", "Ulriksen", "Vasstrand", "Wallenius", "Ytterberg",
                "Aslaksen", "Berntsen", "Dahlstrom", "Engevik", "Fjeldstad", "Grimstad", "Holmgren", "Kolstad",
                "Lundvik", "Sorensen"
            },
            Towns = new[]
            {
                "Aalvik", "Bjornstad", "Dalfors", "Eikhamn", "Fjellvang", "Granholt", "Havstrand", "Isvik",
                "Kvernnes", "Lindfors", "Myrdalen", "Nordvik", "Ostvang", "Rennedal", "Skogvik", "Storhavn",
                "Tjornevik", "Ulvedal", "Vikstrand", "Ytterhavn", "Bergfjord", "Granvik", "Molleby", "Sjodal"
            },
            ClubPrefixes = new[]
            {
                "IF", "IK", "BK", "FK", "SK", "Idraetts", "Boldklub", "United"
            }
        };

        private static NameCulture Slavic() => new NameCulture
        {
            Id = "slavic",
            FirstNames = new[]
            {
                "Andrej", "Bogdan", "Dragan", "Emil", "Filip", "Goran", "Ivan", "Jaroslav", "Kamil", "Lubomir",
                "Milos", "Nikola", "Ondrej", "Pavel", "Radek", "Stanislav", "Tomasz", "Vladan", "Wojciech",
                "Zdenek", "Bartosz", "Dusan", "Grzegorz", "Igor", "Krystian", "Marek", "Sasha", "Vitali"
            },
            LastNames = new[]
            {
                "Andrusiak", "Bielawski", "Cvetkovic", "Dobrovolny", "Emelyanov", "Filipovic", "Gorczyca",
                "Hlavaty", "Ivanenko", "Jankovic", "Kowalczyk", "Lisitsyn", "Mihajlovic", "Novotny", "Obradovic",
                "Pietrzak", "Radosavlje", "Stankevich", "Tomaszek", "Urbanek", "Vukovic", "Wisniewski", "Zaremba",
                "Bogdanov", "Cernik", "Dragunov", "Gorbachuk", "Kaminski", "Lazarevic", "Petrovic", "Sokolovski",
                "Zielinski"
            },
            Towns = new[]
            {
                "Belogorsk", "Cerniva", "Dobrina", "Elovka", "Gradec", "Hrastnik", "Ivanice", "Jasenov",
                "Kamenica", "Lipova", "Mokrany", "Novigrad", "Ostrovna", "Podgora", "Ruzin", "Slatina",
                "Trnovec", "Uzhgrad", "Velenica", "Wislany", "Zorec", "Brezovo", "Dubrava", "Milokrad"
            },
            ClubPrefixes = new[]
            {
                "FK", "NK", "MFK", "Slavia", "Dynamo", "Lokomotiv", "Zenit", "Partizan"
            }
        };

        private static NameCulture Aegean() => new NameCulture
        {
            Id = "aegean",
            FirstNames = new[]
            {
                "Alexios", "Baris", "Christos", "Dimitris", "Emre", "Fatih", "Giorgos", "Hakan", "Ilias", "Kerem",
                "Lefteris", "Mehmet", "Nikos", "Onur", "Panagiotis", "Rifat", "Serkan", "Tasos", "Umut", "Vasilis",
                "Yusuf", "Andreas", "Burak", "Cengiz", "Kostas", "Levent", "Stelios", "Thanasis"
            },
            LastNames = new[]
            {
                "Adamopoulos", "Balaban", "Christodoulou", "Demirtas", "Ergenekon", "Fotiadis", "Gunaydin",
                "Hatzidakis", "Ioannidis", "Karaduman", "Lambrakis", "Mavridis", "Nikolaidis", "Ozdemir",
                "Papazoglou", "Rizopoulos", "Sarikaya", "Tsakalidis", "Uzuner", "Vlachopoulos", "Yildirimoglu",
                "Anastasiou", "Bozkurt", "Chatzikos", "Erdogmus", "Kalaitzis", "Sipahi", "Tunaboylu",
                "Vasileiadis", "Yalcinkaya", "Zervas", "Doganay"
            },
            Towns = new[]
            {
                "Akropoli", "Beylerbey", "Chrysokambos", "Dagkoy", "Elmali", "Fenerdere", "Gulhanli", "Halkidos",
                "Iznikli", "Kalamoni", "Limnovouni", "Marmarali", "Neapoli", "Ovakoy", "Pelasgia", "Rodovouni",
                "Sariyayla", "Thermaia", "Uzuncay", "Vrachori", "Yesilkoy", "Zografou", "Dereboyu", "Kavalos"
            },
            ClubPrefixes = new[]
            {
                "AS", "PAS", "GS", "Spor", "Kulubu", "Enosis", "Aetos", "Panathinos"
            }
        };

        private static NameCulture Arabic() => new NameCulture
        {
            Id = "arabic",
            FirstNames = new[]
            {
                "Adel", "Bilal", "Chakib", "Driss", "Elyas", "Farid", "Ghassan", "Hicham", "Idris", "Jamal",
                "Karim", "Lotfi", "Mahdi", "Nabil", "Omar", "Qasim", "Rachid", "Sofiane", "Tarek", "Walid",
                "Yassin", "Zakaria", "Anouar", "Badr", "Fouad", "Hamza", "Nizar", "Sami"
            },
            LastNames = new[]
            {
                "Abbadi", "Belkacem", "Chaouki", "Doukkali", "Elhamdi", "Fassi", "Ghazali", "Hariri", "Idrissi",
                "Jaziri", "Kettani", "Lahlou", "Mansouri", "Naciri", "Ouazzani", "Qadiri", "Rahmouni", "Saidani",
                "Tazi", "Ubaidi", "Wahbi", "Yacoubi", "Zerouali", "Benjelloun", "Cherkaoui", "Dahmani", "Fakhouri",
                "Kadiri", "Marrakchi", "Nouri", "Sebbagh", "Ziani"
            },
            Towns = new[]
            {
                "Ainzahra", "Babelkheir", "Dararrif", "Elkhalij", "Fajrabad", "Gharbiya", "Hadidoun", "Ibnrachad",
                "Jazirat", "Kasbatel", "Lawziya", "Mahdiyat", "Nakhilat", "Oualidat", "Qantara", "Rimalat",
                "Sahelia", "Tafoukt", "Ummlail", "Wadinour", "Yasmina", "Zaytouna", "Barrhamma", "Nourabad"
            },
            ClubPrefixes = new[]
            {
                "Al", "Ittihad", "Nadi", "Shabab", "Wydad", "Hilal", "Ahli", "Raja"
            }
        };

        private static NameCulture African() => new NameCulture
        {
            Id = "african",
            FirstNames = new[]
            {
                "Abiola", "Bakary", "Chinedu", "Dembo", "Emeka", "Fofana", "Gbenga", "Hakeem", "Ibrahima",
                "Jelani", "Kwabena", "Lamine", "Mamadou", "Ndiaye", "Obinna", "Papis", "Rashid", "Sekou", "Tunde",
                "Uche", "Yaya", "Zoumana", "Amadou", "Cheikh", "Kofi", "Musa", "Ousmane", "Sadio"
            },
            LastNames = new[]
            {
                "Adebayor", "Bakayoko", "Chukwuemeka", "Diallo", "Ekpenyong", "Fadiga", "Gyasi", "Haruna", "Ibeh",
                "Jallow", "Kouyate", "Lamptey", "Mensah", "Ngassa", "Okonkwo", "Ouedraogo", "Ramazani", "Sanogo",
                "Traore", "Ubuka", "Wanyama", "Yeboah", "Zoungrana", "Bamba", "Coulibaly", "Dossou", "Fofie",
                "Kanoute", "Mwepu", "Nwachukwu", "Sissoko", "Toure"
            },
            Towns = new[]
            {
                "Adjame", "Bomaka", "Chibuzo", "Dakarou", "Enugwu", "Fanteland", "Gbadolite", "Harmattan",
                "Ikorodu", "Jamestown", "Kumasa", "Lokoja", "Mbabala", "Ngoroko", "Owerria", "Pikinou", "Quelimba",
                "Rufisqua", "Sokodou", "Tamalea", "Ubundu", "Volta", "Wangara", "Zaria"
            },
            ClubPrefixes = new[]
            {
                "FC", "AS", "Stade", "Union", "Kotoko", "Enyimba", "Espoir", "Etoile"
            }
        };

        private static NameCulture Japanese() => new NameCulture
        {
            Id = "japanese",
            FirstNames = new[]
            {
                "Akira", "Daiki", "Eiji", "Fumiya", "Genki", "Haruto", "Isamu", "Junpei", "Kaito", "Makoto",
                "Naoki", "Osamu", "Ren", "Shota", "Takumi", "Yuji", "Yuto", "Kenta", "Ryusei", "Souta", "Tatsuya",
                "Wataru", "Yamato", "Hiroto", "Kazuki", "Masaru", "Nobuo", "Shinji"
            },
            LastNames = new[]
            {
                "Amagase", "Bandou", "Chikamatsu", "Doihara", "Endoji", "Fujisaka", "Godaira", "Hasegawa",
                "Iwasato", "Jinnouchi", "Kamiyama", "Matsubuchi", "Nagatomi", "Okazato", "Sakaguchi", "Tachibana",
                "Ueshima", "Wakamiya", "Yamashiro", "Zaimoku", "Hoshikawa", "Isayama", "Kurihara", "Miyazato",
                "Nakagome", "Ogasawa", "Serizawa", "Toyokawa", "Uehira", "Yasumoto", "Kitajima", "Morisaki"
            },
            Towns = new[]
            {
                "Aokidai", "Byakurin", "Chidorigawa", "Fuyuhara", "Ginzato", "Hoshimura", "Ichinose", "Kaminoki",
                "Marugahama", "Natsukaze", "Okuyanagi", "Rindouji", "Sakuragaoka", "Takanashi", "Umizaki",
                "Wakabadai", "Yamabuki", "Yukinohara", "Asahigawa", "Kirinodai", "Momijino", "Shioyama",
                "Tsukigase", "Nanagawa"
            },
            ClubPrefixes = new[]
            {
                "FC", "Sporting", "Reysol", "Frontale", "Antlers", "Vissel", "Sanga", "Ardija"
            }
        };

        private static NameCulture Korean() => new NameCulture
        {
            Id = "korean",
            FirstNames = new[]
            {
                "Byungho", "Changmin", "Dohyun", "Eunwoo", "Gunwoo", "Hyunwoo", "Jaesung", "Junho", "Kihoon",
                "Minjae", "Namgil", "Sanghyun", "Seokjin", "Taeyang", "Woojin", "Yeonjun", "Youngho", "Jihoon",
                "Seungmin", "Doyoon", "Hanbin", "Jinwoo", "Kyungsoo", "Minseok", "Sangwoo", "Taehyun", "Wonjae",
                "Yoonseo"
            },
            LastNames = new[]
            {
                "Ahn", "Baek", "Cheon", "Doh", "Eom", "Gwak", "Hwangbo", "Im", "Jang", "Kang", "Koo", "Lim",
                "Moon", "Nam", "Ok", "Pyo", "Ryu", "Seok", "Shim", "Sohn", "Tak", "Uhm", "Wi", "Yang", "Yeom",
                "Yoo", "Byun", "Chae", "Gil", "Heo", "Jeon", "Roh"
            },
            Towns = new[]
            {
                "Baekhwa", "Chunhae", "Dongmyeong", "Eunpyeong", "Garam", "Haneul", "Ilsando", "Jangpo",
                "Keumgang", "Mirae", "Nampo", "Okcheon", "Pyeongan", "Ryeongsan", "Sanho", "Taebaek", "Ubong",
                "Wonhae", "Yeonpo", "Areum", "Bomun", "Cheongna", "Dalseo", "Hwarang"
            },
            ClubPrefixes = new[]
            {
                "FC", "United", "Bluewings", "Dragons", "Citizen", "Ilbo", "Sportif", "Ilhwa"
            }
        };

        private static NameCulture Chinese() => new NameCulture
        {
            Id = "chinese",
            FirstNames = new[]
            {
                "Bingwen", "Chenglei", "Deming", "Fenghua", "Guoliang", "Haoran", "Jianguo", "Kunpeng", "Lianjie",
                "Mingzhe", "Ningyuan", "Peiyu", "Qiangwei", "Renshu", "Shaoqing", "Tianyu", "Weiming", "Xiaolong",
                "Yicheng", "Zhenhua", "Boqin", "Congyu", "Delun", "Junjie", "Longwei", "Ruilin", "Shunfeng",
                "Yuanhang"
            },
            LastNames = new[]
            {
                "Bai", "Cao", "Chai", "Deng", "Dou", "Fang", "Gao", "Geng", "Han", "Hou", "Jiang", "Kong", "Lai",
                "Lei", "Meng", "Mou", "Nie", "Pang", "Qiao", "Rong", "Shen", "Sui", "Tan", "Teng", "Wan", "Xiang",
                "Yin", "Yue", "Zang", "Zhai", "Zhuo", "Zou"
            },
            Towns = new[]
            {
                "Anlin", "Baishan", "Changhe", "Dongping", "Fenglin", "Guangyuan", "Heping", "Jinshui", "Kaiyang",
                "Lianhua", "Mingzhou", "Nanxi", "Pinghu", "Qinglong", "Runcheng", "Shuangqiao", "Taiyun",
                "Wanning", "Xinghai", "Yulin", "Zhaoyang", "Beitang", "Chengxi", "Longtan"
            },
            ClubPrefixes = new[]
            {
                "FC", "Zuqiu", "Guolin", "Shenlong", "Dongfang", "Zhongxing", "Tiancheng", "Haiyun"
            }
        };
    }
}
