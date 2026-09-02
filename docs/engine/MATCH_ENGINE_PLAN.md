# Rifacimento del motore partita — diagnosi e piano

Stato: piano approvato il 2026-09-02. Causalità invertita (il campo decide), lavorazione a fasi
con il gioco sempre funzionante, regolamento fino a falli/cartellini/punizioni.

- [x] **Fase 0 — banco di prova** (2026-09-02). Vedi §7 per la misura di partenza.
- [ ] Fase 1 — unità e base temporale
- [ ] Fase 2 — forma: formazione e blocco
- [ ] Fase 3 — difendere: zona e trigger
- [ ] Fase 4 — decisioni con la palla
- [ ] Fase 5 — il regolamento
- [ ] Fase 6 — inversione della causalità
- [ ] Fase 7 — dati sulle prestazioni
- [ ] Fase 8 — le istruzioni contano

---

## 📍 Stato — 2 settembre 2026

**Siamo qui: Fase 0 chiusa e verificata end-to-end. La prossima è la Fase 1 — unità e base
temporale, cioè l'1,3 km per giocatore che deve diventare 10.**

| | Fase | Stato |
|---|---|---|
| 0 | banco di prova | ✅ fatta **e verificata dall'utente** |
| 1 | unità e base temporale | ⬅️ **prossima** |
| 2-8 | — | da fare |

### Come si verifica che tutto gira

    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html
    dotnet test

### Verificato dall'utente il 2 settembre 2026

- **`balance.ps1 -Scenario pitch`** — compila (l'harness tira dentro `server/Infrastructure`, quindi
  la build vera non è banale) e gira in 4,6 s per 200 partite, 22,9 ms/partita. Esito: 6 letture su
  19 dentro la banda del calcio vero, 2 check su 3 verdi. **L'exit code 1 è atteso**: il check rosso
  è "a held ball is never sitting on a line of the pitch", cioè la misura del bug del §1.7, non un
  guasto del banco.
- **`dotnet test`** — 574/574 verdi in 212,7 s (erano 599,5 s con 2 rossi).
- **Determinismo confermato fra runtime**: la misura di partenza esce identica cifra per cifra su
  .NET 8/Linux e .NET 10/Windows.
- **Il taglio dei tempi non ha spostato un solo risultato**: nella run dell'utente `[sweep]`,
  `[match-fatigue]`, `[positioning-exploit]`, `[positioning-line]`, `[fitness->result]`, i
  `Golden values` e `[DeterminismCheck] 0xBD336A9B5F155792` sono tutti identici a prima della modifica.

### Note operative

- **`generatePositions: false` per ogni partita che nessuno guarderà.** Il flusso di movimento costa
  96 ms/partita in Debug contro 0,54 ms del modello di risultato: 180 volte tanto. Era questo, da
  solo, a rendere `dotnet test` una faccenda da dieci minuti. Alla Fase 1 il costo sale di ~×50.
- **Lo scenario `pitch` non fa parte di `-Scenario all`** ed esce con 1 di proposito. Non è una
  regressione da inseguire: è il divario da chiudere. Le bande diventano check veri solo con
  `--pitch-strict`, che ogni fase accende sulle bande che dichiara chiuse.
- **`dotnet test` resta a 212 s.** I fixture di identità (`PrematchPlanTests`, `MatchResimTests`,
  `MatchEngineTests`) tengono il flusso acceso apposta, perché `MatchReportHasher` lo include
  nell'hash; il resto è generazione di mondi (25.944 giocatori per lo scouting, 10.120 per le
  valutazioni). Se serve scendere ancora: marcare `[Category("Slow")]` le misure di bilanciamento
  travestite da test e girare `dotnet test --filter TestCategory!=Slow`. **Non ancora fatto.**
- **Decisione aperta:** `server/Infrastructure/Leagues/MatchResolver.Resolve` costruisce il motore
  senza argomenti, quindi genera il flusso per ogni partita di lega risolta lato server. Se quei
  report servono da replay va bene; se no sono ~96 ms di CPU buttati a partita. **Da decidere.**
- **Residuo da cancellare a mano:** `_to_delete/stage/simcore.tar.gz` (207 KB), usato per portare i
  sorgenti nel container.
- Due test erano rossi e **non** per colpa della Fase 0: erano bug del test nel rifacimento del
  movimento in corso. Corretti, spiegati in §7.

---

## 1. La diagnosi

### 1.1 Il problema di fondo: la partita che guardi non è la partita che conta

`MatchEngine.Simulate()` decide punteggio ed eventi con un modello statistico a minuti
(`ActionChancePerMinute`, `GoalCoefficient`, `r^ChanceSharpness`). Solo **dopo**,
`MatchSimulator` genera 22 agenti che si muovono, e `MatchDirector` li piega perché la palla
arrivi al marcatore giusto al minuto giusto.

Il codice lo dichiara apertamente: *"Presentation only — nothing here can touch a score."*

Conseguenza diretta di quello che chiedi: **è impossibile capire dal campo se la tattica
funziona**, perché il campo non produce nulla. La tattica muove il risultato altrove
(`TacticModifiers`), e quello che vedi è una recita costruita a posteriori.

Peggio: per far arrivare la palla al marcatore designato, il director accende dei
super-poteri temporanei sulla squadra che "deve" segnare, per 45 tick prima di ogni occasione:

| Knob | Valore | Significato |
|---|---|---|
| `ChancePressPercent` | 260 | raggio di pressing ×2,6 |
| `ChanceTacklePercent` | 260 | probabilità di tackle ×2,6 |
| `ChanceForwardPercent` | 220 | fame di verticalizzazione ×2,2 |
| `ChanceReachBonusDm` | +55 | 5,5 m di raggio in più sulla palla vagante |
| `ChanceDriveShiftPermille` | +110 | blocco 11 m più alto |

E se nonostante tutto la palla non arriva, `_forcedShots` la consegna in mano all'attaccante.
Ecco perché i movimenti sembrano casuali: **lo sono**, nel senso che non rispondono a una
logica di gioco ma a una scadenza da rispettare.

### 1.2 Il modello non è dimensionalmente coerente

`TicksPerMinute = 12` → **1 tick = 5 secondi di partita**. Con quella base:

- velocità giocatore: `10 + 10*Pace/100` dm/tick ≈ 17-20 dm/tick, ×1,7 in sprint ≈ 3,4 m
  ogni 5 secondi = **0,7 m/s**. Un calciatore sprinta a 8 m/s.
- velocità palla: `MaxPassForce = 60` dm/tick = 6 m ogni 5 s = **1,2 m/s**. Un passaggio
  viaggia a 15-25 m/s.
- rapporto palla/giocatore ≈ **2:1**. Nella realtà è **3-4:1**.

Quel rapporto è *l'unica cosa che conta* per la geometria del calcio: quanto spazio copre un
difensore mentre la palla viaggia decide se un passaggio è intercettabile, se il pressing
arriva, se lo spazio alle spalle è attaccabile. Con il rapporto sbagliato, **nessuna
calibrazione può produrre calcio**. È la radice tecnica di "non ha molto senso".

### 1.3 La forma della squadra è sbagliata all'origine

`FormationGeometry.AnchorY()` distribuisce **ogni gruppo di ruolo separatamente su tutta la
larghezza**. In un 4-3-3:

| Ruolo | Anchor Y (permille) | In metri |
|---|---|---|
| CB, CB | 250 / 750 | 17 m e 51 m → **34 m fra i due centrali** |
| FB, FB | 118 / 882 | 8 m e 60 m |
| CM ×3 | 250 / 500 / 750 | 17 / 34 / 51 m |

La difesa a 4 esce come `FB 8 · CB 17 · CB 51 · FB 60`: due centrali a 34 metri l'uno
dall'altro con una voragine in mezzo. Non è mai una linea, è un sorriso.

### 1.4 Il blocco collassa sulla palla

In `HomeSpot()`, ultimo passaggio:

    py += (_ball.Y - py) * BlockBallShiftPercent / 100;   // 25%

È una **lerp per giocatore verso la Y della palla, senza tetto**. Chi è lontano dalla palla si
sposta di più di chi è vicino → la squadra non trasla, **si comprime nella corsia della palla**.
Nel video: nessuno nel terzo inferiore del campo, ventidue giocatori in una fascia orizzontale.
Lo scorrimento vero è una traslazione dell'intero blocco, limitata a ~8-12 m.

### 1.5 Marcatura a uomo a tutto campo

`AssignMarks()` assegna un marcatore **a ogni giocatore di movimento avversario, ovunque, sempre**.
Dieci duelli individuali che vagano per il campo. E `Separate()` respinge **solo i compagni**, mai
gli avversari — quindi il marcatore finisce letteralmente sopra il suo uomo.

È esattamente quello che si vede: coppie rosso/blu sovrapposte che si spostano insieme senza
struttura. Il calcio è zonale: si marca uno spazio, si aggredisce la palla, si prendono le
consegne dentro la propria zona.

### 1.6 Nessun attributo conta, tranne Pace

Nella simulazione visiva l'unico attributo letto è `Attributes.Pace`. Passaggio, Tecnica,
Visione, Tackle, Resistenza, Freddezza: **non esistono**. E i passaggi non falliscono mai —
`TryPass()` sceglie una linea matematicamente sicura e `Kick()` la esegue esatta. L'unico modo
per perdere palla è un lancio di dado del 14% per tick.

Quindi la domanda "quali giocatori rendono meglio o peggio" **non ha una risposta possibile**:
non c'è niente in cui un giocatore forte possa essere migliore.

### 1.7 Il regolamento è quasi assente

Verificato con grep su tutto il repo:

- **fuorigioco: la parola non compare da nessuna parte.** Non esiste.
- **palla fuori a giocatore in possesso: mai rilevata.** `ResolveOutOfPlay()` inizia con
  `if (_ball.Dead || !_ball.Free) return;`. Un portatore può correre lungo la linea laterale
  all'infinito: viene solo clampato dentro il campo da `ClampX/ClampY`. Nessuna rimessa.
- niente falli, cartellini, punizioni, rigori.
- **niente cambio campo all'intervallo**: le squadre attaccano la stessa porta per 90 minuti.
- palla senza altezza: un cross e un filtrante sono lo stesso oggetto.

### 1.8 Difetti minori ma visibili

- `FindSupportSpot()` calcola **un solo punto per squadra**: tutti i giocatori di supporto
  corrono nello stesso posto e si accatastano.
- `Carry()` è un tocco fisso di 8 m ogni 4 tick, senza duello → il feed del video mostra
  *"Tortorella salta l'uomo"* quattro volte di fila.
- `AssignMarks()` alloca `new bool[_n]` **a ogni tick di ogni squadra** — spazzatura GC in un
  ciclo caldo.
- Nessuna statistica individuale: `MatchReport` ha solo `HomeGoals`, `AwayGoals`, `Events`.

---

## 2. Cosa dice lo stato dell'arte

Ricerca fatta prima di decidere se scrivere un motore nuovo, adottarne uno o riparare questo.

**Non esiste un motore riusabile che vada bene.** I candidati:

- **RoboCup 2D Soccer Simulation Server** — il riferimento accademico da 30 anni per la
  simulazione 2D deterministica. Modello a cicli discreti, fisica velocità+decadimento, modulo
  arbitro con fuorigioco. Architetturalmente è *esattamente* la forma giusta, ma è C++,
  client/server via UDP, e modella robot con visione parziale e rumore sensoriale: inadatto da
  incorporare, prezioso come modello concettuale.
- **Google Research Football** — ambiente Python/C++ per RL, completo di regolamento. Stessa
  storia: ottimo riferimento, impossibile da mettere dentro Unity/IL2CPP con determinismo intero.
- **open-football (Rust)**, **openengine**, **Openfoot Manager** — progetti aperti, nessuno con
  un modello di movimento più maturo del vostro.
- **Football Manager 26** ha appena rifatto proprio questo: *positional play*, movimento più
  vero, autenticità della partita. Conferma che è il punto in cui si gioca la credibilità.

**Conclusione: si tiene questo motore e lo si rifà dall'interno.** Le fondamenta sono buone e
sono la parte costosa da riscrivere:

- matematica intera in sottounità (`U`, sedicesimi di decimetro) → determinismo bit-identico su
  .NET, Mono e IL2CPP;
- una sola sorgente RNG seminata, ordine di iterazione fisso;
- `PositionStream` piatto e serializzabile, rigenerabile da (seed + formazioni);
- geometria condivisa fra schermata tattica e campo (`FormationGeometry`);
- separazione client/server già pulita, harness di bilanciamento già esistente
  (`tools/BalanceHarness`) con pattern scenario + PASS/FAIL.

Il modello di partenza (Buckland, *Programming Game AI by Example*, cap. 4 "Simple Soccer") è
corretto ma è un giocattolo didattico: stati semplici, steering, un cervello di squadra
elementare. Va portato al livello successivo — blocco, linee, zone, duelli, arbitro — che è
quello che RoboCup e FM implementano.

---

## 3. L'architettura d'arrivo

    ┌─ Partite che qualcuno guarda (utente, ranked, lega) ──────────────┐
    │  MatchSimulator  →  gol, tiri, eventi, statistiche individuali    │
    │  10 Hz, 22 agenti, arbitro, fisica palla. È LA VERITÀ.            │
    └───────────────────────────────────────────────────────────────────┘
                                   ↕ calibrato l'uno sull'altro
    ┌─ Mondo di sfondo (giornate IA, leghe non seguite) ────────────────┐
    │  QuickResultResolver  →  punteggio + marcatori, modello a minuti  │
    │  Costo trascurabile. Tarato dall'harness sull'output del pieno.   │
    └───────────────────────────────────────────────────────────────────┘

`MatchDirector` **viene eliminato**, con tutti i knob `Chance*`. `MatchEngine` conserva il
modello statistico ma scende di rango: diventa il percorso veloce per il mondo, non la fonte
del risultato delle partite viste.

È lo stesso schema di Football Manager: motore completo per ciò che si guarda, sim rapida per
il resto del mondo.

### Base temporale

- **simulazione a 10 Hz** (1 tick = 100 ms di partita) → geometria fisicamente sensata;
- **stream scritto 1 frame ogni 5 tick** (2 Hz di tempo partita) → 10.800 frame per 90';
- alla compressione 30× già in uso ("1x" = ~3 minuti reali) fanno **60 fps di riproduzione**;
- memoria stream ≈ 1 MB come `int16`, e resta rigenerabile client-side da (seed + formazioni),
  quindi non passa dalla rete.

Velocità reali: giocatore 5,5–8,5 m/s da `Pace`, passaggio 12–25 m/s, attrito palla ~0,4 m/s².

---

## 4. Il piano, fase per fase

Ogni fase compila, passa i test, ed è **guardabile**: si apre una partita e si giudica prima di
proseguire.

### Fase 0 — Banco di prova (prerequisito assoluto)

Senza misura questo non si aggiusta a occhio.

- nuovo scenario `pitch` in `tools/BalanceHarness`, stesso pattern degli altri.
- metriche su N partite: gol, tiri, tiri in porta, possesso, passaggi tentati/riusciti,
  lunghezza media del passaggio, km percorsi per giocatore, tempo per terzo di campo, rimesse,
  corner, rinvii, fuorigioco, falli.
- metriche di **forma**: larghezza e profondità del blocco, distanza media fra le linee,
  distanza minima fra compagni, % di tempo in cui un giocatore è entro 3 m da un avversario.
- **dump visivo** di una partita da riga di comando, per giudicare la forma senza aprire Unity.
  Realizzato come file HTML autonomo (`--pitch-dump`): si scorre tutta la gara e si accendono le
  sovrapposizioni — riquadro squadra, linea difensiva, fili "entro 3 metri". Vedi §7.

Bersagli reali su cui tarare:

| Metrica | Bersaglio |
|---|---|
| Gol | 2,6–2,8 |
| Tiri (totali) | 22–28 |
| Passaggi (totali) | 900–1100 |
| Precisione passaggi | 78–86 % |
| Rimesse laterali | 35–45 |
| Corner | 9–12 |
| Fuorigioco | 2–4 |
| Falli | 20–26 |
| Km per giocatore | 9,5–11,5 |
| Larghezza blocco in fase difensiva | 30–40 m |
| Profondità blocco in fase difensiva | 25–35 m |

### Fase 1 — Unità e base temporale

Sim a 10 Hz, velocità reali, decimazione dello stream, attrito palla corretto.
Rompe golden master e replay salvati → `MatchEngine.Version` da 3 a 4 e rigenerazione.

### Fase 2 — Forma: formazione e blocco

- riscrittura di `AnchorY`: si dispone **la linea come unità** (raggruppamento per banda X,
  poi spaziatura realistica dentro la linea), non ogni ruolo sull'intera larghezza.
- `HomeSpot` sostituito da una **trasformata di blocco**: baricentro squadra (X da palla +
  mentalità, Y da palla) **con tetto** (scorrimento laterale ≤ 10 m), più offset di formazione,
  più **compattezza** (fattore di compressione in fase difensiva).
- **linea difensiva esplicita**: i centrali tengono una X comune = max(linea del fuorigioco,
  16 m dalla propria porta, X palla − 10 m).
- spaziatura verticale fra i reparti mantenuta a 10–12 m.
- scorrimento asimmetrico: la punta scala molto più di quanto il centrale salga.

**Qui il video cambia faccia.**

### Fase 3 — Difendere: zona e trigger

- il cervello di squadra assegna un compito per tick invece della marcatura universale:
  `Presser` (1, a volte 2) · `Cover` (copre il pressante) · `Marker` (solo avversari nel nostro
  terzo o in area, o in corsa alle spalle) · `Zone` (tutti gli altri: tengono la posizione di
  blocco).
- `Separate()` estesa agli **avversari**: i corpi non si compenetrano più.
- trigger di pressing da istruzione `Pressing`: zona di innesco + distanza + situazione
  (retropassaggio, controllo sporco, ricezione sull'esterno).

### Fase 4 — Decisioni con la palla, guidate dagli attributi

- `TryPass` diventa una **valutazione di opzioni**: passaggio a ciascun compagno ×3 varianti,
  conduzione, dribbling, tiro, spazzata. Punteggio su guadagno in avanti, rischio, pressione.
- **esecuzione con errore**: rumore su angolo e peso del passaggio, scalato da `Passing`,
  `Technique`, `Vision` e dalla pressione subita. Un buon regista completa, uno scarso sotto
  pressione la regala. *Qui nasce la differenza fra giocatori.*
- **dribbling come duello**: portatore (`Dribbling`/`Technique`/`Pace`) contro sfidante
  (`Tackling`/`Positioning`/`Pace`), esiti: mantiene · fallo · tackle vinto · palla vagante.
- **tiro**: chiunque tira quando è l'opzione migliore; modello tipo xG su distanza, angolo,
  pressione, `Finishing`; parata su `Reflexes` e piazzamento. **Il gol c'è perché la
  simulazione lo ha segnato.**

### Fase 5 — Il regolamento (modulo arbitro)

- **fuorigioco**: linea calcolata a ogni tick, passaggio verso un uomo oltre la linea →
  bandierina e punizione.
- **palla fuori anche a giocatore in possesso**, con punto di attraversamento sub-tick →
  rimessa / corner / rinvio corretti.
- **falli**, punizioni con barriera, **rigori**, **cartellini** e squalifiche. Rende leggibili
  `Aggression` e `Tackling`.
- **cambio campo all'intervallo**.

### Fase 6 — Inversione della causalità

- `MatchSimulator` produce punteggio ed eventi. `MatchDirector` e i knob `Chance*` eliminati.
- `MatchEngine` retrocesso a percorso veloce per il mondo di sfondo, ricalibrato dall'harness
  perché le tabelle di lega restino plausibili.
- determinismo conservato: stessa RNG seminata, stesso ordine di iterazione, matematica intera.

### Fase 7 — Dati sulle prestazioni

- `MatchReport` guadagna `PlayerMatchStats[]`: minuti, km, passaggi tentati/riusciti, passaggi
  chiave, dribbling, duelli vinti/persi, contrasti, intercetti, tiri, xG, parate, gol, assist,
  falli, e un **voto** calcolato.
- **rapporto tattico**: possesso, territorio, mappa dei passaggi, posizioni medie ricavate dallo
  stream, larghezza/profondità del blocco. È lo strumento con cui l'utente vede se la tattica ha
  funzionato.
- alimenta anche i modelli di condizione e sviluppo, che oggi non hanno dati di partita.

### Fase 8 — Le istruzioni contano davvero

I quattro assi diventano leve sul campo: Mentalità → altezza della linea e baricentro;
Pressing → zona e intensità di innesco; Ritmo → tempo di possesso e peso dell'opzione in avanti;
Ampiezza → larghezza del blocco.

Validazione nell'harness: tattiche contrapposte devono produrre **differenze misurabili**
(pressing alto → più recuperi nell'ultimo terzo; ampio → più cross; difensivo → linea media
più bassa). Se non le producono, la tattica non conta e la fase non è finita.

---

## 5. Rischi e costi

| Rischio | Mitigazione |
|---|---|
| Golden master e replay salvati si rompono | Bump `MatchEngine.Version` a 4, rigenerazione, replay vecchi marcati come versione precedente |
| CPU: 54.000 tick × 23 agenti per partita vista | Zero allocazioni nel ciclo caldo (oggi `AssignMarks` alloca per tick); bersaglio < 30 ms/partita su desktop; il mondo resta sul modello rapido |
| Memoria WebGL per lo stream | `int16` + decimazione a 2 Hz ≈ 1 MB; rigenerazione client-side, niente traffico di rete |
| Determinismo su IL2CPP | Nessuna funzione trascendente, nessun float: la regola attuale resta intatta e va verificata a ogni fase con `DeterminismCheck`. La misura di partenza della fase 0 è uscita identica su .NET 8/Linux e .NET 10/Windows, quindi lo scenario `pitch` è a sua volta una sonda di determinismo |
| Tabelle di lega che deragliano | Il modello rapido resta il produttore dei risultati di sfondo e viene ricalibrato dall'harness contro il motore pieno |
| Deriva estetica ("più realistico" ma illeggibile) | Il dump visivo della Fase 0 a ogni fase: si guarda prima di proseguire |

---

## 6. Ordine di attacco

    Fase 0  ──▶  Fase 1  ──▶  Fase 2  ──▶  Fase 3   ← qui il video cambia
                                            │
                                            ▼
                            Fase 4  ──▶  Fase 5  ──▶  Fase 6  ← qui la tattica conta
                                                        │
                                                        ▼
                                            Fase 7  ──▶  Fase 8

Le fasi 0-3 sono il pacchetto che rende la partita guardabile. Le fasi 4-6 sono quelle che la
rendono *significativa*. Le 7-8 sono quelle che la rendono *leggibile* all'utente.

---

---

## 7. Fase 0 — fatta: cosa misura e cosa dice

### Come si esegue

    .\tools\balance.ps1 -Scenario pitch
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html

Lo scenario `pitch` **non** fa parte di `-Scenario all`: misura una riscrittura in corso, non il
bilanciamento spedito, e infilarlo nel giro predefinito renderebbe rosso ogni run nascondendo una
regressione vera negli altri cinque. Si chiede per nome.

`-PitchDump` scrive **una partita come file HTML autonomo**: si apre nel browser, si scorre tutta la
gara, e si accendono le sovrapposizioni che disegnano ciò che i numeri misurano — il riquadro di
ogni squadra, la linea che tengono i quattro più arretrati, e un filo da ogni giocatore
all'avversario che sta entro tre metri da lui. Niente server, niente Unity.

`--pitch-strict` trasforma le bande "contro il calcio vero" in check PASS/FAIL. Oggi è spento: la
fase 0 misura il divario, non lo chiude. Ogni fase che dichiara di aver chiuso una banda la accende.

### Cosa c'è di nuovo

| File | Ruolo |
|---|---|
| `shared/Sim.Core/Match/Analysis/MatchMetrics.cs` | le grandezze misurate, in metri e chilometri |
| `shared/Sim.Core/Match/Analysis/MatchAnalyzer.cs` | legge `MatchReport` + `PositionStream` e conta. Sola lettura: non tocca il motore |
| `shared/Sim.Core.Tests/Match/MatchAnalyzerTests.cs` | 23 test; ogni valore atteso calcolato a mano, non registrato da un run |
| `tools/BalanceHarness/PitchScenario.cs` | lo scenario `pitch` e le bande del calcio vero |
| `tools/BalanceHarness/PitchDump.cs` | il visore HTML autonomo |

Il portiere non viene chiesto: viene **dedotto** (chi passa la partita più vicino alla propria porta),
così l'analizzatore non ha bisogno di altro che dello stream — ed è testato contro le formazioni vere.

### La misura di partenza (200 partite, tattiche neutre, seed 20260803)

    goals 2.52   shots 25.1 (14.2 on target)   passes 96 at 57.7% accuracy
    long balls 19.1   crosses 2.5   dribbles 116.9   clearances 22.3
    throw-ins 5.0   corners 0.0   goal kicks 17.9   offsides 0.0   fouls 0.0
    nobody on the ball 44.9% of frames
    ground covered 1.28 km per player (busiest 1.67, laziest 1.08)

    defending  width 32.7 m   depth 51.4 m   back line spread 13.9 m   biggest hole 16.2 m
    attacking  width 35.1 m   depth 52.9 m   back line spread 10.9 m   biggest hole 16.6 m

Questa misura è stata prodotta due volte in modo indipendente — .NET 8 su Linux e .NET 10 su
Windows — e le due esecuzioni sono **identiche cifra per cifra**, gli 114,2 tick sulla linea
compresi. Il determinismo fra runtime che il §5 elenca fra i rischi non è più un'ipotesi: la misura
di partenza è un riferimento su cui si può contare per otto fasi.

**6 letture su 19 dentro la banda del calcio vero.** Le fuori banda, in ordine di gravità:

| Lettura | Motore | Calcio vero | Cosa conferma |
|---|---|---|---|
| km per giocatore | **1,3** | 9-12 | §1.2 — la base temporale. I giocatori sono fisicamente incapaci di correre |
| passaggi | **96** | 850-1150 | il motore **dribbla più di quanto passi**: 117 dribbling contro 96 passaggi |
| precisione passaggi | **57,7%** | 76-88% | nessun errore di esecuzione: si perde palla solo per il dado del tackle |
| falli | **0** | 18-28 | non esistono |
| fuorigioco | **0** | 1,5-5 | non esiste |
| corner | **0** | 8-13 | mai concessi in 200 partite |
| rimesse laterali | **5,0** | 30-50 | §1.7 — la palla non esce quasi mai |
| spread linea difensiva | **13,9 m** | ≤6 | §1.3 — `AnchorY` sparpaglia i due centrali |
| profondità del blocco in difesa | **51,4 m** | 22-38 | nessuna compattezza |
| larghezza del blocco in attacco | **35,1 m** | 40-60 | la squadra non si allarga mai col pallone |
| tiri in porta | **14,2** su 25,1 | 6-11 | il 55% cosmetico di `SavedShareOfFailedChancesPercent` |

E un check di contratto che **fallisce**:

    [FAIL] a held ball is never sitting on a line of the pitch
           114.2 ticks per match with a player holding the ball on a boundary

**Il 10% della partita si gioca con un giocatore che tiene la palla appoggiata su una linea del
campo.** È il "fuori non funziona", contato: `ResolveOutOfPlay` esce subito se la palla ha un
padrone, quindi il portatore viene semplicemente riportato dentro da `ClampX/ClampY`.

Gli altri due check di contratto passano: ogni partita produce uno stream, e il quadro e il
risultato concordano sempre sul punteggio.

### Il costo — e la suite di test da 10 minuti

44 ms per partita a 12 tick/minuto in Release, simulazione + misura. In Debug (che è quello che usa
`dotnet test`) la misura è impietosa:

| | ms per partita, Debug |
|---|---|
| motore con `generatePositions: true` | **96,4** |
| motore con `generatePositions: false` | **0,54** |

**La simulazione di movimento costa 180 volte il modello di risultato.** È questo, e solo questo,
che rendeva `dotnet test` una faccenda da dieci minuti: `TacticsTests` gioca il campo delle
istruzioni (81 tattiche × 80 partite) più due sweep da 1000, cioè ~8.500 partite di cui legge
**solo il punteggio** — e per ognuna generava un filmato di 90 minuti che nessuno guarda.

Corretto: `generatePositions: false` nei quattro fixture statistici (`TacticsTests` campo tattiche,
`PositioningTests` linea/ampiezza/exploit, `MatchFatigueTests`, `ConditionTests` fresco-vs-stanco) —
in tutto ~11.000 partite. Lo stream è generato **dopo** ogni tiro di dado del risultato e ogni
partita di questi test riceve il proprio seed, quindi spegnerlo è identico al bit sul risultato:
verificato su 1296 partite, 0 differenze. I test di identità e di determinismo (`PositionStreamTests`,
`MatchResimTests`, `PrematchPlanTests`, golden master) lo tengono acceso — lì lo stream *è* la cosa
sotto esame, e `MatchReportHasher` lo include nell'hash.

**Regola d'ora in avanti:** chiunque simuli una partita che nessuno guarderà passa
`generatePositions: false`. Alla fase 1 il costo del flusso sale di circa ×50 (10 Hz invece di
12 tick/minuto), quindi senza questa disciplina la suite diventa inutilizzabile. Il tetto resta
~30 ms per le partite guardate, e il mondo di sfondo sta sul modello rapido.

Da guardare, fuori dalla fase 0: **`server/Infrastructure/Leagues/MatchResolver.Resolve`** costruisce
il motore senza specificare nulla, quindi genera il flusso per ogni partita di lega risolta lato
server. Se quei report servono come replay va bene; se no, sono ~96 ms di CPU buttati per ogni
partita di sfondo. Da decidere insieme.

### Due test rossi trovati per strada

Entrambi pre-esistenti (nel rifacimento del movimento in corso, non nella fase 0) e entrambi bug
*del test*, non del motore:

- `PrematchPlanTests.NeverFiringRule_IsByteIdentical` — la regola "in vantaggio di 5" era data per
  impossibile, ma il seed 27 finisce 6-1 e passa da +5: la regola **scattava** e il report poteva
  legittimamente cambiare. Margine portato a 8 (su quei 50 seed il vantaggio massimo raggiunto è 5).
- `PositionStreamTests.TheBall_NeverChangesDirectionUntouched` — il test misurava la distanza dei
  giocatori dalla posizione **precedente** della palla. Quando una palla vagante viene raccolta da un
  compagno di chi l'ha giocata il motore non registra alcuna azione (logga un'intercettazione solo se
  cambia squadra), ma la palla viene incollata ai piedi del raccoglitore: una curva giudicata nel
  punto sbagliato. Ora un cambio di possesso conta come tocco. I 2 casi su 7.421 sparivano entrambi.

Il secondo è comunque sintomatico: il raccoglitore può stare a `ControlRadius` **+
`ChanceReachBonusDm`** (fino a 7,9 m) grazie al pollice del direttore sulla bilancia — cioè uno dei
trucchi del §1.1 che si vede a schermo come un teletrasporto della palla. Sparisce con la fase 6.

---

## 8. Riferimenti

- RoboCup Soccer Simulator — https://rcsoccersim.readthedocs.io/en/latest/overview.html
- RoboCup 2D Soccer Simulation League — https://en.wikipedia.org/wiki/RoboCup_2D_Soccer_Simulation_League
- Football Manager 26, positional play e autenticità della partita —
  https://www.footballmanager.com/features/truer-football-motion-match-authenticity-positional-play
- open-football (Rust) — https://github.com/ZOXEXIVO/open-football
- openengine — https://github.com/atas76/openengine
- Openfoot Manager — https://openfootmanager.com/
- Buckland, *Programming Game AI by Example*, cap. 4 (base dell'implementazione attuale)
