# Rifacimento del motore partita — diagnosi e piano

Stato: piano approvato il 2026-09-02. Causalità invertita (il campo decide), lavorazione a fasi
con il gioco sempre funzionante, regolamento fino a falli/cartellini/punizioni.

- [x] **Fase 0 — banco di prova** (2026-09-02). Vedi §7 per la misura di partenza.
- [x] **Fase 1 — unità e base temporale** (2026-09-03). Vedi §8.
- [x] **Fase 2 — forma: formazione e blocco** (2026-09-03). Vedi §9.
- [x] **Fase 3 — difendere: zona e trigger** (2026-09-04). Vedi §10.
- [x] **Fase 4 — decisioni con la palla** (2026-09-04, misurata nel container). Vedi §11.
- [x] **Fase 5 — il regolamento** (2026-09-05, verificata dall'utente; la correzione della
      barriera §12.5 è arrivata dopo ed è stata riverificata il 2026-09-06). Vedi §12.
- [x] **Fase 6 — inversione della causalità** (2026-09-08, corretta il 09-11 dopo il suo primo run,
      **verificata dall'utente il 2026-09-11**). Vedi §13. 🏁 CHIUSA.
- [~] **Fase 7 — dati sulle prestazioni** (scritta il 2026-09-11; due run dell'utente lo stesso
      giorno: **`Sim.Core` 398/398, golden master invariato, pitch 20/20 e 25/25, balance 28/28**,
      voto medio 8,37 → **6,35** e xG 2,00 → **1,33 contro 1,33 gol**. Tre correzioni, tutte trovate
      dalla misura o dall'occhio; manca l'ultimo giro). Vedi §14.
- [ ] Fase 8 — le istruzioni contano

---

## 📍 Stato — 11 settembre 2026

**Siamo qui: FASE 7 — due run dell'utente, `Sim.Core` verde (398/398), i numeri tornano; resta da
confermare la terza correzione (il voto per reparto) che l'OCCHIO ha imposto.**

Il suo run dell'11 settembre: **compilato al primo colpo, 636 test su 637**, e soprattutto
**`[DeterminismCheck]` e `[server-determinism]` stampano ancora `0xB0052E0B3942206A`** — leggere la
partita non la tocca, che è l'intera argomentazione di sicurezza della fase, verificata. Il `pitch`
esce con **20/20 in banda e 25/25 check** (i due nuovi di contratto passano: gli undici per novanta
minuti e ogni gol su una riga), `balance.ps1` è **28/28 con ogni cifra invariata**, e leggere la
partita costa **+11,5 ms su 306**, il 3,8%. L'unico rosso era il **voto medio a 8,5 invece di 6,0**:
il motore produce trentatré azioni difensive per uomo dove il calcio ne conta due o tre, e un bonus
fisso per azione regalava tre punti a tutti. Adesso il lavoro senza palla si paga **sullo scarto
dalla media di quella partita**. Corretto anche l'**xG**, che diceva 2,00 a squadra dove se ne
segnavano 1,33. Tutto in §14.

**Il secondo run** ha dato `Sim.Core.Tests` **398/398**, voto medio **6,35** (era 8,37) e xG **1,33
a squadra contro 1,33 gol** — l'xG adesso predice quello che il motore fa. Poi ho aperto il dump: la
pagella diceva che **chi aveva segnato due gol valeva meno del suo centrale**, e su entrambe le
squadre tutti i difensori stavano sopra tutti gli attaccanti. Un attaccante recupera meno palloni e
ne perde di più perché è il suo mestiere, quindi adesso ogni uomo è confrontato **con il suo
reparto**, ricavato da dove ha davvero passato la partita. Terza correzione, un test la inchioda, e
serve un ultimo giro.

**Lo stato dopo le correzioni: FASE 7 SCRITTA E QUASI VERIFICATA — la partita adesso si LEGGE.**

Ogni uomo ha la sua riga del referto (minuti veri, palloni giocati e quanti sono arrivati, il
passaggio che ha creato il tiro, il duello perso, i chilometri, il voto), e ogni squadra ha il suo
rapporto tattico (possesso, territorio, forma del blocco difendendo e attaccando, mappa dei
passaggi). **Tutto è LETTO dal filmato a partita finita**, dopo l'ultimo tiro di dado: è la stessa
regola che l'analizzatore della fase 0 rispetta da sempre, e la conseguenza è che
**`MatchReportHasher` non cambia di un bit e il golden master `0xB0052E0B3942206A` della fase 6 resta
il numero giusto**. Il resoconto completo è in §14.

**Due avvertenze oneste, entrambe in §14.** (1) Il container di questa sessione **non ha potuto
compilare né misurare**: l'archivio Ubuntu non è più raggiungibile dal proxy, quindi niente
`dotnet-sdk-8.0`, niente stub NUnit, niente harness. Il codice è scritto con cura e non è stato
compilato da nessuna parte — è il primo `dotnet test` sulla sua macchina a dire la verità. (2) La
parte della fase che TOCCA IL GIOCO — minuti reali e voti che alimentano condizione e sviluppo — è
scritta ma **spenta**: si accende passando i dati, e prima di accenderla va misurata con
`balance.ps1`, perché cambia come le rose si stancano e come i giocatori crescono.

**Lo stato della fase 6, che resta la base:**

**🏁 FASE 6 CHIUSA E VERIFICATA DALL'UTENTE — la causalità è invertita, e il tiro decide
il gol.**

**`dotnet test` → 615 su 615 verdi, zero rossi, in 486,2 s** (`Sim.Core.Tests` **375** — i 364 della
fase 5 più gli 11 di `CausalityTests` — e `Api.Tests` **240**). `[DeterminismCheck]` e
`[server-determinism]` stampano entrambi **`0xB0052E0B3942206A`**, identico cifra per cifra al valore
calcolato nel container su .NET 8: **il determinismo fra runtime sopravvive a engine v9.**

**E lo scenario `pitch` ha riprodotto OGNI SINGOLO NUMERO del run del container** — gol 2,66, tiri
22,0 (7,1 nello specchio), passaggi 896 al 76,7%, rimesse 35,4, corner 11,4, rinvii dal fondo 21,0,
fuorigioco 2,9, falli 22,7, gialli 3,25, km 11,80, difendendo 39,2 × 31,9 e attaccando 44,0 × 37,7 —
**20/20 in banda e 23/23 check PASS**, a 306,2 ms a partita contro i 603,1 del container (1,97x, in
linea col 2,20x della fase 5). **`Balance checks PASSED`: exit code 0.**

**`.\tools\balance.ps1` 28/28 PASS con ogni cifra invariata** (tattiche 45,0% / 42,5%, formazioni
F433 37,4%/49,9% e F352 51,4%, stagione +6,5 pts, difficoltà 6,9/6,6/9,5/7,6/10,1, 67,6 trasferimenti,
ingaggi 69,8%, `Avg goals/match 2,44 | draws 24,8% | home wins 48,4%`, `Strong wins 82%`,
`[counter] 56,0%`, `[familiarity] 49,3% contro 23,9%`, `[sweep] top 53,8%`, `[match-fatigue] 481 →
580`, `[fitness->result] 517 contro 318`) — **ed è la prova che il modello risultato non è stato
toccato**, che è la cosa che questa fase rischiava di più.

**Il primo run (8 settembre) aveva dato 607/615**, e i suoi otto rossi erano quattro cose diverse: un
errore mio (il commit finale di `BalanceConfig.cs` non era atterrato, e il suo motore girava ancora
a `PitchHomeAdvantagePermille = 45`), tre test che asserivano il contratto che questa fase rompe
apposta, uno che girava con la picture accesa per una domanda sul risultato, **e un difetto vero, il
quinto della fase** (§13, difetto 5). Tutto raccontato lì.

**E L'OCCHIO: guardato.** Il dump della sua partita, aperto e misurato frame per frame: ogni gol è la
palla sulla linea, **ogni parata è il portiere** (e nessun altro), in una posizione da portiere —
fra 8,7 e 11,9 m dalla propria linea; il tiratore è **sulla palla** quando la colpisce, non a tre
metri; e un tiro murato si vede per quello che è, la conclusione respinta e il pallone che scappa in
angolo. Quello che l'occhio ha trovato è **la distanza dei tiri**, ed è in §13 fra le cose aperte.

| | Fase | Stato |
|---|---|---|
| 0 | banco di prova | ✅ fatta **e verificata dall'utente** |
| 1 | unità e base temporale | ✅ fatta **e verificata dall'utente** |
| 2 | forma: formazione e blocco | ✅ fatta **e verificata dall'utente** |
| 3 | difendere: zona e trigger | ✅ fatta **e verificata dall'utente** |
| 4 | decisioni con la palla | ✅ fatta **e verificata dall'utente** |
| 5 | il regolamento | ✅ fatta **e verificata dall'utente**, barriera (§12.5) compresa |
| 6 | inversione della causalità | 🏁 **fatta e verificata dall'utente** — il tiro decide il gol |
| 7 | dati sulle prestazioni | ✍️ **scritta, tre correzioni, ultimo giro da fare** (§14) |
| 8 | le istruzioni contano | da fare |

### Come si verifica che tutto gira

    .\tools\build-simcore.ps1
    dotnet test
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchStrict -PitchDump .\replay.html
    .\tools\balance.ps1        # gli altri scenari NON devono muoversi di un numero

**Golden master della fase 6: `0xB0052E0B3942206A`** (engine v9; era `0x222F723B4993ED25` in v8,
`0xF8BE4A32C28421A1` in v7 e `0xABC7B41DC6F258C2` in v6), già ripuntato nei quattro posti soliti. I
replay salvati in v8 non sono più disegnabili: è previsto, e il client li rifiuta da solo perché
confronta con `MatchEngine.Version`. Attesi **375 test verdi** in `Sim.Core.Tests` (i 364 della fase
5 più gli 11 di `CausalityTests`), **615 in totale**, e lo scenario `pitch` che esce con **codice 0**
e **20/20 in banda** sotto `-PitchStrict`.

**Quattro test sono stati RIDISEGNATI, non aggiustati**, perché asserivano il contratto che questa
fase rompe apposta: `WatchingAFixture_DoesNotChangeAnyResult` → `..._PlaysItOnThePitch_AndLeavesEveryOtherFixtureAlone`
(guardare una partita può cambiarla; quello che non può fare è muovere un'ALTRA partita della
giornata, ed è quello a tenere la classifica la stessa classifica); `CustomPositions_...EvenWithTheFlagOff`
(dove metti un uomo adesso conta, quindi resta solo la metà che diceva che lo MUOVE);
`EveryEvent_IsStruck_AndCreditedToItsPlayer` (un gol deviato è credito suo senza un suo tiro prima);
`Rotation_OutperformsFixedXI_OverACongestedBlock` (96 partite sul percorso veloce: la domanda è sul
risultato, e una stagione di rotazione la decidono le partite che nessuno guarda).


### Verificato dall'utente il 5 settembre 2026 (fase 5) — 🏁 CHIUSA

**`dotnet test` → 604 su 604 verdi, zero rossi, in 518,5 s** (`Sim.Core.Tests` **364** — i 355 della
fase 4 più i 9 nuovi `RefereeTests` — e `Api.Tests` **240**). `[DeterminismCheck]` e
`[server-determinism]` stampano entrambi **`0x222F723B4993ED25`**, **identico cifra per cifra** al
valore calcolato nel container su .NET 8: il determinismo fra runtime sopravvive a engine v8.

**E lo scenario `pitch` ha riprodotto OGNI SINGOLO NUMERO del run del container** (cifre del run del
6 settembre, dopo la correzione della barriera; quelle del 5, prima, tornavano identiche allo stesso
modo) — gol 2,52, tiri 25,2 (15,0), passaggi 877 al 78,2%, **rimesse 33,4, corner 10,7, rinvii dal
fondo 17,5, fuorigioco 4,1, falli 20,9**, 2,85 gialli / 0,14 rossi / 0,16 rigori, difendendo
38,7 × 32,1 con linea 5,9 e buco 10,8, attaccando 45,2 × 39,6, km 11,07 (il più attivo 16,45) —
**19/20 in banda e 3/3 check di contratto PASS**, a **311,7 ms a partita contro i 684,2 del
container** (2,20x, in linea col 1,64x delle fasi 3 e 4). **`Balance checks PASSED`: l'exit code è 0.** Alla fase 4 quello stesso comando
usciva con 1 per costruzione — era il rosso della palla tenuta su una linea — quindi da adesso **un
exit code diverso da zero sul `pitch` è una regressione vera e va inseguito.**

**Anche ogni riga diagnostica della fase riproduce il numero del container**, sulla sua macchina e
sul suo OS:

    [laws-restarts] 40,5 rimesse · 10,7 corner · 18,3 rinvii dal fondo a partita,
                    e 0 frame in 30 partite con una palla tenuta su una linea
    [laws-offside]  4,7 fuorigioco a partita, 56 bandierine controllate
    [laws-fouls]    22,3 falli · 3,05 gialli · 0,15 rossi · 0,20 rigori a partita (446 controllati)
    [laws-tackling] una squadra di marcatori a 90 ha commesso 8,5 falli e preso 1,50 cartellini a
                    partita; una a 20 ne ha commessi 12,1 e presi 1,90
    [laws-cards]    7 espulsi in 40 partite
    [laws-halftime] intervallo controllato in 6 partite

**`.\tools\balance.ps1` 28/28 PASS con ogni cifra invariata** (tattiche 45,0% / 42,5%, formazioni
F433 37,4%/49,9% e F352 51,4%, stagione +6,5 pts, difficoltà 6,9/6,6/9,5/7,6/10,1, 67,6
trasferimenti, ingaggi 69,8%, tutto il blocco mondo) — **ed è la prova che il modello risultato non è
stato toccato**, insieme alle sue calibrazioni tutte identiche: `Avg goals/match 2,44 | draws 24,8% |
home wins 48,4%`, `Strong wins 82%`, `[condition-live] 2,59 gol, 21,3% pareggi`, `[counter] 56,0%`,
`[familiarity] 49,3% contro 23,9%`, `[sweep] top 53,8%`, `[positioning-line] 487→513 / 467→513`,
`[positioning-width] 549 contro 531`, `[match-fatigue] 481 → 580`, `[fitness->result] 517 contro 318`.

**E L'OCCHIO: guardato e accettato dall'utente lo stesso giorno.** Ha aperto il dump e visto le
rimesse, i corner, la barriera e l'intervallo. La metà visiva dell'accettazione non è un test, ed è
data: **🏁 la fase 5 è chiusa in tutto e per tutto.**

**Il CAMBIO DI CAMPO era la decisione aperta, ed è decisa: si vede** (l'utente ha chiesto di vederlo
lo stesso giorno). Resta fuori dalla simulazione — Sim.Core tiene le squadre sullo stesso lato — e
sono i due VISORI a specchiare la ripresa, entrambi leggendo il frame del fischio dall'azione
`BallActionKind.HalfTime`: il dump dell'harness (**verificato nel container e accettato dall'utente**)
e `MatchRenderer` del client (**scritto, Play-mode ancora da guardare** — il client non si compila
qui). Il dettaglio è in §12.

**E il dump ha finalmente un OROLOGIO che si vede.** C'era, ma era uno `<span>` grigio dentro una
riga muta dell'header: mentre guardi il campo quel numero non lo trova nessuno. Adesso è due volte —
grande e ambra nell'header accanto al titolo, e nella fascia sopra il campo insieme al punteggio
corrente — in **minuti E secondi**, perché un frame è mezzo secondo e senza i secondi il numero
sembra fermo.

### Verificato dall'utente il 5 settembre 2026 (fase 4) — 🏁 CHIUSA

**`dotnet test` → 595 su 595 verdi, zero rossi, in 378,8 s** (`Sim.Core.Tests` **355** in 310,7 s —
i 349 della fase 3 più i 6 nuovi `BallDecisionTests` — e `Api.Tests` **240** in 378,1 s).
`[DeterminismCheck]` e `[server-determinism]` stampano entrambi **`0xF8BE4A32C28421A1`**,
**identico cifra per cifra** al valore calcolato nel container su .NET 10: il determinismo fra
runtime sopravvive a engine v7.

**E qui c'è il fatto che vale più di tutti: OGNI riga diagnostica della fase riproduce il numero del
container, alla prima decimale.**

    [ball-skill] passaggio+tecnica+dribbling  56,6% di palla · 154 vs 111 nell'ultimo terzo · 18,1 vs 21,0 perse in casa propria
    [ball-skill] solo passaggio e tecnica     53,9% · 160 vs 121 · 22,4 vs 28,2
    [ball-skill] solo dribbling               54,4% · 152 vs 130 · 20,9 vs 23,2
    [passing]                                 887 passaggi a partita al 79,2%
    [keeper]                                  respinte 30,0% da un portiere a 90, 52,7% da uno a 20

Due macchine diverse, due sistemi operativi diversi, gli stessi numeri: la differenza fra due
giocatori non è un artefatto del banco di prova, è una proprietà del motore.

**Il modello risultato è intatto, e lo dicono le sue stesse calibrazioni**, tutte identiche ai
valori accettati da fasi precedenti: `Avg goals/match 2,44 | draws 24,8% | home wins 48,4%`,
`Strong wins 82%`, `[condition-live calibration] 2,59 gol/partita, pareggi 21,3%`,
`[counter] 56,0%`, `[familiarity] 49,3% contro 23,9%`, `[sweep] top 53,8%`,
`[positioning-line] 487→513 / 467→513`, `[positioning-width] 549 contro 531`,
`[match-fatigue] 481 → 580`, `[fitness->result] 517 contro 318`.

**Le righe che SI SONO mosse sono tutte del livello movimento, ed è previsto** (la fase l'ha
riscritto): `[duties]` difendendo **38,7 × 31,9** con linea 5,6 e buco 10,8 (era 40,6 × 36,2 / 5,9 /
11,5), attaccando 44,4 × 38,6; `[duties] 402 contrasti e 235 intercetti` a partita contro i 180/318
di prima — il duello ha sostituito l'intercetto; `[bodies]` 0,131% → 0,072% (era 0,116% → 0,066%);
`[shape]` il centro squadra al massimo **13,5 m** fuori dalla mediana, era 15,7; `[movement]` palla
ai piedi di qualcuno per l'**81%** della partita, era il 60%. Le asserzioni di quei test tengono
tutte: sono misure che si sono spostate, non bande uscite.

**UNA COSA DA GUARDARE, e non è un rosso: il trigger di pressing ha perso la monotonia fra basso e
medio.** `[press]` stampa **low 5,50 m · medium 5,56 m · high 4,98 m**, dove alla fase 3 leggeva
6,76 / 6,56 / 5,96. L'estremo tiene (alto contro basso: 4,98 contro 5,50, ed è l'unica cosa che il
test asserisce), ma basso e medio ormai coincidono dentro il rumore. Il motivo è plausibilmente
questo: con il possesso che sopravvive e i duelli al posto degli intercetti, lo spazio lasciato a
un uomo sulla palla nel proprio terzo è più piccolo per tutti, e la differenza fra due tarature di
pressing si comprime. **È esattamente la domanda della fase 8** ("le istruzioni contano davvero"),
e va misurata lì invece di essere ritoccata adesso a occhio.

**Un altro numero al limite da tenere d'occhio:** `[movement] worst single-tick step 40dm (cap
42dm)`. Alla fase 1 era 29 su 34. Il passo più lungo di un tick è vicino al suo tetto, quindi se una
fase futura alza ancora le velocità quel test diventa il primo a rompersi.

**COSA RESTA, ed è solo l'occhio:** aprire `replay.html` e GUARDARE. I numeri sono in banda; la
metà visiva dell'accettazione non è un test.

### Verificato dall'utente il 4 settembre 2026 (fase 3, quella precedente)

`dotnet test` **589 su 589, zero rossi, in 360,5 s** — `Sim.Core.Tests` **349** (i 340 della fase 2,
più i 4 `ReplayCodecTests` che non aveva ancora girato, più i 5 nuovi `DefensiveDutyTests`) e
`Api.Tests` **240**. `[DeterminismCheck]` e `[server-determinism]` stampano entrambi
**`0xABC7B41DC6F258C2`**, **identico cifra per cifra** al valore calcolato nel container: la
determinismo fra runtime sopravvive a engine v6. `.\tools\balance.ps1` **28/28 PASS con ogni cifra
invariata** (difficoltà 6,9/6,6/9,5/7,6/10,1, 67,6 trasferimenti, ingaggi 69,8%, tutto il blocco
mondo). Lo scenario `pitch` ha riprodotto **ogni singolo numero** del run del container — 12/19 in
banda, difendendo 40,6 × 36,2 m, linea 5,9 m, gol 2,52, tiri 25,1 — a **263,6 ms a partita** contro
i 444 del container. Il suo **exit code 1 è previsto**: è il rosso della palla tenuta sulla linea
(§1.7), che è della fase 5.

E la correzione dei quattro test costosi è confermata sul campo: **`dotnet test` è passato da 41
minuti a 360 s** (`Sim.Core.Tests` da 2467 s a **239,9 s**) a parità di test e di cifre stampate.

**Una cifra stampata si è però mossa, e vale sapere perché:** `[positioning-width]` legge 549 contro
531 dove leggeva 543 contro 524. Quell'harness riusa **un solo `Pcg32` per le sue 400 partite**,
quindi le pescate del livello movimento spostano le partite successive. La tesi che afferma è
comparativa (largo crea più di stretto) e il distacco è invariato, +18 contro +19 — ma uno sweep che
condivide l'RNG fra le partite non è un numero da citare come fisso.

### Verificato dall'utente il 3 settembre 2026 (fase 2)

Comandi eseguiti, nell'ordine: `.\tools\build-simcore.ps1` → `dotnet test` →
`.\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html` →
`.\tools\balance.ps1`.

- **`dotnet test` — 580/580 verdi**, zero falliti, zero ignorati, in **2467,2 s** (Sim.Core.Tests
  340/340, che sono i 334 di prima più i 6 nuovi di `BlockShapeTests`; Api.Tests 240/240 in 445,2 s).
- **Il determinismo fra runtime regge anche attraverso l'engine v5.**
  `[DeterminismCheck] 0xB3C30BEEAA5781B2` e `[server-determinism] 0xB3C30BEEAA5781B2` sulla sua
  .NET 10/Windows sono **identici cifra per cifra** al valore calcolato nel container su .NET 8 e 10.
- **Le nuove asserzioni di forma stampano gli stessi numeri del container**, il che vuol dire che il
  programma sostitutivo con cui erano state provate misurava davvero la stessa cosa:
  `[shape] the team's centre is worst 15,7 m off the middle of the pitch` ·
  `[shape] attacking width 42,3 m depth 48,5 m back line spread 8,5 m`.
- **`balance.ps1 -Scenario pitch` — ogni singola cifra identica a quella del container**: gol 2,52,
  tiri 25,1, km 10,83, larghezza 42,0/38,8, profondità 48,3/47,8, buco 14,6/14,9, **10/19 in banda**.
  A **291,7 ms per partita** contro i 497 del container: la sua macchina è di nuovo circa il doppio
  più veloce. **L'exit code 1 è atteso** — il check rosso è "a held ball is never sitting on a line
  of the pitch", cioè la misura del bug del §1.7, non un guasto.
- **`balance.ps1` — 28/28 PASS con ogni numero fermo**: difficoltà 6,9/6,6/9,5/7,6/10,1,
  trasferimenti 67,6, ingaggi 69,8% dei ricavi, tutto il blocco mondo. Il modello di risultato non
  si è mosso di una cifra, come previsto e come già verificato con il diff nel container.
- **Le altre letture del movimento, tutte a posto:** `[movement] worst single-tick step 40dm (cap
  42dm)` · `[swerve] 0 over 69070 moving ticks` · `[outcomes] goals 21/21 · saves and misses
  190/190` · `[passes] 7374 · median 16m · p99 55m · longest 69m` · `[shots] 23 goals · 191 saves ·
  131 misses` · palla ai piedi di qualcuno il 58% della partita.

### Il numero che questo run ha fatto emergere: `dotnet test` costa 41 minuti

Non è della fase 2 — 268 → 292 ms per partita sono il 9%, cioè al massimo tre minuti e mezzo dei
quarantuno — e **non è ancora spiegato**. Il **212,7 s** che questo documento porta dietro è la
misura della **fase 0**, presa quando una partita col filmato costava pochi millisecondi; dalla
fase 1 ne costa 0,29 sulla macchina dell'utente, e 2467 s sarebbero circa ottomila partite col
flusso acceso.

**La prima spiegazione che mi ero dato era sbagliata, e vale la pena scriverlo.** Avevo accusato
`SeasonProgressor._watchEngine`, che ha `generatePositions: true`. Verificato con un grep:
**nessun test passa `watchedClubId`**, e senza quello il progressore usa sempre l'altro motore.
Il `_watchEngine` nella suite non gira mai. Di nuovo la lezione della fase 1: la prima ipotesi era
sbagliata e solo la verifica l'ha smentita — solo che stavolta la verifica sarebbe costata un grep
e l'ho fatta dopo aver scritto l'ipotesi in tre documenti.

**Come si scopre davvero, in un comando:**

    dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "trx;LogFileName=slow.trx"

Il file `.trx` porta la durata di ogni singolo test. Con quella lista in mano si sa se sono le
partite col flusso, la generazione di mondi, o qualcosa che la fase 1 ha reso caro senza accorgersene
— e la via d'uscita del §5 (`[Category("Slow")]` + `dotnet test --filter TestCategory!=Slow`) si
può puntare sui test giusti invece che a caso.

### `MatchResolver.Resolve`: deciso — il filmato resta, il formato no

La decisione era aperta dalla fase 1: *"il risolutore di lega genera il flusso per ogni partita
risolta lato server; se quei replay servono, il prezzo è quello; se no, è un parametro da girare."*
Guardato il codice, **quei replay servono**: `LeagueSeasonService.GetReplayAsync` restituisce
`fixture.ReplayJson` a qualunque membro della lega, il client lo deserializza in `MatchReport` e lo
disegna — c'è perfino un errore dedicato, `ReplayTooOld`, per quando la versione del motore non
combacia. Guardare la propria partita **è** la funzionalità. Quindi il parametro non si gira, e la
domanda "chi ha bisogno del filmato" ha una risposta: chi apre il replay.

**E la CPU non è il problema.** 0,5 s a partita, una giornata di lega sono cinque incontri, ed è un
lavoro in background: due secondi e mezzo per giornata.

**Il problema è il formato, e adesso ha dei numeri** (una partita, 10.801 fotogrammi, 496.846 valori
di posizione):

| Come è scritto il referto | Dimensione | gzip |
|---|---|---|
| **senza filmato** | **2 KB** | — |
| **con il filmato, oggi (JSON)** | **2077 KB** | 754 KB |
| filmato come int16 + base64 (la proposta del §3) | 1294 KB | 797 KB |
| **filmato come delta + varint + base64** | **649 KB** | 391 KB |
| filmato come delta + varint, byte grezzi | 487 KB | — |

**Il filmato è mille volte il resto del referto.** Duecentomila numeri piccoli scritti in ASCII
decimale dentro una colonna `text` di Postgres: una stagione di lega a dieci squadre sono 90
incontri, cioè **180 MB**. E la proposta che il §3 dava per buona — `int16` — **è la peggiore delle
due misurate**: un `int16` in base64 costa 2,67 byte per valore mentre il delta di un uomo fra due
fotogrammi sta quasi sempre in **un byte solo**, perché in mezzo secondo nessuno si sposta di più di
qualche decimetro. È la differenza fra 1294 KB e 649 KB, e si vede solo misurando.

**Il codec, scritto il 3 settembre 2026: 2077 KB → 794 KB, 2,61×.** L'utente ha scelto il codec e
non la cadenza — cioè nessun fotogramma in meno nel replay.

**Come è fatto.** `PositionStream.Pack()` porta le quattro tracce intere (palla, casa, ospiti,
portatore) in una stringa: **delta per corsia, zigzag, varint, base64**. "Per corsia" perché gli
array intercalano i fotogrammi — la X di un uomo sta ogni `stride` valori — ed è il suo movimento a
essere piccolo: in mezzo secondo nessuno copre più di qualche decimetro, quindi quasi ogni delta sta
in **un byte solo**. `Unpack()` fa il contrario. La **lista delle azioni resta JSON leggibile** di
proposito: è la telecronaca, pesa un quindicesimo delle posizioni, e poterla leggere dentro un
replay salvato vale più dei byte.

**Perché non è un attributo di serializzazione.** Il server serializza con `System.Text.Json`, il
client deserializza con **Newtonsoft**, e `Sim.Core` non ha **nessun riferimento a pacchetti** — è
precisamente ciò che le permette di compilare offline nel container, cioè che una fase possa essere
misurata prima di essere consegnata. Quindi il codec è **esplicito e simmetrico**: `Pack()` prima di
serializzare, `Unpack()` dopo aver letto. Sul server c'è un solo posto che lo sa,
`Infrastructure/Leagues/ReplayStore`, usato dai sei punti che scrivono un replay; sul client sono i
quattro punti che ne leggono uno.

**Tre proprietà che il test inchioda** (`Sim.Core.Tests/Match/ReplayCodecTests`):

- il giro completo è **senza perdite** e il referto **produce lo stesso hash** — quindi la forma
  compressa non può muovere un golden master, ed è per questo che non c'è un bump di versione;
- un replay **salvato prima** del formato compresso continua a leggersi: porta i suoi array e
  `Unpack()` su di lui non fa nulla;
- uno che arriva compresso e a cui **nessuno ha detto di decomprimersi si disegna lo stesso**:
  `TickCount`, `BallAt`, `HomeAt` e `AwayAt` decomprimono da soli. Il prezzo di una dimenticanza è
  un primo fotogramma lento, non un campo vuoto.

**Quello che non è stato fatto, e perché.** `int16` era la proposta del §3 ed è **la peggiore delle
due misurate** (1294 KB contro 649): due byte fissi per valore non possono battere uno variabile.
E la leva della cadenza — `StreamTicksPerFrame` da 5 a 10, che dimezzerebbe di nuovo — **resta sul
tavolo, non tirata**: costa 30 fotogrammi al secondo invece di 60 nel replay, e la qualità del
filmato è esattamente ciò che le fasi 1 e 2 sono servite a comprare.

    referto senza filmato        2 KB
    con il filmato, prima     2077 KB
    con il filmato, adesso     794 KB     (2,61x; comprimere costa 25 ms, leggere 30)

**Verificato nel container:** giro completo senza perdite su tutte e quattro le tracce, referto con
lo **stesso hash**, replay vecchio ancora leggibile, decompressione pigra funzionante — e le due
prove che contano per il resto del lavoro: **`[DeterminismCheck] 0xB3C30BEEAA5781B2` non si è
mosso** e lo scenario `pitch` stampa **gli stessi numeri cifra per cifra** di prima del codec. I due
file di test nuovi (`BlockShapeTests`, `ReplayCodecTests`) non erano mai stati compilati da nessuna
parte: adesso passano il *type-check* contro Sim.Core vera, con uno stub di NUnit scritto a mano —
la tecnica che il CLAUDE.md già registrava. Restano non compilati qui, come sempre, il server e il
client. Il conto atteso sulla macchina dell'utente sale da 580 a **584** (i quattro di
`ReplayCodecTests`).

### Note operative

- **`generatePositions: false` per ogni partita che nessuno guarderà.** Il flusso di movimento
  costa ~0,5 s a partita contro 0,9 ms del modello di risultato. È questo che tiene `dotnet test`
  su tre minuti invece che su dieci.
- **Lo scenario `pitch` non fa parte di `-Scenario all`** ed esce con 1 di proposito. Le bande
  diventano check veri solo con `--pitch-strict`, che ogni fase accende sulle bande che dichiara
  chiuse.
- **Decisa il 3 settembre 2026 (§9):** `MatchResolver.Resolve` **tiene il filmato** — è il replay
  che `GetReplayAsync` serve ai membri della lega e che il client disegna, e mezzo secondo di CPU su
  una giornata da cinque incontri non è un costo. Il **formato** è stato sistemato lo stesso giorno:
  `PositionStream.Pack()`/`Unpack()` più `Infrastructure/Leagues/ReplayStore`, 2077 → 794 KB.
- **Residuo da cancellare a mano:** `_to_delete/` (i due tarball usati per portare i sorgenti nel
  container). La shell del container non ha il permesso di cancellare nella cartella collegata.

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

### Fase 1 — Unità e base temporale ✅ FATTA

Sim a 10 Hz, velocità reali, decimazione dello stream, attrito palla corretto.
Rompe golden master e replay salvati → `MatchEngine.Version` da 3 a 4 e rigenerazione.
**Fatta il 3 settembre 2026 — il resoconto, con i numeri, è in §8.**

### Fase 2 — Forma: formazione e blocco ✅ FATTA

- riscrittura di `AnchorY`: si dispone **la linea come unità** (raggruppamento per banda X,
  poi spaziatura realistica dentro la linea), non ogni ruolo sull'intera larghezza.
- `HomeSpot` sostituito da una **trasformata di blocco**: baricentro squadra (X da palla +
  mentalità, Y da palla) **con tetto** (scorrimento laterale ≤ 10 m), più offset di formazione,
  più **compattezza** (fattore di compressione in fase difensiva).
- **linea difensiva esplicita**: i centrali tengono una X comune = max(linea del fuorigioco,
  16 m dalla propria porta, X palla − 10 m).
- spaziatura verticale fra i reparti mantenuta a 10–12 m.
- scorrimento asimmetrico: la punta scala molto più di quanto il centrale salga.

**Fatta il 3 settembre 2026 — il resoconto, con i numeri, è in §9.**

### Fase 3 — Difendere: zona e trigger ✅ FATTA

*Qui il video cambia faccia: finché la marcatura è a uomo su tutti e dieci, la sagoma di chi
difende È la sagoma di chi attacca, e le tre bande difensive ancora rosse sono sue (§9).*

- il cervello di squadra assegna un compito per tick invece della marcatura universale:
  `Presser` (1, a volte 2) · `Cover` (copre il pressante) · `Marker` (solo avversari nel nostro
  terzo o in area, o in corsa alle spalle) · `Zone` (tutti gli altri: tengono la posizione di
  blocco).
- `Separate()` estesa agli **avversari**: i corpi non si compenetrano più.
- trigger di pressing da istruzione `Pressing`: zona di innesco + distanza + situazione
  (retropassaggio, controllo sporco, ricezione sull'esterno).

### Fase 4 — Decisioni con la palla, guidate dagli attributi ✅ FATTA

*Scritta e misurata il 4 settembre 2026 — il resoconto, con i numeri, è in §11.*

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

### Fase 5 — Il regolamento (modulo arbitro) ✅ FATTA

*Scritta e misurata il 5 settembre 2026 — il resoconto, con i numeri, è in §12.*

- **fuorigioco**: linea calcolata a ogni tick, passaggio verso un uomo oltre la linea →
  bandierina e punizione.
- **palla fuori anche a giocatore in possesso**, con punto di attraversamento sub-tick →
  rimessa / corner / rinvio corretti.
- **falli**, punizioni con barriera, **rigori**, **cartellini** e squalifiche. Rende leggibili
  `Aggression` e `Tackling`.
- **cambio campo all'intervallo**.

### Fase 6 — Inversione della causalità ✅ FATTA

*Scritta e misurata l'8 settembre 2026 — il resoconto, con i numeri, è in §13.*

- `MatchSimulator` produce punteggio ed eventi. `MatchDirector` e i knob `Chance*` eliminati.
- `MatchEngine` retrocesso a percorso veloce per il mondo di sfondo, ricalibrato dall'harness
  perché le tabelle di lega restino plausibili.
- determinismo conservato: stessa RNG seminata, stesso ordine di iterazione, matematica intera.

### Fase 7 — Dati sulle prestazioni ✍️ SCRITTA (da verificare, §14)

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

## 8. Fase 1 — fatta: unità e base temporale

### Cosa è cambiato

**La configurazione ora è fisica.** Prima ogni velocità era scritta *per tick* e un tick durava
cinque secondi, il che rendeva il modello dimensionalmente incoerente (§1.2). Adesso tutto ciò che
contiene del tempo è scritto **al secondo** o **in millisecondi**, e i conteggi di tick su cui il
modello lavora sono *derivati* dalla frequenza:

    TicksPerSecond      10        → un tick è 100 ms, 600 tick al minuto, 54.000 a partita
    StreamTicksPerFrame 5         → un fotogramma ogni mezzo secondo: 120 al minuto, 10.801 a partita
    TicksOfMs(ms)                 → ogni durata (attesa su palla morta, blocco dopo un contrasto,
                                    tempo di possesso, finestra del director) passa di qui

Cambiare `TicksPerSecond` e ogni durata conserva il proprio significato. È questa la differenza fra
una base temporale e un numero magico.

**Le velocità sono quelle vere.**

| | prima | ora |
|---|---|---|
| giocatore, punta massima | 0,7 m/s | **5,5-8,5 m/s** da `Pace` |
| giocatore, andatura fuori palla | — (unica velocità) | **42%** della punta: si sprinta solo per la palla |
| passaggio, massimo | 1,2 m/s | **26 m/s** |
| tiro | 2,2 m/s | **32 m/s** |
| rapporto palla/giocatore | 2:1 | **3,06:1** (il bersaglio del §1.2 era 3-4:1) |

**L'attrito è scritto al secondo.** `BallSpeedKeptPermillePerSecond = 740`: la palla conserva il 74%
della velocità dopo un secondo, e il valore *per tick* si ricava con una ricerca binaria intera
sulla stessa ricorrenza che la simulazione poi esegue — nessuna radice, nessun logaritmo, nessun
float, quindi la risposta è identica su .NET, Mono e IL2CPP. È moltiplicativo e non una sottrazione
costante perché una palla vera è frenata dal rotolamento **e** dall'aria: perde circa 7 m/s² a
venticinque metri al secondo e meno di uno a passo d'uomo, che è la forma che dà un'esponenziale e
non una costante.

**Il flusso è decimato.** La fisica gira a 10 Hz, il replay no: un fotogramma ogni cinque tick, cioè
2 Hz di tempo partita, che alla compressione 30× già in uso fa **60 fps** di riproduzione. Un'azione
sulla palla viene registrata nello spazio dei FOTOGRAMMI, non dei tick, così renderer, analizzatore
e test continuano ad avere un solo indice e nessuna regola di conversione.

**`MatchEngine.Version` è 4** e il golden master si è spostato (era atteso: `MatchReportHasher`
include il flusso). I replay salvati in v3 non sono più disegnabili — il client li rifiuta già da
solo, perché confronta con `MatchEngine.Version` invece che con un numero scritto a mano.

### La misura, prima e dopo (200 partite, tattiche neutre, seed 20260803)

| Lettura | Fase 0 | **Fase 1** | Calcio vero |
|---|---|---|---|
| **km per giocatore** | **1,28** | **11,30** ✅ | 9,5-11,5 |
| rapporto palla/giocatore | 2:1 | **3,06:1** ✅ | 3-4:1 |
| gol | 2,52 | **2,52** | 2,6-2,8 |
| tiri | 25,1 | **25,1** | 22-28 |
| passaggi | 96 | **1201** | 900-1100 |
| precisione passaggi | 57,7% | 56,1% | 78-86% |
| rimesse laterali | 5,0 | **76,5** | 35-45 |
| corner | 0,0 | 0,8 | 9-12 |
| falli / fuorigioco | 0 | 0 | 20-26 / 2-4 |
| passo peggiore in un fotogramma | — | 42 dm = uno scatto in mezzo secondo | — |

**I gol e i tiri sono identici alla fase 0, cifra per cifra.** Non è una coincidenza ed è la cosa
più importante di questa pagina: il modello di risultato 1.4 non è stato toccato, e il rifacimento
del movimento non ha spostato il punteggio di una virgola. Le due letture che la fase possedeva —
i chilometri e il rapporto fra le velocità — sono dentro la banda del calcio vero. Le altre restano
il lavoro delle fasi 2-5, e sono nominate lì: la precisione dei passaggi è l'errore di esecuzione
(fase 4), corner e falli sono il regolamento (fase 5), e i 76 rimessa a partita sono una squadra
che non tiene ancora la palla (fasi 3-4).

### Il difetto vero che la fase ha trovato

**Un tiro che usciva sul fondo laterale perdeva del tutto il proprio esito.** In `ResolveOutOfPlay`
il ramo della rimessa laterale chiamava `Restart`, che azzera il tiro in volo *in silenzio*: la
parata o l'errore che il tabellino dichiarava non venivano mai registrati. Con la palla a 1,2 m/s
non succedeva quasi mai; a 32 m/s un tiro esce di lato di continuo, e la misura lo ha inchiodato
subito — **149 esiti su 190 mostrati, il 78%**, contro una soglia dell'85%. Corretto con
`SettleStrayStrike`: **190 su 190**. Una fase che cambia le unità non scopre difetti nuovi, scopre
difetti che le vecchie unità nascondevano.

### Il costo, detto onestamente

    modello di risultato (generatePositions: false)   0,9 ms per partita
    con il flusso di movimento                       523 ms per partita   (Release, .NET 8, container)

Il bersaglio scritto nel §5 era **< 30 ms**. Non ci siamo, e non è una distanza che si chiude
limando: 54.000 tick × 22 agenti sono 1,2 milioni di aggiornamenti, cioè 25 ns ciascuno per stare
nei 30 ms — meno di quanto costi il solo controllo di separazione dai dieci compagni. Cosa è stato
fatto, con la misura accanto (e la lezione di sempre: **le prime due ipotesi erano sbagliate e solo
il profilo le ha smentite**):

- la radice quadrata intera era la prima indiziata: sostituita con il metodo cifra-per-cifra, senza
  divisioni. **Guadagno: 84 ms su 871.** Non era lei.
- il profilo ha detto dov'era davvero: `Move` e il cervello di squadra. Allora — confronti sui
  **quadrati** delle distanze ovunque serva solo sapere chi è più vicino; nessuna radice quando il
  bersaglio è già a un passo; `PassSafe` che chiede *quanta strada ha fatto la palla* (una lettura
  in tabella) invece di *a che tick arriva* (una ricerca binaria per ogni avversario di ogni
  candidato); il punto di appoggio ricalcolato una volta al secondo su una griglia più piccola; la
  posizione di blocco calcolata una volta invece che due per ogni difensore; e le decisioni —
  chi marca chi, dove si corre — su un orologio da 2 Hz separato dalla fisica a 10 Hz, perché un
  difensore non ricambia uomo dieci volte al secondo.
- risultato: **871 → 523 ms**. Per tick siamo circa **quattro volte** più economici di prima; è il
  numero di tick, ×50, a fare il resto.

Cosa vuol dire in pratica: una partita **guardata** costa mezzo secondo di CPU, che nessuno vede.
`dotnet test` non è toccato dove conta, perché le ~11.000 partite di bilanciamento girano già con
`generatePositions: false` e costano 0,9 ms l'una; **le suite di identità sono state messe in
regola nella stessa fase** — `MatchResimTests` e `PrematchPlanTests` spegnevano il flusso per i
loro sweep (fino a 800 partite in un solo test) e lo tengono acceso su un caso singolo, che è dove
l'identità del *quadro* va davvero dimostrata.

**Deciso il 3 settembre 2026 (vedi §9): il filmato resta, il formato no.**
`server/Infrastructure/Leagues/MatchResolver.Resolve` genera
il flusso per ogni partita di lega risolta lato server. Adesso sono 523 ms **e 2 MB di JSON** a
partita (era 202 KB): una giornata di dieci partite sono 5 secondi di CPU e 20 MB in `ReplayJson`.
Se quei replay servono, il prezzo è quello; se no, è un parametro da girare. Il §3 prevede `int16`
+ rigenerazione lato client, che toglierebbe il grosso — ma è una modifica al contratto del flusso,
non alla fase 1.

### Cosa è stato toccato

| File | Cosa |
|---|---|
| `Config/BalanceConfig.cs` | il blocco movimento riscritto in unità fisiche; durate in ms, velocità al secondo, tick derivati |
| `Match/Movement/MatchUnits.cs` | `PerTick`/`PerTickPerTick` (dove l'unità di lunghezza incontra la base temporale), `DistanceSq`, `Cap` senza radice |
| `Match/Movement/MatchBall.cs` | attrito per secondo, tabella cumulativa del rotolamento, aiuti passati da statici a d'istanza |
| `Match/Movement/MovementGeometry.cs` | radice quadrata intera cifra-per-cifra |
| `Match/Movement/MatchSimulator.cs` | velocità reali, andatura contro scatto, deadband d'arrivo, cadenza delle decisioni, decimazione del flusso, `SettleStrayStrike` |
| `Match/Movement/MovementTactics.cs` | i tempi di possesso vengono dai millisecondi |
| `Match/MatchEngine.cs` | `Version = 4` |
| `Match/PositionStream.cs` | documentato: un "tick" del flusso è un FOTOGRAMMA |
| `Sim.Core.Tests/Match/*` | soglie ricavate dalla config invece che scritte a mano; l'invariante della palla verificata alla risoluzione della simulazione; sweep di identità senza flusso |

### Cosa deve girare sulla macchina dell'utente

    .\tools\build-simcore.ps1
    dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "console;verbosity=detailed"
    dotnet test server/Api.Tests/Api.Tests.csproj
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html
    .\tools\balance.ps1            # ogni numero degli altri scenari deve restare identico

**Il golden master calcolato qui su .NET 8 è `0x214A70906A5180AC`** (prima `0xBD336A9B5F155792`;
v2 `0xCDEA5A2F7B9E5CF6`), già ripuntato in `server/Api.Tests/SimulationDeterminismTests.cs`,
`server/Application/Simulation/SimulationService.cs`, `docs/ops/runbook.md` e
`docs/store/release-checklist.md`. Se `dotnet test server/Api.Tests` non è d'accordo, il valore
buono è quello stampato da `[DeterminismCheck]`: sarebbe una differenza fra runtime, non un errore
di codice — ma alla fase 0 le due esecuzioni erano identiche cifra per cifra, quindi non dovrebbe
succedere.

`.\tools\balance.ps1` senza `-Scenario pitch` **non deve muovere un solo numero**: il motore di
risultato non è stato toccato e le ~11.000 partite di quegli scenari girano senza flusso. Se si
muove qualcosa, il movimento sta consumando casualità dove non deve.

### Il run dell'utente (3 settembre 2026) e l'unico rosso

**Verde:** Api.Tests 240/240 · `[server-determinism] 0x214A70906A5180AC` **identico al valore
calcolato su .NET 8** · `balance.ps1` 28/28 con ogni cifra ferma · `pitch` 7/19 letture in banda
(erano 6) e **268 ms per partita** · `[movement] worst single-tick step 40dm (cap 42)` ·
`[passes] median 16m p99 52m` · `[swerve] 0 over 68929 moving ticks` ·
`[outcomes] goals 21/21 · saves and misses 190/190`.

**Il rosso, uno solo, ora CHIUSO:** `SavedShots_StopAtTheKeeper_AndMissesGoWide` — *"a miss must
not end up in the net either"*.

**La causa, misurata e non ipotizzata.** Un'azione veniva archiviata sul fotogramma
`floor(tick / 5)`, cioè **fino a 0,4 secondi PRIMA di essere avvenuta**. Con la vecchia base
temporale un tick era un fotogramma e la cosa non esisteva; adesso un tiro a 32 m/s in quel mezzo
secondo percorre tredici metri, quindi il test guardava il pallone quando il gol non era ancora
entrato. Contato: **18 gol su 23 e 4 errori su 131** finivano "fuori posto" per questo solo motivo,
e il test si ferma al primo che incontra. Le parate erano 0 su 186 perché il portiere è fermo.

**La correzione è nel flusso, non nel test:** `Record` arrotonda ora **per eccesso**, al primo
fotogramma pari o successivo all'azione. È anche ciò che il renderer deve disegnare — annunciare un
passaggio nel fotogramma in cui la palla è ancora ai piedi del passatore è la telecronaca che
racconta cose non ancora successe. L'arrotondamento resta monotono, quindi l'ordine delle azioni è
intatto. Di conseguenza `Passes_AreFootballLength` misura ora dal fotogramma PRECEDENTE l'azione,
dove la palla è ancora ai piedi di chi la gioca.

**Verificato dall'utente:** `SavedShots_StopAtTheKeeper_AndMissesGoWide` e
`Passes_AreFootballLength` **entrambi verdi** — `[shots] 23 goals · 186 saves · 131 misses`,
`[passes] 7214 · median 16m · p99 53m · longest 90m`. La suite Sim.Core è **334/334**.

**La lezione, ed è generale:** decimare un flusso non è solo scartare fotogrammi, è decidere *a
quale istante* ogni evento appartiene. Un'azione archiviata sul fotogramma precedente racconta un
mondo che non è ancora successo, e più il tick è veloce più la bugia è grande. Ogni fase che cambia
la base temporale deve chiedersi la stessa cosa.

### Aperto, per scelta

- **Il costo per partita guardata** (523 ms contro i 30 del piano): da giudicare insieme sui numeri
  della macchina dell'utente, che gira .NET 10 su hardware suo e non un container condiviso.
- **La dimensione del flusso** (2 MB di JSON): `int16` e/o rigenerazione lato client, §3.
- Il visore HTML della fase 0 adesso carica 10.801 fotogrammi invece di 1.081: da guardare che il
  browser lo regga.
- I 76 rimessa laterali e gli 0,8 corner a partita sono forma e regolamento, cioè fasi 2, 3 e 5.


---

## 9. Fase 2 — fatta: forma, formazione e blocco

### Cosa è cambiato

**La formazione è disposta per LINEE, non per ruoli.** La larghezza veniva assegnata un ruolo alla
volta, ogni gruppo spalmato per conto suo sull'intera ampiezza: in un 4-3-3 i due centrali
finivano su 250 e 750 permille, **trentaquattro metri l'uno dall'altro**, con in mezzo un buco
grande quanto mezzo campo, mentre i terzini stavano su 118 e 882 (§1.3). Un calciatore però non
si dispone rispetto a chi ha il suo stesso mestiere: si dispone **in una linea**, accanto a chi
c'è dentro. Adesso l'unità è la linea (`FormationLineByRole`): i ruoli che stanno sulla stessa
banda vengono disposti insieme, gli esterni sul margine di fascia, gli altri distanziati di
quattordici metri l'uno dall'altro attorno al centro.

| Reparto | Prima | Ora |
|---|---|---|
| Difesa a 4 (4-3-3) | FB 8 · **CB 17** · **CB 51** · FB 60 m | FB 8 · **CB 27** · **CB 41** · FB 60 m |
| Centrali fra loro | **34,0 m** | **13,9 m** |
| Centrocampo a 4 (4-4-2) | tutti e quattro fra 8 e 60 m (spalmati) | 8 · 27 · 41 · 60 m, gli esterni sono esterni |
| Trequarti (4-2-3-1) | AM 500 solo, W 118/882 | AM 500 · W 118/882 sulla stessa linea del centravanti |

La regola che chiude i casi difficili è una sola e sta in una riga: **da quattro uomini in su, i
due di fuori di una linea sono i suoi esterni comunque si chiamino**. È così che i "wide
midfielder" del 4-4-2, che il repo codifica CM per farli finire nel secchio di centrocampo, stanno
sulla fascia e non stipati in mezzo. Tutti e sei i moduli continuano a fare il **round-trip** su
`ZoneRole.Resolve`: un preset pulito resta il punto fisso della mappa, quindi il "reset al cambio
modulo" restituisce ancora esattamente i ruoli del preset.

**Il blocco ha sostituito `HomeSpot`.** Una squadra ha tre proprietà separate — **dov'è**, quanto è
**profonda**, quanto è **larga** — e prima erano tutte e tre lo stesso numero.

- **Dov'è** ha un solo grado di libertà: **l'altezza della linea difensiva**. È la linea che un
  allenatore istruisce davvero ("tieni alto", "abbassati"), e ogni altra linea è distanziata in
  avanti a partire da lei — per cui la difesa a quattro **è una linea per costruzione** invece che
  per fortuna. La linea sta sopra il minimo di 16,5 m dalla propria porta (il limite dell'area),
  sotto il massimo di 52 m, e prende il **40%** dell'avanzamento della palla: non il cento per
  cento, perché una linea che segue la palla metro per metro fa due chilometri a partita che
  nessun difensore fa. Il terzo vincolo del piano — la linea del fuorigioco — non esiste ancora:
  arriva con la fase 5, e qui è detto e non fatto.
- **Quanto è profonda** dipende da quante linee ha davvero il modulo, non da una tabella: un 4-4-2
  ne ha tre e difende a 20 m, un 4-2-3-1 ne ha quattro e difende a 30. `LineRank` conta le linee
  **occupate**, così la profondità del blocco segue la forma.
- **Quanto è larga** dipende solo dal possesso: il 66% dell'ampiezza nominale senza palla, il 118%
  con la palla.

**Lo scorrimento asimmetrico esce da solo**, e non è un caso: la distanza fra le linee si misura
**in avanti a partire dalla linea difensiva**, quindi perdere la palla fa arretrare la punta di
una quarantina di metri e sposta i centrali di quindici. È esattamente quello che si vede quando
una squadra si richiude nel proprio blocco, e in codice è una moltiplicazione.

**La lerp verso la palla è sparita.** Al suo posto una **traslazione con tetto**: il blocco scorre
verso la corsia della palla, come pezzo unico, e mai più di dieci metri dal centro del campo
(§1.4). Prima era una lerp *per giocatore* verso la Y della palla, senza tetto — chi era lontano
dalla palla si spostava **più** di chi era vicino, per cui la squadra non traslava, si restringeva
del 25% e basta. Adesso il tetto è un'invariante che il test afferma; il centro squadra misurato
non esce mai da 15,7 m dalla metà campo.

**Il calcio d'inizio è finalmente legale.** Regola 8: si batte con entrambe le squadre nella
propria metà campo. Prima i due blocchi si accavallavano attorno al centrocampo e il fotogramma
del fischio d'inizio era regolamentare solo per caso — misurato, **tre uomini per parte** stavano
nella metà campo avversaria. Adesso zero, e il battitore sul dischetto è l'unico ammesso.

**Due cose sono cambiate perché la misura le ha chieste, non perché il piano le prevedesse.**

- **La forma ha inerzia.** Con qualche centinaio di cambi di possesso a partita (che è un difetto
  del passaggio, non della forma: se ne occupa la fase 4), passare di scatto dalla sagoma difensiva
  a quella offensiva spostava ventidue uomini di sei metri di lato e ritorno, ogni volta. Adesso la
  squadra ci mette **quattro secondi** ad aprirsi e altrettanti a chiudersi.
- **Un uomo cammina verso un posto vicino e trotta verso uno lontano.** Il modello aveva due sole
  andature, trotto e scatto, e faceva partire il trotto anche per una correzione di cinque metri.
  Entro quindici metri l'andatura adesso scala con la distanza che resta. È la modifica che da
  sola vale **due chilometri e mezzo a partita per giocatore**, ed è anche il motivo per cui un
  uomo che insegue un punto che vibra non lo insegue più: al passo lo media, che è quello che
  succede su un campo.

### La misura, prima e dopo (200 partite, tattiche neutre, seed 20260803)

| Lettura | Fase 1 | **Fase 2** | Calcio vero | |
|---|---|---|---|---|
| **larghezza del blocco in possesso** | 34,6 | **42,0 m** | 40-60 | ✅ **chiusa** |
| **profondità del blocco in possesso** | 55,7 | **48,3 m** | 30-50 | ✅ **chiusa** |
| **buco più grande fra due uomini** | 16,6 | **14,9 m** | 0-15 | ✅ **chiusa** |
| larghezza del blocco senza palla | 32,1 | **38,8 m** | 28-42 | ✅ tenuta |
| km per giocatore | 11,30 | **10,83** | 9,5-11,5 | ✅ tenuta |
| un avversario entro 3 m | 12,4% | 19,0% | 5-25 | ✅ tenuta |
| centrali fra loro (nominale) | 34,0 | **13,9 m** | 8-14 | ✅ |
| uomini in campo avversario al fischio d'inizio | 3 per parte | **0** | 0 | ✅ |
| profondità del blocco senza palla | 55,1 | 47,8 m | 22-38 | ❌ fase 3 |
| dispersione della linea difensiva | 14,4 | 9,2 m | 0-6 | ❌ fase 3 |
| **gol** | 2,52 | **2,52** | 2,6-2,8 | invariati |
| **tiri** | 25,1 | **25,1** | 22-28 | invariati |

**Letture in banda: da 7/19 a 10/19.** E ancora una volta la cosa più importante della pagina è la
riga che non si muove: **gol e tiri sono identici cifra per cifra** a quelli della fase 0 e della
fase 1. Il modello di risultato non è stato sfiorato, e `balance.ps1` senza `pitch` è stato
verificato con un **diff riga per riga** contro l'albero pre-fase — `[sweep]`, `[match-fatigue]`,
`[fitness->result]`, difficoltà, mondo, risolutore di sfondo: tutto identico, tranne i millisecondi.

### Le tre bande difensive che restano rosse, e perché non sono di questa fase

Vale la pena essere precisi, perché "profondità 47,8 contro una banda 22-38" sembra un fallimento e
non lo è. Contato dentro una partita, **il 41% dei tick di un uomo lo passa a marcare** e lo **0,0%**
lo passa a tenere la zona: `AssignMarks` assegna tuttora un marcatore a **ognuno** dei dieci
avversari, ovunque si trovi (§1.5), e il ramo "tieni la posizione di blocco" del movimento non viene
eseguito mai. Quindi la sagoma di chi difende **è la sagoma di chi attacca**, traslata di cinque
metri e ottanta verso la propria porta — ed è per questo che le due righe della misura si somigliano
a un decimo di metro:

    difendendo   larghezza 38,8 m   profondità 47,8 m
    attaccando   larghezza 42,0 m   profondità 48,3 m

Non è un difetto della forma: è la marcatura. Le tre bande si chiudono quando i compiti diventano
`Presser` / `Cover` / `Marker` / `Zone`, che è precisamente la fase 3 — e a quel punto il ramo che
oggi non gira mai diventerà quello che gira quasi sempre.

Quello che la fase 2 poteva fare su quel fronte l'ha fatto: **un uomo della linea difensiva tiene
la linea**. Prende il suo avversario in larghezza, ma non lo segue in profondità — se lo facessero
tutti e quattro la difesa smetterebbe di essere una linea e diventerebbe quattro duelli separati.
La abbandona solo per chi le è già passato dietro, che è l'unica cosa per cui la linea esiste. La
dispersione della linea è scesa da 14,4 a 9,2 m; il resto lo prende la marcatura.

### Il costo

    modello di risultato (generatePositions: false)   invariato
    con il flusso di movimento    523 ms → 497 ms per partita   (container)
                                  268 ms → 292 ms per partita   (macchina dell'utente)

Nel container è **meno** della fase 1, perché la posizione di blocco adesso si calcola **una volta
a tick per squadra** e viene letta da tutto il resto invece di essere ricalcolata per ogni uomo a
ogni domanda. Sulla macchina dell'utente è invece salita del **9%**, 268 → 292 ms: il risparmio del
ricalcolo non copre del tutto quello che il blocco fa muovere in più. È il nove per cento che pesa
sui quarantuno minuti di `dotnet test`, ed è nominato sopra.

### Cosa è stato toccato

| File | Cosa |
|---|---|
| `Tactics/FormationGeometry.cs` | `AnchorY` riscritta per linee; `LineOf` e `LineRank` nuove |
| `Config/BalanceConfig.cs` | la tabella delle linee, la geometria della linea, e il blocco: altezza, inseguimento della palla, distanza fra le linee, larghezza con e senza palla, tetto laterale, transizione, andatura di avvicinamento |
| `Match/Movement/MatchSimulator.cs` | `UpdateBlock` (nuova) e `HomeSpot` riscritta; la linea difensiva dentro `MarkSpot`; il calcio d'inizio nella propria metà; l'andatura che scala con la distanza |
| `Match/Movement/MovementTactics.cs` | la mentalità è l'altezza della linea in decimetri, non due spostamenti in permille |
| `Match/MatchEngine.cs` | `Version = 5` |
| `Sim.Core.Tests/Match/BlockShapeTests.cs` | **nuovo**: la linea è una linea, il blocco è un blocco, il calcio d'inizio è legale |
| `client/.../FormationLayout.cs` | non ha più la sua copia delle costanti: chiama `FormationGeometry`, così la schermata Tattiche e il campo non possono più divergere in silenzio |
| `Api.Tests` · `SimulationService` · `runbook.md` · `release-checklist.md` | golden master ripuntato |

### Cosa deve girare sulla macchina dell'utente

    .\tools\build-simcore.ps1
    dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "console;verbosity=detailed"
    dotnet test server/Api.Tests/Api.Tests.csproj
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html
    .\tools\balance.ps1            # ogni numero degli altri scenari deve restare identico

**Il golden master è `0xB3C30BEEAA5781B2`** (prima `0x214A70906A5180AC`) — calcolato nel container
su .NET 8/10 e **confermato identico dalla .NET 10/Windows dell'utente** — già ripuntato in `server/Api.Tests/SimulationDeterminismTests.cs`,
`server/Application/Simulation/SimulationService.cs`, `docs/ops/runbook.md` e
`docs/store/release-checklist.md`.

**Come è stata verificata, e cosa questo dice del metodo.** Il container in cui la fase è stata
scritta non arriva a NuGet, quindi NUnit non si è potuto restaurare e `dotnet test` non è stato
eseguito lì: le asserzioni di `BlockShapeTests` sono state provate ricompilandole come programma a
sé contro Sim.Core. **Il run dell'utente ha poi dato 580/580** e — questo è il punto — le righe di
diagnostica dei nuovi test hanno stampato **gli stessi numeri del programma sostitutivo**
(`centre worst 15,7 m`, `attacking width 42,3 m depth 48,5 m back line spread 8,5 m`), il che è la
prova che il sostituto misurava davvero la stessa cosa e non una sua parente. Nessuno dei due posti
sospettati ha avuto qualcosa da ridire: né `Teams_FaceEachOther_AtKickoff`, né i due sweep di
posizionamento.

### Aperto, per scelta

- **Le tre bande difensive** restano alla fase 3, per il motivo misurato qui sopra.
- **Il ramo `Zone` del movimento gira lo 0,0% del tempo.** È codice morto finché la marcatura non
  diventa zonale: vale la pena saperlo prima di aprirlo.
- **`FindSupportSpot` calcola un punto solo per squadra** (§1.8): gli uomini di supporto corrono
  ancora tutti nello stesso posto, e sono l'8,3% dei tick. Restringe la larghezza in possesso più
  di quanto faccia la forma, ed è della fase 4.
- **La linea del fuorigioco** non entra ancora nel vincolo della linea difensiva: fase 5.
- **La cadenza del flusso.** Il codec è fatto (2077 → 794 KB); `StreamTicksPerFrame` da 5 a 10
  dimezzerebbe ancora, al prezzo di 30 fotogrammi al secondo invece di 60. Non tirata.
- **Dove vanno i 41 minuti di `dotnet test`**, che il `.trx` dirà in un comando.

---

## 10. Fase 3 — fatta: difendere, zona e trigger

### Cosa è cambiato

**Il cervello di squadra assegna un COMPITO, non un marcatore a testa.** `AssignMarks` metteva un
marcatore su **ognuno** dei dieci avversari, ovunque si trovasse (§1.5): dieci duelli individuali
che vagavano per il campo, il ramo "tieni la posizione di blocco" del movimento che girava lo
**0,0%** del tempo, e — la conseguenza che la fase 2 aveva misurato e rimandato qui — una sagoma
difensiva che **era** la sagoma offensiva spostata di cinque metri e ottanta. Adesso ogni uomo
riceve un compito solo per tick di cervello:

| compito | quota dei tick difensivi | dove sta |
|---|---|---|
| va sulla palla (`Presser`) | 7,5% | addosso al portatore |
| copre chi ci è andato (`Cover`) | 8,9% | dal lato porta della palla, a 9,5 m |
| prende un uomo (`Marker`) | 11,8% *(era 41%)* | solo nel nostro terzo, o già dietro la linea |
| tiene la zona (`Zone`) | **71,5%** *(era 0,0%)* | al suo posto nel blocco |

Un avversario si marca **dove è pericoloso davvero**: dentro il nostro terzo, oppure quando ha già
passato la linea difensiva. Tutti gli altri sono una zona da tenere, non un uomo da inseguire. Il
**Cover** non esce mai dalla linea difensiva — un centrale che va a coprire è un centrale fuori
dalla linea, e la linea è esattamente ciò che si misura.

**`Separate()` respinge anche gli avversari.** Prima allontanava solo i compagni, quindi il
marcatore finiva letteralmente sopra il suo uomo. Il raggio è **deliberatamente più corto** della
distanza a cui il pressante si ferma sulla palla, così tenere i corpi separati non impedisce mai un
contrasto. Misurato sulla stessa partita, con la spinta spenta e accesa: il tempo passato entro un
metro da un avversario scende da **0,116% a 0,066%**.

**Il pressing è un TRIGGER.** L'istruzione `Pressing` adesso è una **zona di innesco** — fin dove
la squadra va a prendere il portatore, in decimetri dalla propria porta: 350 / 620 / 1050 — più due
situazioni che accendono il pressing fuori da quella zona: un **retropassaggio** (+50% di raggio per
tre secondi) e una **ricezione sull'esterno** (+20%). Misurato, lo spazio lasciato a un uomo che ha
la palla nel proprio terzo: **basso 6,76 m · medio 6,56 m · alto 5,96 m**, e la quota di tick
passati a pressare va da **5,7% a 9,7%**. Il terzo trigger che il piano nominava — il controllo
sporco — ha bisogno che l'errore di esecuzione esista, e quello è della **fase 4**: non è stato
finto.

### Le due cose che ha chiesto la misura, e che il piano non prevedeva

**(1) Il blocco si CHIUDE più in fretta di quanto si apra.** La fase 2 aveva dato inerzia alla
forma per farle smettere di sbattere di lato a ogni cambio di possesso, e l'aveva fatta simmetrica:
quattro secondi in entrambe le direzioni. Con un cambio di possesso ogni pochi secondi (che è un
difetto del passaggio, non della forma — fase 4) questo significava che una squadra che aveva
appena perso palla si portava dietro **la larghezza e la spaziatura offensive per quattro secondi**,
cioè per quasi tutto il tempo in cui difendeva. Adesso la chiusura è **1,5 secondi** e l'apertura
resta 4. Da sola questa riga vale 2,6 m di larghezza difensiva.

**(2) Un uomo sorpreso in avanti TORNA DI CORSA.** Fuori palla un calciatore trotta; la corsa di
recupero è l'unica cosa fuori palla per cui scatta davvero. Senza, il blocco ci metteva una decina
di secondi a riformarsi dopo ogni turnover, e ciò che l'harness misurava come "la squadra che
difende" era una squadra ancora sfilata dall'azione d'attacco.

### Come la misura ha smentito due ipotesi, di nuovo

Il primo run con i compiti a posto chiudeva la marcatura (41% → 20%) ma la profondità difensiva si
muoveva appena, da 47,8 a 45,3 m. **L'ipotesi ovvia — "sono i marcatori che sfilano la squadra" —
era sbagliata**, e il probe l'ha detto in una riga: le posizioni di zona coprivano **26 m**, gli
uomini ne coprivano **42,6**, e l'uomo più avanzato stava **12 m davanti al suo posto**. Non era la
forma: erano uomini che non ci arrivavano mai.

La seconda ipotesi — "camminano troppo piano quando sono vicini al posto" — è stata provata e
**pagata malissimo**: dimezzare la banda di andatura (`PlayerApproachDm` 150 → 60) ha guadagnato
0,8 m di profondità e costato **1,5 km a partita per giocatore**, portando i chilometri fuori banda.
Rimessa com'era. Quello che ha funzionato è stato misurare la **dispersione** invece della media:
lo scarto è rumore, non deriva, e il rumore si riduce comprimendo la forma nominale e la
transizione, non facendo correre di più la gente.

### La misura, prima e dopo (200 partite, tattiche neutre, seed 20260803)

| Lettura | Fase 2 | **Fase 3** | Calcio vero | |
|---|---|---|---|---|
| **profondità del blocco senza palla** | 47,8 | **36,2 m** | 22-38 | ✅ **chiusa** |
| **dispersione della linea difensiva** | 9,2 | **5,9 m** | 0-6 | ✅ **chiusa** |
| larghezza del blocco senza palla | 38,8 | 40,6 m | 28-42 | ✅ tenuta |
| buco più grande fra due uomini | 14,9 | 11,5 m | 0-15 | ✅ tenuta |
| un avversario entro 3 m | 19,0% | 13,0% | 5-25 | ✅ tenuta |
| larghezza del blocco in possesso | 42,0 | 42,9 m | 40-60 | ✅ tenuta |
| profondità del blocco in possesso | 48,3 | 39,4 m | 30-50 | ✅ tenuta |
| km per giocatore | 10,83 | 11,56 | 9,5-11,5 | ✅ tenuta (banda del check 9-12) |
| **gol** | 2,52 | **2,52** | 2,6-2,8 | invariati |
| **tiri** | 25,1 | **25,1** | 22-28 | invariati |

**Letture in banda: da 10/19 a 12/19, e sono le due che la fase dichiarava.** La riga più
importante resta quella che non si muove: **gol e tiri identici cifra per cifra** alla fase 0, alla
fase 1 e alla fase 2. Il modello di risultato non è stato sfiorato — il livello movimento pesca
dall'RNG **dopo** che il tabellino è deciso, ed è per questo che può cambiare faccia senza cambiare
un punteggio.

E per la prima volta le due righe della forma **non si somigliano più**: 40,6 × 36,2 difendendo
contro 42,9 × 39,4 attaccando. Alla fase 2 erano 38,8 × 47,8 contro 42,0 × 48,3, cioè la stessa
sagoma due volte.

### Il costo

    modello di risultato (generatePositions: false)   invariato
    con il flusso di movimento    483 → 444 ms per partita   (container, .NET 10)

**Meno** della fase 2, non di più: assegnare quattro marcature invece di dieci, e avere sette uomini
su dieci che stanno fermi al loro posto invece di inseguire qualcuno, costa meno di quello che
costava prima. Sulla macchina dell'utente il rapporto sarà diverso in valore assoluto (là la fase 2
girava a 292 ms), ma il segno dovrebbe reggere.

### Cosa è stato toccato

| File | Cosa |
|---|---|
| `Match/Movement/MatchSimulator.cs` | `AssignDuties` (sostituisce `AssignMarks`), `CoverSpot`, il trigger dentro `Pressing`, la corsa di recupero in `Move`, `Separate` estesa agli avversari, la chiusura asimmetrica in `UpdateBlock`, la disciplina di linea in `MarkSpot` |
| `Match/Movement/MovementTactics.cs` | l'istruzione `Pressing` porta anche la zona di innesco |
| `Config/BalanceConfig.cs` | il blocco "difendere": zona di marcatura, cover, trigger, separazione dagli avversari, corsa di recupero, chiusura della forma, spaziatura difensiva delle linee |
| `Match/MatchEngine.cs` | `Version = 6` |
| `Sim.Core.Tests/Match/DefensiveDutyTests.cs` | **nuovo**: la sagoma difensiva è sua, i corpi non si compenetrano, il blocco basso lo lascia giocare |
| `Api.Tests` · `SimulationService` · `runbook.md` · `release-checklist.md` | golden master ripuntato |

### Cosa deve girare sulla macchina dell'utente

    .\tools\build-simcore.ps1
    dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "console;verbosity=detailed"
    dotnet test server/Api.Tests/Api.Tests.csproj
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html
    .\tools\balance.ps1            # ogni numero degli altri scenari deve restare identico

**Il golden master è `0xABC7B41DC6F258C2`** (prima `0xB3C30BEEAA5781B2`), calcolato nel container su
.NET 10 e già ripuntato nei quattro posti. I replay salvati in v5 non sono più disegnabili: è
previsto, e il client li rifiuta da solo confrontando `MatchEngine.Version`.

**Come è stata verificata.** Il container non arriva a NuGet, quindi NUnit non si restaura: come
nelle fasi 1 e 2 lo **stub NUnit scritto a mano** compila l'INTERO `Sim.Core.Tests` (zero errori) e
un runner a riflessione esegue davvero le fixture che contano — `BlockShapeTests` **6/6** e i nuovi
`DefensiveDutyTests` **5/5**, con le righe `[duties]`, `[bodies]` e `[press]` stampate. Il modello di
risultato è stato ricontrollato dal suo harness: `Harness_EqualTeams_RealisticScores` stampa ancora
**2,44 gol/partita, 24,8% di pareggi, 48,4% di vittorie interne**, cioè la calibrazione accettata.

### E la domanda aperta della fase 2 ha una risposta: dove vanno i 41 minuti di `dotnet test`

Il runner a riflessione cronometra ogni test, e il namespace `Match` nel container costava
**1932 secondi — di cui 1806, il 93%, in QUATTRO test di `MatchEngineTests`**:

    839,1 s  Harness_HomeAdvantage_IsRealAndConfigurable
    419,8 s  Harness_EqualTeams_RealisticScores
    419,7 s  Harness_StrongBeatsWeak_70to80Percent
    127,0 s  Goals_AreScoredMostlyByAttackers

Sono le tre calibrazioni del modello di RISULTATO più il controllo sui marcatori: migliaia di
partite a sweep, e **ognuna di quelle partite costruiva anche un filmato che nessuno di quei test
guarda**, perché `new MatchEngine()` genera lo stream per default e dalla fase 1 una partita col
filmato costa mezzo secondo contro il millisecondo del modello di risultato.

**La correzione è una parola:** l'helper `Play` di `MatchEngineTests` costruisce il motore con
`generatePositions: false`, e solo `GoldenMaster_SameSeed_IdenticalReport` lo riaccende (è l'unico
di quel file che ha qualcosa da dire sul filmato). Misurato: **1806 s → 2,2 s**, e **ogni cifra
stampata è identica** — 2,44 gol/partita, 24,8% di pareggi, 48,4% di vittorie interne, 51,2% contro
41,8%. Che sia lecito non è un'opinione: `SkippingTheStream_LeavesTheResultUntouched` è il test che
lo afferma, e il golden master continua a coprire il filmato da `DeterminismCheckTests`.

Sulla macchina dell'utente gli stessi quattro test valgono grosso modo **7.000 partite × 268 ms ≈
31 minuti** dei 41 misurati: il `.trx` lo confermerà, ma il conto torna già.

### Aperto, per scelta

- **Il controllo sporco come trigger di pressing** aspetta l'errore di esecuzione: fase 4.
- **`FindSupportSpot` calcola un punto solo per squadra** (§1.8) — fase 4, come già scritto.
- **La linea del fuorigioco** non entra ancora nel vincolo della linea difensiva: fase 5.
- **Le rimesse laterali restano 82 a partita** (banda 30-50) e i corner mezzo: sono il regolamento
  e il possesso, fasi 4 e 5.
- **Gli uomini stanno in media 7-8 m dal loro posto in zona.** Non è pigrizia del modello: con
  qualche centinaio di turnover a partita il blocco è quasi sempre in transizione. Si chiude quando
  il passaggio smette di regalare la palla — fase 4 — e allora la profondità difensiva scenderà
  ancora senza toccare la geometria.

---

## 11. Fase 4 — fatta: decisioni con la palla, guidate dagli attributi

Scritta e misurata nel container il 4 settembre 2026. Le due scelte prese con l'utente prima di
cominciare: **tutta la fase in un giro** (passaggio, dribbling e tiro insieme, perché sotto c'è
un'unica misura coerente) e **l'esito del tiro resta della timeline** — il modello risultato 1.4
continua a decidere gol, parata e fuori, e l'inversione della causalità resta dichiaratamente la
fase 6, con la sua ricalibrazione. Quello che la fase 4 aggiunge al tiro è la QUALITÀ: dove va la
palla, e se il portiere la trattiene.

### Cosa è cambiato

**Il giocatore con la palla PESA le sue opzioni, invece di scendere una scala fissa.** Prima era
`TryPass` → altrimenti spazza se sei pressato → altrimenti conduci. Adesso passaggio, conduzione e
spazzata sono quotati **nella stessa moneta**: *i decimetri di avanzamento che si aspetta,
meno quanto vale a chi la riceve perderla nel punto in cui la perderebbe*. È quella moneta comune
che rende le tre cose confrontabili, e prezza il rischio **dove la palla finisce**, non dove sta
adesso — ed è il motivo per cui una spazzata può essere la risposta giusta: sposta la perdita di
quaranta metri, dove costa una frazione di quello che costa sul proprio limite dell'area.

- **il valore di ogni opzione** = `completamento × (guadagno + valore del possesso) − rischio ×
  costo del turnover lì`. Il costo del turnover è una tabella per terzo di campo
  (`TurnoverCostDm` 900 / 480 / 240 dm).
- **quanto di quel rischio il giocatore VEDE** viene da `Positioning`: un cattivo lettore di gioco
  gioca il pallone che *sembra* migliore. Nasce così una seconda dimensione, distinta da chi il
  pallone lo sa colpire.

**Il passaggio non è più un test sì/no, e la marcatura sul ricevente non è più gratis.** Il vecchio
`PassSafe` giudicava la corsia e **escludeva gli ultimi 7,5 metri davanti al ricevente**, così un
uomo marcato a due metri era invisibile all'unico test che veniva fatto. Misurato: **il 44,2% dei
passaggi finiva direttamente a un avversario.** Adesso ci sono tre domande separate — la corsia
(`LaneCompletion`, odds invece di un booleano), **lo spazio del ricevente** (prezzato, non ignorato)
e se quel giocatore quel pallone lo sa colpire.

**L'esecuzione sbaglia.** Errore laterale sulla linea del passaggio più errore sul peso, scalati da
`Passing`/`Technique` e dalla pressione subita, pescati come **media di due uniformi** perché quasi
tutte le palle siano vicine alla loro linea e quella storta sia rara. La deviazione perpendicolare
è un'operazione intera esatta: nessun angolo, nessuna trigonometria, niente che possa arrotondare
diversamente su un altro runtime.

**Il duello.** Il contrasto era un dado piatto a 45‰ per tick di contatto, identico per chiunque.
Adesso il config decide il RITMO dei duelli e i due uomini decidono chi li vince: `Dribbling`,
`Technique`, `Strength` e `Pace` del portatore contro `Defending`, `Positioning` e `Pace` dello
sfidante, col rapporto **al quadrato** (un 60 non batte un 30 a testa o croce, e un 90 non è
ingiocabile). Metà dei duelli vinti sono un pallone tolto, l'altra metà una palla che schizza via e
se la giocano entrambi — che è da dove arrivano le palle vaganti.

**Il tocco della conduzione** non è più fisso: corto quando è pressato, lungo quando ha spazio,
allungato da `Dribbling` e `Pace`, e **limitato dal campo che ha davanti**.

**Il tiro ha una qualità** (`BallSkill.ShotQualityPermille`): una lettura in stile xG di distanza,
angolo, corpi in mezzo e di chi lo calcia. La fase 4 la spende su **dove va la palla** (il gol di un
buon finalizzatore è piazzato dentro il palo, quello di uno scarso passa vicino al portiere; il suo
errore è di un metro, quello dello scarso è in curva) e sulle **mani del portiere**: `Goalkeeping`
dice quanto spesso la trattiene, e una respinta rimette la palla viva in area invece di chiudere
l'azione. L'esito resta della timeline. Il modello è scritto come componente a sé proprio perché la
fase 6 lo prenda com'è e gli faccia decidere il gol.

### Il difetto vero che la fase ha trovato, e che non era nel piano: il PESO del passaggio

Il modello non aveva alcuna nozione del peso di un pallone. Ogni palla veniva colpita con la forza
che **raggiunge** il bersaglio nel tempo di volo nominale, e poi continuava a rotolare quasi alla
velocità con cui era partita: **un passaggio di undici metri rotolava cinquantasei metri.** Il
ricevente aveva due tick per infilarsi sulla sua traiettoria, o era andata.

Si vede in una traccia, presa da una partita vera — per ogni mezzo secondo, la distanza del
ricevente dalla palla:

    passaggio 11 m, ricevente con 12 m di spazio:  10 → 6 → 6 → 9 → 13 → 18 → 19 → 20 m

`MatchBall.ForceToArrive` calcola la forza che consegna la palla **e la fa morire mentre arriva**,
con una ricerca binaria sulle due tabelle da cui la palla è costruita (terreno coperto in n tick,
velocità rimasta dopo n tick), che sono monotone per costruzione. E c'è un **ottimo misurabile**:
troppo lenta e la corsia la taglia, troppo forte e supera l'uomo.

| velocità d'arrivo | passaggi che arrivano |
|---|---|
| 5,5 m/s | 56,2% |
| 9,5 m/s | 66,3% |
| **11 m/s** | **68,7%** |
| 12,5 m/s | 66,3% |
| 14 m/s | 64,8% |

### Due cose che la misura ha chiesto, e che il piano non prevedeva

**(1) Il ricevente corre a INCONTRARLA.** Andava al punto in cui la palla era stata mirata, e restava
lì mentre gli passava a due o tre metri — e due o tre metri sono tutto il raggio di controllo di un
calciatore. Adesso usa lo stesso inseguimento predittivo del `chaser`. **Un passaggio dentro dodici
metri di spazio libero si perdeva ancora nel 27% dei casi**: era questo.

**(2) Una corsa di supporto è uno SCATTO, non uno sprint di novanta minuti.** Con il possesso che
sopravvive, i compagni fanno molte più corse d'appoggio, e correvano tutte a tavoletta: **12,07 km a
giocatore, con il più laborioso a 19,5 km** — che non è calcio. Adesso è a tavoletta finché il
terreno è da coprire e un trotto una volta arrivato nello spazio: **11,31 km, il più laborioso 16,6**.

### La misura, prima e dopo (200 partite, tattiche neutre, seed 20260803)

| | fase 3 | fase 4 |
|---|---|---|
| gol | 2,52 | **2,52** |
| tiri (in porta) | 25,1 (14,9) | **25,1 (14,9)** |
| passaggi | 1347 | **877** ✅ |
| precisione passaggi | 54,6% | **78,5%** ✅ |
| palloni lunghi · cross | 98,8 · 111,6 | 39,4 · 25,8 |
| conduzioni · spazzate | 34,0 · 283,0 | 279,3 · 295,2 |
| contrasti vinti · intercetti | 180,9 · 318,8 | 394,6 · 218,8 |
| rimesse · rinvii | 81,8 · 40,2 | 18,4 · 46,8 |
| nessuno sulla palla | 38,0% | 18,5% |
| palla per terzo | 30,1 / 38,8 / 31,2 | 36,7 / 25,5 / 37,8 |
| km per giocatore | 11,56 | 11,31 |
| difendendo (largh. × prof.) | 40,6 × 36,2 | 38,8 × 31,8 |
| linea difensiva · buco più grande | 5,9 · 11,5 | 5,6 · 10,7 |
| attaccando (largh. × prof.) | 42,9 × 39,4 | 44,4 × 38,6 |
| **letture in banda** | **12/19** | **14/19** |
| palla tenuta su una linea | 550,8 tick | **48,7 tick** |
| costo per partita (container) | 444 ms | 504 ms |

### E la differenza fra due giocatori, finalmente misurabile

Gli stessi ventidue uomini, giocati due volte: da una parte `Passing`, `Technique` e `Dribbling` a
**88**, dall'altra a **24**, e **tutto il resto identico** — Pace, Strength, Defending, Positioning,
il modulo, le istruzioni, il seme. Prima di questa fase le due squadre giocavano una partita
indistinguibile.

| | palla | nell'ultimo terzo | perse nella propria trequarti |
|---|---|---|---|
| passaggio + tecnica + dribbling | **56,6%** vs 43,4% | 154 vs 111 | 18,1 vs 21,0 |
| solo passaggio e tecnica | 53,9% vs 46,1% | 160 vs 121 | 22,4 vs 28,2 |
| solo dribbling | 54,4% vs 45,6% | 152 vs 130 | 20,9 vs 23,2 |

Le due abilità lavorano **ognuna per conto suo**, quindi nessuna delle due sta portando l'altra. E
sul campionato così com'è generato, la percentuale di passaggi che arrivano sale con `Passing`:
76,3% (54-65) → 77,4% (66-77) → **78,5%** (78-89).

### Un'ipotesi smentita dalla misura, e vale tenerla

**"Errore di esecuzione più grande ⇒ più passaggi sbagliati" è FALSO a livello di campionato.**
Portando `PassErrorMaxPermille` da 190 a 300 a 420 la precisione di lega si muove di tre decimi di
punto (76,5% → 76,8% → 76,5%). Il motivo è che il modello di DECISIONE compensa: un passatore
scarso prezza il proprio errore e sceglie palloni che sa colpire. Non cambia **quanti** passaggi
arrivano, cambia **quali** vengono giocati — ed è esattamente ciò che si vede in un campionato vero,
dove il difensore centrale di una squadra modesta completa l'85% dei suoi passaggi, tutti di lato.
Un test che avesse cercato la differenza fra due giocatori nella sola percentuale di passaggi
riusciti non l'avrebbe trovata: sta nel **possesso** e nella **progressione**.

### I test nuovi

`Sim.Core.Tests/Match/BallDecisionTests.cs` (6), tutti letti **dalla figura** — dal position stream,
come li legge l'harness — e non da un numero interno che potrebbe essere sbagliato di suo:
`BetterFootballers_KeepTheBall_AndGetItForward` (la ✅), `Passing_AloneMovesTheGame`,
`Dribbling_AloneMovesTheGame` (su 16 semi, perché è il più stretto dei tre effetti), `Passes_ArriveLikeRealFootball` (banda 76-88%),
`TheKeepersHands_DependOnHim` (respinte 30,0% da un portiere a 90, **52,7%** da uno a 20) e
`TheSameSeed_PlaysTheSameMatch`.

### Il costo

**444 → 504 ms a partita nel container (+13%).** Il conto lo fa la valutazione delle opzioni: per
ogni decisione, dieci compagni × tre varianti, e per ognuna la corsia contro undici avversari più lo
spazio del ricevente. Gira però **solo sui tick in cui qualcuno decide davvero** (il portatore, e
solo quando la sua attesa è scaduta), non su ogni tick di ogni giocatore. Sulla macchina dell'utente
la cifra assoluta sarà diversa — la fase 3 girava a 263 ms lì contro i 444 del container — ma il
segno dovrebbe tenere.

### Come è stata verificata qui

`dotnet-sdk-10.0` si installa dall'archivio Ubuntu; `Sim.Core` non ha package reference, quindi un
csproj `net10.0` di comodo lo compila offline sotto `TreatWarningsAsErrors`. Lo **stub NUnit scritto
a mano** compila TUTTO `Sim.Core.Tests`, e un runner a riflessione **ha eseguito l'intera suite:
355 test verdi, zero rossi, in 4 minuti e 28 secondi** (i 349 della fase 3 più i 6 nuovi). Lo
scenario `pitch` è stato ricostruito come progetto console contro il solo Sim.Core: è da lì che
viene ogni cifra qui sopra.

### Aperto, per scelta

- **la palla passa poco dal mezzo**: 36,7 / 25,5 / 37,8 per terzo, contro 30,1 / 38,8 / 31,2 della
  fase 3. Prima la palla viveva in mezzo al campo perché lì la si perdeva in continuazione; adesso
  le squadre progrediscono. Il residuo è una domanda per chi difende — il blocco recupera la palla
  troppo tardi — e non si chiude con un knob del passaggio (misurato: `PossessionValueDm` da 55 a
  140 sposta la quota del terzo centrale di un punto).
- **rimesse laterali 18,4, sotto banda.** Le rimesse che mancano sono quelle che il regolamento non
  rileva ancora: un portatore che esce dal campo viene solo riportato dentro (§1.7). È della fase 5,
  e la stessa fase 5 le riporterà su.
- **corner 0,3, fuorigioco 0, falli 0**: il regolamento, fase 5.
- **tiri in porta 14,9 su 25,1**: è la quota `SavedShareOfFailedChancesPercent` del modello
  risultato, non della figura. Si sistema quando la causalità si inverte (fase 6).
- il tiro non decide ancora il gol, per scelta: fase 6.

### Cosa deve girare sulla macchina dell'utente

1. `.\tools\build-simcore.ps1` — **obbligatorio**, `shared/` è cambiato.
2. `dotnet test` → attesi **595** verdi (589 + i 6 `BallDecisionTests`). Incollare le righe
   `[ball-skill]`, `[passing]` e `[keeper]`.
3. `dotnet test server/Api.Tests` → il golden master nuovo è già puntato; se `[DeterminismCheck]`
   stampa un altro valore, quello stampato è quello da tenere.
4. `.\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\replay.html` — e poi
   **aprire `replay.html` e GUARDARE**: i numeri sono in banda, l'occhio è l'altra metà.
5. `.\tools\balance.ps1` — ogni cifra degli altri scenari deve essere identica.

### File toccati

`Match/Movement/BallSkill.cs` (**nuovo**: tutto il modello puro — errore di esecuzione, odds della
corsia, ricezione, valore di un'opzione, duello, qualità del tiro, mani del portiere),
`Match/Movement/MatchSimulator.cs` (`Act` che pesa le opzioni, `FindPass`/`PlayPass` al posto di
`TryPass`, `LaneCompletion` al posto di `PassSafe`, `PressurePermille`, `TurnoverCostDm`,
`CarryValue`/`ClearValue`/`CarryTouchDm`/`CarryTarget`, il duello dentro `ResolveControl`, la
qualità e le mani del portiere dentro `TakeShot`/`ResolveControl`, il ricevente che va a incontrarla,
lo scatto di supporto), `Match/Movement/MatchBall.cs` (`ForceToArrive`),
`Config/BalanceConfig.cs` (il blocco "decisioni con la palla"; via `NominalPassSpeedDmPerSecond`,
`DribbleDistanceDm` e `TackleChancePermille*`, che il modello nuovo non usa più),
`Match/MatchEngine.cs` (`Version = 7`), **nuovo** `Sim.Core.Tests/Match/BallDecisionTests.cs`.


---

## 12. Fase 5 — fatta: il regolamento (modulo arbitro)

*Scritta e misurata nel container il 5 settembre 2026 (200 partite, .NET 8) e **verificata
dall'utente lo stesso giorno**: 604 test verdi, golden master identico cifra per cifra, e lo scenario
`pitch` che riproduce ogni numero a 318 ms a partita. Il resoconto della sua esecuzione è nel blocco
di stato in cima al file.*

*Dopo quella verifica è arrivata la correzione della barriera (§12.5), che ha cambiato il golden
master in **`0x222F723B4993ED25`**: i numeri di questo capitolo sono quelli DOPO la correzione, e
**anche quelli sono stati verificati dall'utente, il 6 settembre 2026** — 604 test verdi in 577,6 s,
i due hash identici, `pitch` 19/20 + 3/3 a 311,7 ms a partita con ogni numero uguale a quello del
container, e `balance.ps1` 28/28 con ogni cifra invariata.*

Quattro delle cinque letture ancora fuori banda alla fine della fase 4 erano dell'arbitro, ed erano
tutte a zero o quasi per lo stesso motivo: **nessuno arbitrava**. La palla usciva solo se era
LIBERA — un giocatore che la portava oltre la linea veniva semplicemente riportato dentro (§1.7), e
il check di contratto dell'harness contava quel difetto a 48,7 tick a partita — non esisteva alcuna
linea del fuorigioco, e un contrasto poteva solo essere vinto o perso, mai sbagliato.

| lettura | fase 4 | fase 5 | banda |
|---|---|---|---|
| rimesse laterali | 18,4 | **33,4** | 30-50 |
| corner | 0,3 | **10,7** | 8-13 |
| fuorigioco | 0,0 | **4,1** | 1,5-5 |
| falli | 0,0 | **20,9** | 18-28 |

E il rosso di contratto del §1.7 è **chiuso**: `a held ball is never sitting on a line of the pitch`
legge **0,0 tick a partita**, e per la prima volta da quando esistono **i tre check di contratto
passano tutti e tre** (lo scenario `pitch` esce con codice 0). Le letture dentro la banda del calcio
vero passano da **14/19 a 19/20**: l'unica ancora fuori è **tiri in porta 15,0 su 25,1** (banda
6-11), che è `SavedShareOfFailedChancesPercent` del modello risultato e non del campo — è della fase
6, come il gol.

**E il modello risultato non è stato toccato, ed è la cosa che questa fase rischiava di più**: un
rigore o un tiro deviato che segna sarebbe un gol che il tabellino non ha. Un rigore *prende in
prestito* l'occasione della timeline quando ce n'è una, un gol della timeline non è respingibile, e
il check `the picture and the result agree on the score` passa su tutte le 200 partite. Ogni
calibrazione del modello risultato torna **identica cifra per cifra** ai valori accettati nelle fasi
precedenti: `Avg goals/match 2,44 | draws 24,8% | home wins 48,4%`, `Strong wins 82%`,
`[condition-live] 2,59 gol/partita, pareggi 21,3%`, `[counter] 56,0%`,
`[familiarity] 49,3% contro 23,9%`, `[sweep] top 53,8%`, `[positioning-line] 487→513 / 467→513`,
`[positioning-width] 549 contro 531`, `[match-fatigue] 481 → 580`,
`[fitness->result] 517 contro 318`.

### Cosa fa l'arbitro

**Palla fuori anche a giocatore in possesso, con punto di attraversamento sub-tick.** Il passo di
ogni giocatore viene ora registrato *prima* che il campo lo riporti dentro (`_stepToX/_stepToY`), e
il portatore che esce viene giudicato su quel passo: quale linea ha attraversato per prima e in che
punto, con la stessa interpolazione che `MatchBall.CrossingOf*` usa da sempre per una palla libera.
La rimessa si batte dal punto in cui la palla è uscita davvero; se il portatore l'ha portata oltre la
propria linea di fondo è un corner, oltre quella che attacca è una rimessa dal fondo.

Tre conseguenze che sembrano dettagli e non lo sono:

- **la palla ai piedi sta SUL CAMPO** (`BallToFeet`). Un giocatore può essere mezzo passo oltre la
  linea con la palla ancora in gioco al piede interno; quello che non può succedere è che la palla
  stia appoggiata su una linea, perché la legge la chiama rimessa. Era il grosso di quel rosso:
  l'uomo più esterno del blocco in attacco veniva schiacciato SULLA linea laterale e una palla giocata
  a lui restava lì nei suoi piedi per secondi.
- **la forma, il passaggio e la conduzione stanno dentro la linea** di `TouchlineInsetDm` (0,8 m). Un
  calciatore non sta *sulla* linea: metà di lui sarebbe fuori dal campo. L'inset è deliberatamente
  più corto di una falcata a velocità massima, così chi la vuole portare fuori può ancora farlo.
- **le rimesse e i corner si battono da un passo dentro la linea.** Le leggi le fanno battere dalla
  linea con il battitore FUORI dal campo, e un'immagine dall'alto di ventidue punti non ha dove
  metterlo. Lo stesso punto per l'occhio, e mantiene "una palla tenuta non sta mai su una linea" un
  invariante vero invece di un check che scatta ogni volta che l'arbitro azzecca una rimessa.

**Fuorigioco (Legge 11).** La linea è il penultimo avversario (`OffsideLineDepth`), con la palla e la
metà campo come vincoli aggiuntivi. Il modello del *perché* i fuorigioco succedono è la parte che
conta: **il portatore gioca quello che crede sia buono, l'arbitro giudica quello che era**. Il
passatore legge la linea con un errore che dipende dal suo `Positioning`
(`PerceivedOffsideLine`, `OffsideJudgementDm`), scarta ogni opzione oltre la linea che *crede* ci
sia, e quando ciò che crede è sbagliato la bandierina si alza. Un passatore che leggesse la linea
perfettamente non metterebbe mai nessuno in fuorigioco — ed è esattamente perché la lettura era zero
prima di questa fase. La bandierina si alza al momento del passaggio, si risponde solo se uno dei
segnalati tocca la palla, e la punizione si batte dal punto in cui era quando la palla è stata
giocata.

**Falli, cartellini, rigori e barriera (Leggi 12, 13, 14).** Il contrasto vinto non è più
automaticamente palla vinta: quanto spesso il piede arriva invece della palla dipende dal
`Defending` di chi contrasta (`FoulPermilleOfChallenges*`). È ciò che rende un buon marcatore utile
nell'immagine e non solo nel modello risultato: **lo stesso contrasto, fatto da un difensore
peggiore, è una punizione contro** — misurato, 8,5 falli a partita da una squadra di marcatori a 90
contro 12,1 da una a 20. Dentro la propria area resta in piedi (`FoulInBoxPermille`), che è perché i
rigori sono rari (0,11 a partita) senza essere impossibili. Il cartellino: una quota dei falli è
ammonizione, raddoppiata per il fallo cinico — quello su un uomo lanciato con poca gente davanti
(`StoppedAnAttack`) — e chi è già ammonito diventa molto più prudente (`BookedCarePercent`, senza il
quale il motore espelleva qualcuno in tre partite su quattro). La seconda ammonizione è rossa per
legge e non per manopola, **e un espulso lascia il campo**: cammina fino alla linea laterale
all'altezza del centrocampo (a passo d'uomo — niente teletrasporti, il contratto dello stream lo
vieta) e ogni ciclo che legge il campo lo salta, così la sua squadra finisce la partita davvero in
dieci — le consegne difensive si dividono fra dieci, la linea del fuorigioco si traccia su dieci, e
ci sono dieci uomini a cui passarla. La barriera e i nove metri e quindici sono in `RetreatSpot`.

**Il rigore, e il nodo del punteggio.** Il gol appartiene al modello risultato 1.4 fino alla fase 6:
un rigore *non può* inventare un gol. Quindi se la squadra ha una sua occasione della timeline
abbastanza vicina, **il rigore È quella occasione**, battuta adesso e con il suo esito — lo stesso
meccanismo con cui il direttore tiene insieme l'immagine e il tabellino da tre fasi. Quando non c'è
nulla da rivendicare, il portiere para o il tiro va fuori (`PenaltySavedPercent`). È un residuo
dichiarato, ed è della fase 6.

**Il tiro può essere RESPINTO da un corpo** (`BlockStrike`). Un quarto dei tiri di una partita vera
finisce su un difensore, e da lì viene una fetta dei corner: il motore non aveva modo di mettere un
corpo davanti alla palla. Un gol della timeline non è respingibile — il punteggio non si tocca —
tutto il resto sì, l'esito della timeline viene comunque registrato, e dove finisce la palla è affare
dell'arbitro come per ogni altro pallone libero.

**La deviazione tiene la linea della palla** (`Deflect`). Prima veniva spedita ordinatamente in
avanti a forza fissa qualunque cosa stesse facendo la palla, così un cross deviato di stinco usciva
come una spazzata pulita di quaranta metri e i corner non arrivavano mai. Adesso la palla
*continua*: la linea d'arrivo, sparpagliata di lato e a volte rimandata indietro, alla quota di
velocità che il config le concede. E vicino alla propria porta o alla linea laterale **non è una
deviazione, è una spazzata**: di testa o di punta, verso qualsiasi posto che non sia la propria
porta, e ogni difensore del mondo la mette volentieri dietro per un corner o in fallo laterale.

**La spazzata ha un errore di esecuzione, ha bisogno di spazio davanti, ed è colpita per coprire la
distanza a cui è mirata.** La fase 4 aveva dato l'errore a ogni palla *passata* e aveva lasciato la
spazzata esatta al centimetro; ed era colpita alla forza massima qualunque fosse la distanza, per cui
un pallone spazzato dalla propria area attraversava tutto il campo e usciva: **ventisette delle
trentanove rimesse dal fondo a partita erano quello** (adesso sono 17,4 in tutto, e otto su dieci
vengono da un tiro fuori bersaglio, che è quello che sono nel calcio vero). E non è nemmeno
un'opzione se davanti non c'è campo — un attaccante negli ultimi venti metri non "spazza" verso una
linea che sta attaccando, che è una rimessa dal fondo per costruzione. Infine il difensore ha la
decisione che gli mancava: **buttarla fuori**, dietro per un corner o in fallo laterale, che è il
modo più comune di far uscire un pallone nel calcio.

**Il portiere respinge anche DIETRO** (`KeeperParryBehindPercent`): una parata su un tiro forte
diventa un corner tanto spesso quanto rimette la palla in gioco.

**Intervallo (Legge 7).** Al 45' si fischia — e il fischio *aspetta*: non mentre un tiro è in volo e
non mentre la timeline ha un'occasione da giocare, perché un arbitro non fischia con la palla in
area. Poi la palla torna sul centro del campo e il secondo tempo lo batte la squadra che non ha
battuto il primo, con entrambe le squadre nella propria metà. Il calcio d'inizio adesso è anche
*legale*: nessuno fa più un movimento di sostegno nella metà campo avversaria mentre l'arbitro
aspetta di fischiare (era esattamente quello che facevano i sostegni della squadra in possesso), chi
è nella metà sbagliata torna di corsa, chi batte sta un passo *dietro* la palla invece che sul punto
del centro, e la linea di metà campo è un **muro**: il passo con cui uno la attraverserebbe viene
annullato, invece di riportarlo indietro di trenta metri in un tick (che è l'unica cosa che il
contratto dello stream vieta, vedi `PositionStreamTests.NobodyTeleports`). Il fischio è anche
un'azione dello stream, `BallActionKind.HalfTime`, così la commentary può dire "intervallo".

**Il CAMBIO DI CAMPO è nell'IMMAGINE, non nella simulazione — e adesso si vede.** Il campo è
simmetrico, il fattore campo è un bonus di forza e non un posto, e ogni parte del modello porta con sé
la propria direzione d'attacco (`MovementGeometry.Direction`): invertire i due lati dentro Sim.Core
non cambierebbe niente del calcio giocato e obbligherebbe ogni consumatore dello stream — l'analizzatore,
il dump HTML, il renderer del client — a invertirle di nuovo. Quindi la simulazione tiene le squadre
sullo stesso lato per novanta minuti e **sono i due visori a specchiare il secondo tempo**, che è dove
l'occhio guarda:

- `tools/BalanceHarness/PitchDump.cs` legge il frame del fischio dall'azione
  `BallActionKind.HalfTime` e ruota il campo di 180° da lì in poi (`mx`/`my`/`ownGoalIsLeft`): il
  riquadro squadra, la linea difensiva, la palla e la sua scia seguono da sole perché passano tutte da
  quelle tre funzioni, la scia si interrompe al fischio (una linea disegnata attraverso il cambio di
  campo è lo specchio, non la palla), e il **tabellone** in cima al campo — una fascia sopra il
  rettangolo, non sopra il gioco, perché una scritta nell'angolo in alto a sinistra sta esattamente
  dove sta la bandierina del corner — porta punteggio corrente, **minuti E secondi** (un frame è mezzo
  secondo: senza i secondi il numero sembra fermo) e il tempo in corso, "1st half" oppure
  "2nd half · ends changed";
- `client/Assets/Scripts/MatchView/MatchRenderer.cs` fa la stessa cosa nei due soli metodi da cui
  passa ogni punto disegnato (`BallPixel`/`PlayerPixel` → `Pixel`), con la guardia che **non
  interpola mai attraverso l'intervallo** (due frame in sistemi di coordinate specchiati, mescolati,
  farebbero scivolare ventidue uomini per un frame).

Ruotano ENTRAMBI gli assi, perché è quello che il cambio di campo è: lo stesso calcio visto dall'altra
linea laterale. **Nessuna modifica a Sim.Core**, quindi golden master e test restano dove sono.

### La misura (200 partite, tattiche neutre, seed 20260803)

    goals 2.52   shots 25.2 (15.0 on target)   passes 877 at 78.2% accuracy
    long balls 29.2   crosses 41.3   dribbles 297.5   clearances 257.6
    tackles won 364.4   interceptions 207.7
    throw-ins 33.4   corners 10.7   goal kicks 17.5   offsides 4.1   fouls 20.9
    yellow cards 2.85   red cards 0.14   penalties 0.16
    possession home 50.4%   nobody on the ball 19.5% of frames
    ball by third (home->away) 35.7% / 27.3% / 37.0%
    ground covered 11.07 km per player (busiest 16.45, laziest 8.92)

    defending  width 38.7 m   depth 32.1 m   back line spread 5.9 m   biggest hole 10.8 m
    attacking  width 45.2 m   depth 39.6 m   back line spread 5.3 m   biggest hole 12.9 m

    19/20 inside the band · 3/3 contract checks PASS · 684.2 ms a partita

**I nuovi test** sono `Sim.Core.Tests/Match/RefereeTests.cs` (9), e ognuno stampa la sua riga:

    [laws-restarts] 37.0 throw-ins · 11.0 corners · 17.5 goal kicks a match, and 0 frames in
                    30 matches with a held ball on a line
    [laws-offside]  4.2 offsides a match, 50 flags checked
    [laws-fouls]    21.9 fouls · 2.95 yellows · 0.15 reds · 0.10 penalties a match (437 checked)
    [laws-tackling] a side of 90 tacklers gave away 8.5 fouls a match and 1.20 cards;
                    a side of 20 tacklers 11.2 and 1.60
    [laws-cards]    10 men sent off in 40 matches
    [laws-halftime] the interval checked in 6 matches

`[laws-tackling]` è **la ✅ della fase**, ed è la controparte di `[ball-skill]` della fase 4: gli
stessi ventidue calciatori, due volte, con il `Defending` di una squadra alzato e quello dell'altra
abbassato e tutto il resto identico. Gli altri quattro test fissano la *legge* e non la taratura: che
la rimessa vada dalla parte giusta, che la bandierina appartenga alla squadra che passava e non si
alzi mai nella propria metà, che un fallo fermi il gioco e restituisca la palla, che un espulso non
tocchi più il pallone. E `TheReferee_NeverChangesTheScore` fissa la cosa che l'arbitro non può fare.

**Le righe del livello movimento che si sono mosse, ed è previsto:** `[duties]` 348 contrasti e 210
intercetti a partita (erano 402 e 235: il gioco si ferma più spesso), difendendo 39,1 × 32,2 e
attaccando 45,5 × 39,5; `[movement]` palla ai piedi di qualcuno per il **78%** della partita (era
81%); `[keeper]` respinte 47,2% da un portiere a 90 contro 60,3% da uno a 20 (erano 30,0 e 52,7 —
adesso una parte dei tiri viene respinta da un difensore prima che il portiere ci arrivi, e un tiro
murato conta come non trattenuto); `[shape]` il centro squadra al massimo 15,2 m fuori dalla mediana
(era 13,5); `[bodies]` 0,129% → 0,078%; `[passing]` 879 passaggi a partita al 77,2%.
`[movement] worst single-tick step 40dm (cap 42dm)` è invariato: **nessun corpo si teletrasporta**,
espulsi compresi.

**`[press]` legge 5,58 / 5,70 / 5,09 m** (basso / medio / alto): la monotonia fra basso e medio resta
persa come alla fase 4, l'estremo tiene, ed è la domanda della fase 8 come già scritto lì.

### 12.5 La barriera — il difetto che ha trovato l'occhio, non il numero

Le venti letture erano in banda e i tre check passavano, e la barriera **non era una barriera**: tre
uomini sparpagliati a cinque metri dalla palla. È il motivo per cui questa fase si chiude anche
guardando e non solo misurando. Le cause erano tre, tutte nel livello movimento e **nessuna nel
regolamento**, e le ho trovate misurando dopo che guardare non era bastato:

1. **la separazione di squadra apriva la barriera.** La regola "non stare addosso a un compagno" del
   blocco vuole 6,2 m fra due uomini; una barriera ne vuole 0,8, e vinceva la separazione. → chi è
   in barriera è **esente** dalla separazione di squadra (`_inWall`), e `WallSpacingDm` passa da 8 a
   **20** (spalla a spalla, non sovrapposti).
2. **la barriera inseguiva se stessa.** I tre uomini venivano ri-ordinati per distanza dalla palla a
   OGNI TICK: chi arrivava primo veniva sostituito da chi era più vicino adesso, e nessuno arrivava
   mai. → `FormWall(side)` assegna gli slot **una volta sola**, al fischio, e non li cambia più.
3. **lo sterzo era sbagliato per stare su un punto, in entrambe le marce.** Di corsa superava il
   punto di 8 m e poi gli girava attorno; al trotto strisciava, e la banda morta del movimento
   parcheggiava gli uomini 3 m prima — la barriera stava a **5 m** dalla palla invece che a 9,15.
   → `WalkTo(k, tx, ty)`: nessuna inerzia, nessuna banda morta, e il passo tagliato perché non
   possa oltrepassare il punto. È quello che adesso piazza la barriera, i difensori che arretrano,
   il battitore della punizione e il calcio d'inizio — **ogni piazzamento a palla ferma**. Il gioco
   in movimento non è toccato.

Ne è caduta fuori una quarta: `UpdateBlock` **teneva** il blocco anche a un calcio d'inizio, e
lasciava le ali 1,9 m dentro la metà sbagliata. Un calcio d'inizio adesso **ri-forma** il blocco
invece di tenerlo.

**Verificato con i numeri, non con l'occhio**: ogni difensore della barriera sta a **91-95 dm dalla
palla** — 9,1-9,5 m, i 9,15 della regola — e ci resta. La misura del capitolo è quella dopo la
correzione: 364 test verdi, 19/20 in banda, 3/3 check PASS. **Golden master →
`0x222F723B4993ED25`** (era `0x436E4440B6350A7B` nella verifica precedente): è cambiato lo stream,
non il modello risultato — i gol restano 2,52. File toccati: **`MatchSimulator.cs` e
`BalanceConfig.cs`**, nient'altro.

**Verificata dall'utente il 6 settembre 2026**, con gli stessi quattro comandi della sezione "Come si
verifica questa fase": `build-simcore` a posto, `dotnet test` **604/604 verdi in 577,6 s**,
`[DeterminismCheck]` e `[server-determinism]` entrambi **`0x222F723B4993ED25`** (il suo .NET 10
contro il .NET 8 del container: **la determinatezza fra runtime regge anche la correzione**), lo
scenario `pitch` che esce con codice 0 a **311,7 ms a partita** riproducendo **ogni singolo numero**
del container — rimesse 33,4, corner 10,7, fuorigioco 4,1, falli 20,9, gol 2,52, tiri 25,2 (15,0),
passaggi 877,4 al 78,2% — e `balance.ps1` **28/28 con ogni cifra invariata**, che è la prova che la
barriera non ha toccato il modello risultato. Le righe `[laws-*]` tornano al decimale: 37,0 rimesse /
11,0 corner / 17,5 rinvii con 0 frame di palla tenuta su una linea, 4,2 fuorigioco, 21,9 falli · 2,95
gialli · 0,15 rossi · 0,10 rigori, 8,5 falli da una squadra di 90 di contrasto contro 11,2 da una di
20, 10 espulsi in 40 partite, 6 intervalli.

### Cosa resta aperto, per scelta

- **tiri in porta 15,0 su 25,1** (banda 6-11): è `SavedShareOfFailedChancesPercent` del modello
  risultato, cosmetico per definizione, e si sistema quando la causalità si inverte (fase 6). È
  anche la ragione per cui `--pitch-strict` resta spento: 19 bande su 20 sono chiuse, e la ventesima
  è della fase che viene.
- **il rigore che non può segnare** quando la timeline non ha un'occasione da prestargli: stessa
  radice, stessa fase.
- **le squalifiche**: i cartellini stanno nell'immagine (nello stream, con il loro tipo di azione).
  Portarli nella carriera — `MatchReport`, il tabellino, la squalifica alla giornata dopo — vuol dire
  toccare `MatchEventType`, che è la timeline del modello risultato, e la timeline è della fase 6.
  È il primo pezzo del wiring client (5b).
- **`Aggression` non esiste nel dominio.** Il piano la citava insieme a `Tackling`: in questo gioco
  gli attributi sono dieci e quello del contrasto è `Defending`, che è ciò che il fallo legge.
  Aggiungerne uno nuovo toccherebbe generazione, allenamento e valutazione — cioè ogni golden master
  del mondo, non solo quello del motore — e non è di questa fase.
- **un espulso sta sulla linea laterale all'altezza del centrocampo**, dentro le coordinate del
  campo: l'analizzatore lo conta ancora fra i dieci di movimento, quindi una partita con un rosso ha
  la larghezza del blocco leggermente gonfiata. Con 0,14 rossi a partita è dentro il rumore, ed è
  scritto qui perché si sappia.

### Come si verifica questa fase

    .\tools\build-simcore.ps1
    dotnet test
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchStrict -PitchDump .\replay.html
    .\tools\balance.ps1        # gli altri scenari NON devono muoversi di un numero

Attesi — **confermati dall'utente il 5 settembre 2026 sul corpo della fase e di nuovo il 6 settembre
2026 dopo la correzione della barriera (§12.5), che ha cambiato il golden master**: **364 test verdi** in
`Sim.Core.Tests` (604 in totale con `Api.Tests`), `[DeterminismCheck]` e `[server-determinism]` che
stampano **`0x222F723B4993ED25`**, lo scenario `pitch` che esce con **codice 0** e 19/20 in banda, e
`balance.ps1` 28/28 con ogni cifra invariata. Il file `.cs` nuovo (`RefereeTests.cs`) ha bisogno del
`.meta` di Unity al primo import. Resta l'occhio: aprire `replay.html` e guardare le rimesse, i
corner, le punizioni con la barriera e l'intervallo.

---

## 13. Fase 6 — fatta: l'inversione della causalità

*Scritta e misurata il 7-8 settembre 2026. Decisioni con l'utente prese prima di scrivere una riga:
(1) **lo stream è la verità** — le partite con `generatePositions: true` prendono gol ed eventi dal
campo, il resto del mondo resta sul modello a minuti; (2) **vantaggio casa, condizione e stanchezza
vanno portati sul campo**, o la partita che l'utente gioca è l'unica della sua lega senza; (3) **si
tara il campo sul modello**, non il modello sul campo, così `balance.ps1` resta 28/28 con ogni cifra
invariata ed è la prova che il resto del gioco non si è mosso.*

### Cosa era, e cosa è

Il difetto che l'intero rifacimento esiste per togliere è il §1.1: **la partita che guardavi non era
la partita che contava**. `MatchEngine` decideva punteggio ed eventi con un modello statistico a
minuti; poi `MatchDirector` apriva una finestra di venticinque secondi prima di ogni occasione e
accendeva dei super-poteri temporanei sulla squadra che "doveva" segnare — raggio di pressing ×2,6,
probabilità di tackle ×2,6, fame di verticalizzazione ×2,2, cinque metri e mezzo di raggio in più
su ogni palla vagante — perché la palla arrivasse al marcatore già eletto. E se non arrivava,
gliela metteva in mano.

**Adesso il campo produce tutto.** Un uomo con la palla pesa il TIRO accanto al passaggio, alla
conduzione e alla spazzata, nella stessa moneta in cui sono quotati tutti: i decimetri di progresso
in avanti. Lo colpisce con la precisione che il suo Tiro e la sua Tecnica gli concedono. Un
difensore può murarlo, il tuffo del portiere può arrivarci e il suo Portiere può non bastare, e un
gol è la palla che passa la linea fra i pali. **Il referto lo scrive quella partita**, non lo
illustra.

`MatchDirector` è **cancellato**, e con lui ogni manopola `Chance*`.

### Come è fatto

**Il tiro come decisione.** `ShootValue` quota il gol a `GoalValueDm` per le probabilità che il
giocatore si dà — la sua lettura dell'occasione: distanza, angolo, corpi in mezzo, e il suo
finalizzare — meno quello che perdere la palla lì costa. È esattamente la forma che la fase 4 aveva
dato al passaggio e alla conduzione, e per questo le quattro opzioni sono confrontabili. **Non deve
avere ragione**: l'esito lo decidono la palla, il portiere e i pali, e la differenza fra i due è che
cos'è un cattivo finalizzatore.

**Il tiro come traiettoria.** Dove MIRA è dentro il palo, tanto più stretto quanto migliore è
l'occasione; dove VA è quello più un errore che il suo Tiro e la sua Tecnica gli tolgono e la
difficoltà dell'occasione gli aggiunge, tirato come l'errore di un passaggio — due uniformi mediate,
così quasi tutti i tiri sono vicini alla loro linea e quello selvaggio è raro. Se la traiettoria
passa fra i pali il tiro è **nello specchio** e da quel momento è palla del portiere e di nessun
altro; se va fuori non è di nessuno, esce, ed è rinvio dal fondo. Un portiere non raccoglie una
palla che passa un metro fuori dal suo palo: la guarda uscire.

**La parata come due domande.** Il TUFFO dice se ci è arrivato — raggio in più sul raggio di
controllo che hanno tutti, quasi tutto guadagnato col Portiere e un po' tolto dalla qualità del
tiro. La PARATA dice se arrivarci è bastato: `KeeperStopPercent`, il suo Portiere contro quel tiro.
Farla solo geometria satura — un tuffo lungo abbastanza da coprire gli angoli para tutto, uno corto
abbastanza da essere battuto non arriva a niente — e quindi sono due cose separate.

**Il muro come cosa a sé.** Un tiro murato non è né parato né sbagliato: il calcio lo conta come una
terza cosa, e adesso lo fa anche il referto (`BallActionKind.Block`, appesa in fondo all'enum come le
azioni dell'arbitro della fase 5, così i replay salvati prima continuano a significare quello che
significavano).

**Il referto scritto dal campo.** `RecordGoal` / `RecordSave` / `RecordMiss` sono l'inversione vista
da fuori: il quadro non illustra più un referto scritto prima di lui, **lo scrive man mano**. Ed è
per questo che il check "il quadro e il risultato sono d'accordo sul punteggio" ha smesso di essere
un contratto che poteva rompersi ed è diventato una tautologia — di punteggio ce n'è uno solo.

**La panchina, un minuto alla volta.** Il ciclo a minuti di `MatchEngine` era l'unico che sapesse il
punteggio, quindi era l'unico che potesse far scattare una sostituzione o una regola condizionale.
Adesso il punteggio lo sa il campo, quindi il calendario degli input è diventato una cosa che
entrambi i percorsi sanno percorrere: `MatchInputFeed`. Non consuma casualità — ed è quello che
permette al percorso veloce di restare identico al bit al motore di prima.

**Casa, forma e stanchezza sul campo.** Gli attributi cachati sono quelli SCALATI: condizione e
vantaggio casa sono fissi per la partita, la stanchezza si ripiega dentro a ogni cambio di minuto,
e nessun punto di chiamata ha dovuto imparare che esistono. La stanchezza è la curva del modello
risultato letta **un giocatore alla volta invece che una squadra alla volta**, che è tutto il
guadagno dell'avere la causalità sul campo.

### La misura (200 partite, tattiche neutre, seed 20260803)

**20/20 letture in banda per la prima volta da quando esiste lo scenario, e 23/23 check PASS.** La
lettura che la fase 5 lasciava fuori — **i tiri nello specchio, 15,0 contro una banda 6-11** — è
dentro, ed è dentro perché adesso è una cosa vera: prima ogni occasione della timeline che non fosse
un errore diventava una parata per costruzione.

| Lettura | Fase 5 | **Fase 6** | Calcio vero | |
|---|---|---|---|---|
| gol a partita | 2,52 | **2,66** | 2,4-3,0 | ✅ |
| tiri a partita | 25,2 | **22,0** | 20-30 | ✅ |
| **tiri nello specchio** | 15,0 | **7,1** | 6-11 | ✅ **chiusa** |
| passaggi a partita | 877 | **896** | 850-1150 | ✅ |
| precisione | 78,2% | **76,7%** | 76-88% | ✅ |
| rimesse | 33,4 | **35,4** | 30-50 | ✅ |
| corner | 10,7 | **11,4** | 8-13 | ✅ |
| rinvii dal fondo | 17,5 | **21,0** | 8-24 (§12) | ✅ |
| fuorigioco | 4,1 | **2,9** | 1,5-5 | ✅ |
| falli | 20,9 | **22,7** | 18-28 | ✅ |
| gialli | 2,85 | **3,25** | 2,0-5,5 | ✅ |
| km a giocatore | 11,07 | **11,80** | 9-12 | ✅ |
| **totale in banda** | 19/20 | **20/20** | | |

E il tiro, guardato da vicino: **22,0 tiri a partita, il 32% nello specchio, il portiere ne para il
70%, il 9,7% dei tiri finisce in gol.** Il resto della partita: 0,15 rigori, 0,27 rossi, possesso
casa 51,7%, palla per terzo 33,3 / 29,0 / 37,7, blocco difensivo 39,2 × 31,9 m con linea 5,9 e buco
10,6, blocco offensivo 44,0 × 37,7. **603,1 ms a partita** nel container (erano 542,8 alla fase 5:
il tiro costa l'11%).

**I due margini più stretti, detti perché lo sono**: la precisione dei passaggi è 76,7% contro un
pavimento di 76,0, e i rinvii dal fondo letti sui trenta semi di `RefereeTests` sono 23,1 contro un
tetto di 24 (l'harness, su duecento partite, ne legge 21,0). Sono misure deterministiche — quei
numeri escono identici a ogni run — ma è il posto in cui una fase successiva romperà per prima.

### Il modello risultato NON si è mosso, ed è dimostrato

Ogni manopola ritarata da questa fase vive **solo** nello strato di movimento — `GoalValueDm`,
`ShotSpreadDm`, `ShotConversionPercent`, `KeeperStop*`, `KeeperDive*`, `PitchHomeAdvantagePermille`,
`PitchDriveShiftPermille`, `SecondPressDepthDm`, `ParryRoundThePostDm`, `TempoHoldMs*`,
`OffsideJudgementDm`, `FoulPermilleOfChallenges*` — e il percorso veloce non ne legge nessuna.
Verificato nel container, cifra per cifra contro i numeri della fase 5:

    [balance-tactics]     45,0% miglior set / 42,5% peggiore · F433 37,4% / 49,9% · F352 51,4%
    [balance-tactics-season] +6,5 pts/stagione                                    4/4 PASS
    [balance-difficulty]  gap 6,9 / 6,6 / 9,5 / 7,6, tutti monotoni
    [balance-world]       risolutore 2,54 gol contro i 2,42 del motore, casa 44,9% vs 44,9%   9/9 PASS

### Quattro difetti veri che la fase ha trovato, e che solo l'inversione poteva rendere visibili

1. **Una palla già uscita poteva ancora essere toccata.** `ResolveOutOfPlay` gira DOPO
   `ResolveControl`, quindi una palla che aveva superato la linea durante il tick veniva ancora
   offerta a chiunque fosse vicino alla sua traiettoria — e un difensore che la "murava" lì
   rimetteva in gioco una palla già fuori, di cui l'arbitro poi leggeva un punto di attraversamento
   inventato. Finché il punteggio era della timeline questo poteva solo produrre una rimessa
   strana; con la causalità invertita produceva **un gol ogni tre partite**. La legge è più semplice
   del codice: nel momento in cui è fuori, non la tocca più nessuno.
2. **Il tiro passava attraverso il portiere.** La palla viaggia a 32 m/s, cioè 3,25 m per tick, e il
   raggio di presa è di 2,4: testare "la palla è a portata ADESSO" una volta per tick lasciava che
   un tiro attraversasse il portiere piazzato sulla sua traiettoria. Non è una parata sbagliata, è
   una parata mai offerta. `U.DistanceSqToSegment` misura contro il **segmento spazzato** dal tick,
   e lo stesso vale per il muro. Da sola questa correzione ha portato i gol da 21,4 a 11,7.
3. **Il portiere si murava da solo.** La respinta "dietro" spingeva la palla sei metri indietro e
   sei di lato: da una posizione centrale, e partendo da quattro metri e mezzo davanti alla linea,
   quel punto è **dentro la sua porta**, e il punto di attraversamento cade a metà della curva. Il
   bersaglio deve ESSERE il punto di attraversamento: sulla linea, fuori dal palo.
4. **Il portiere tirava.** Ogni opzione risponde con quanto vale, e una che non è aperta risponde
   con qualcosa che nessuna opzione vera può battere — ma il confronto deve allora ESCLUDERLA, o un
   uomo senza niente di aperto fa la prima cosa della lista. Un portiere che aveva appena
   recuperato palla nella propria area finiva in `TakeShot`, e il tabellino contava il suo rinvio
   come un tiro: il 5,5% dei "tiri" della prima misura erano quello.
5. **La deviazione volava fuori dal fondo avversario** — e questo l'ha trovato il run dell'utente,
   non il container. `RefereeTests` chiedeva 8-24 rinvii dal fondo e ne leggeva **28,4**. Misurato
   da dove venivano: **16,1 su 28,4 nascevano da una deviazione, non da un tiro.** La fase 5 aveva
   corretto esattamente questo difetto su `Clear` — *"un rinvio da quaranta metri volava per tutta
   la lunghezza del campo e usciva"* — e aveva lasciato il metodo gemello `Deflect` a colpire un
   bersaglio di venti metri a una frazione fissa della forza massima. Era un numero troppo grande
   già alla fase 5, dove stava in banda solo perché i tiri non ne producevano nemmeno uno: sono gli
   **undici tiri fuori che il calcio vero ha** ad averlo spinto oltre il tetto. Corretto come il
   gemello (colpita per ARRIVARE) più un bersaglio della spazzata più lontano dalla porta
   avversaria (`ClearanceGoalGapDm` 200 → 220): rinvii **28,4 → 21,0**, e quelli da deviazione
   **16,1 → 0,1**. Adesso quasi tutti nascono da un tiro fuori, che è il modo in cui nascono nel
   calcio.

### Cosa questa fase lascia aperto, detto onestamente

- **Tutti i tiri partono dall'area, e più vicino di così.** L'occhio, sul dump della partita
  verificata, dà il numero esatto: **sedici tiri, distanze 3 · 3 · 4 · 4 · 4 · 4 · 4 · 5 · 5 · 7 ·
  8 · 10 · 10 · 11 · 11 · 11 metri. Nessuno oltre gli undici metri in tutta la partita**, dove il
  calcio vero ha una distanza mediana intorno ai sedici. In banda per NUMERO, non per
  distribuzione. La causa è misurata: al limite dell'area la qualità dell'occasione è già tagliata
  dal traffico (`ShotQualityPressurePercent`) e il passaggio di sicurezza vale sempre un po' di
  più, quindi la soglia della decisione cade dentro il dischetto. È la cosa che un giocatore nota
  guardando: **nessuno le prova mai da fuori.** È la fase 8 il posto giusto per muoverlo — "tira
  appena puoi" è precisamente un'istruzione — e non una manopola in più adesso.
- **La palla del gol si ferma in mezzo alla porta.** `ScoreGoal` la posa a `(goalX, CenterY)` per la
  celebrazione, quindi i tre gol del dump entrano tutti esattamente al centro. È cosmetico e non
  tocca un numero, ma l'occhio lo vede: dovrebbe fermarsi nel punto in cui ha passato la linea.
  Costa una riga e un golden master, quindi non si fa su una build già verde.
- **0,88 gol a partita non nascono da un tiro**: deviazioni sul muro che entrano, un cross che
  finisce dentro, un'autorete. Il calcio vero ne ha molti meno. Sono tutti gol legittimi per il
  regolamento e nessuno di loro è un errore del motore, ma la coda è più grassa del vero.
- **Meno pareggi del modello a minuti.** La stessa squadra contro sé stessa: il campo dà casa 46% /
  pari 20% / trasferta 32%, il modello veloce 43% / 28,5% / 28,5%. Il vantaggio casa c'è ed è della
  misura giusta; i pareggi sul campo sono più rari.
- **Quanto morde la stanchezza SUL CAMPO.** `Rotation_OutperformsFixedXI` con la picture accesa dava
  73 punti alla formazione fissa e 65 a quella ruotata: sul campo il divario di qualità fra i
  titolari e i freschi batte il malus di condizione, dove nel modello a minuti non lo batteva. Il
  test è tornato sul percorso veloce — è lì che una stagione di rotazione si decide — ma la domanda
  resta aperta e appartiene alla fase 7, che è quella che dà al campo i dati sulle prestazioni.
- **Il costo.** 603,1 ms a partita restano il prezzo di una partita vista. Il mondo non lo paga.

### Cosa deve girare sulla macchina dell'utente

    .\tools\build-simcore.ps1
    dotnet test
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchStrict -PitchDump .\replay.html
    .\tools\balance.ps1        # gli altri scenari NON devono muoversi di un numero

Attesi: **375 test verdi** in `Sim.Core.Tests` (i 364 della fase 5 più gli 11 di `CausalityTests`),
**615 in totale** con `Api.Tests`; `[DeterminismCheck]` e `[server-determinism]` che stampano
**`0x690376649823E3A4`** (engine v9; era `0x222F723B4993ED25` in v8); lo scenario `pitch` che esce
con **codice 0** e **20/20 in banda**; `balance.ps1` 28/28 con ogni cifra invariata. Niente `.meta` da
generare, questa volta: i file nuovi e quello cancellato stanno tutti in `shared/`, che Unity vede
come DLL e non come sorgenti — resta solo da cancellare a mano `_to_delete/MatchDirector.cs.phase6`,
perché la shell della sandbox non ha il permesso di farlo. **I replay salvati in v8 non sono più
disegnabili**: è previsto, e il client li rifiuta da solo perché confronta con
`MatchEngine.Version`.

E poi l'occhio, che è la metà dell'accettazione che nessun test può dare: aprire `replay.html` e
guardare **da dove partono i tiri, chi para, e chi si butta davanti alla palla**.

---

## 14. Fase 7 — i dati sulle prestazioni

*Scritta l'11 settembre 2026, subito dopo la chiusura della fase 6, e **consegnata senza essere stata
compilata da nessuna parte**: il container di questa sessione ha perso l'accesso all'archivio Ubuntu
(403 dal proxy su `archive.ubuntu.com`, e anche npm e PyPI sono chiusi), quindi il `dotnet-sdk-8.0`
che le sei fasi precedenti hanno usato per compilare `Sim.Core` offline non era installabile. È
l'unica fase del piano consegnata così.*

### Il primo run dell'utente (11 settembre 2026): compila, e un rosso solo — il voto

**Ha compilato al primo colpo** (`build-simcore.ps1` verde, zero errori, zero warning nuovi) e
**`dotnet test` ha dato 636 su 637**. L'unico rosso è `TheMarks_OfARealMatch_ReadLikeFootballsMarks`:
**voto medio 8,51 invece di ~6,0**, ed è un difetto di taratura mio, raccontato qui sotto.

**Tutto il resto ha detto sì, e ha detto la cosa che contava:**

- **`[DeterminismCheck]` e `[server-determinism]` stampano entrambi `0xB0052E0B3942206A`**, identico
  alla fase 6 cifra per cifra. **Leggere la partita non la tocca**: è l'intera argomentazione di
  sicurezza di questa fase, verificata sulla sua macchina.
- **Lo scenario `pitch`: 20/20 in banda e 25/25 check PASS, exit code 0** — i 23 di prima più i due
  nuovi di contratto, e **passano entrambi**: `0 sides did not add up to eleven men for ninety
  minutes` e `0 goals are in the report and on nobody's line of it`. L'aritmetica dei minuti regge,
  e con lei tutta l'attribuzione.
- **`balance.ps1` 28/28 con ogni singola cifra invariata** (tattiche 45,0%/42,5%, F433 37,4%/49,9%,
  stagione +6,5, difficoltà 6,9/6,6/9,5/7,6/10,1, 67,6 trasferimenti, ingaggi 69,8%, `Avg goals
  2,44 | draws 24,8% | home wins 48,4%`, `Strong wins 82%`). Il modello risultato non è stato
  sfiorato.
- **Il costo: 317,7 ms a partita contro i 306,2 della fase 6** — **+11,5 ms, il 3,8%**, per tre
  passate sul filmato. Sotto la stima che avevo dato (5-10%).
- **I chilometri tornano**: 11,76 km a giocatore contro gli 11,80 che l'analizzatore misura in
  virgola mobile sulla stessa partita. La radice intera in decimi di decimetro perde 40 metri su
  dodici chilometri, lo 0,3%: era la ragione per cui l'accumulo è in decimi, e ha funzionato.
- **I passaggi tornano al totale di squadra**: 40,7 a giocatore al **76,7%**, contro il 76,7%
  esatto che l'analizzatore conta lato squadra. L'attribuzione per uomo somma al totale.
- **E gli assist sono football**: 0,07 a giocatore = **1,5 assist a partita su 2,66 gol**, cioè il
  58% dei gol ha un assist. Nel calcio vero sono fra il 60% e il 70%.

### Il secondo run (stesso giorno): i numeri tornano, e l'occhio trova la terza correzione

**`Sim.Core.Tests` 398 su 398, verde.** Il voto medio è sceso da 8,37 a **6,35**, e l'xG da 2,00 a
**1,33 a squadra contro 1,33 gol segnati** — cioè adesso l'xG *predice* quello che il motore fa.
Il `pitch` resta 20/20 in banda e 25/25 check, exit 0, a 315,3 ms. E il blocco `the men` ha
finalmente stampato la riga che serviva per capire tutto: **`off the ball 33,3 actions ·
dispossessed 13,9 times`** — trentatré azioni difensive a testa, esattamente la diagnosi.

*(L'unico rosso del secondo giro è in `Api.Tests` e **non è di questa fase**: il test della scala
ranked asserisce che dopo il reset stagionale le rose restino entro 3 punti di forza e ne ha letti 4.
Quel mondo è generato **a caso a ogni run** — gli account sono GUID nuovi — e infatti il primo run
partiva da uno spread di 16 e il secondo da 22. È la stessa fragilità che il test gemello del draft
aveva già imparato e documentato in `LeagueEndpointTests` («≤2 era troppo stretto e flakeava in CI»,
portato a ≤6): il limite della scala è stato allineato, con la stessa spiegazione scritta accanto.
Il golden master è identico in tutti e due i run, `Sim.Core` è verde, e l'equalizzatore non è mai
stato toccato da questa fase.)*

**E POI L'OCCHIO, che è la metà dell'accettazione che nessun test può dare.** Aperto `replay.html`
sulla partita che l'harness ha dumpato (Inter Rigoria 2-1 SS Capocannona), la pagella dice questo:

    # 4 Jacopo Olivetti   90'  voto 7,8   0 gol   18 recuperi        (difensore)
    # 7 Jacopo Roversi    90'  voto 6,2   2 GOL   34 palloni persi   (attaccante)

**Chi aveva segnato due gol prendeva meno del suo centrale.** E non era un caso isolato: su
*entrambe* le squadre, *tutti* i difensori e i centrocampisti stavano sopra *tutti* gli attaccanti —
7,8 · 7,6 · 7,3 · 7,3 · 7,2 · 7,2 contro 6,2 · 6,1 · 5,9 · 3,3 in casa, e lo stesso fuori.

La causa è strutturale, ed è il rovescio esatto della correzione precedente: **un attaccante recupera
meno palloni e ne perde di più di un centrale, perché è quello che il suo mestiere È.** Misurarlo
sulla media di tutta la partita lo puniva due volte per aver giocato davanti. Quindi ogni uomo è
confrontato **con il suo reparto**: i dieci di movimento vengono ordinati per quanto lontano dalla
propria porta hanno davvero passato la partita — la posizione media che questa fase già calcola — e
divisi in quattro difensori, tre di mezzo e tre davanti. Niente formazione dichiarata, niente ruoli:
solo dove sono stati. Un test lo inchioda su otto partite (`[perf-lines]`): la media dei quattro più
arretrati e quella dei tre più avanzati devono stare entro un punto l'una dall'altra.

**Una cosa che l'occhio ha visto e che NON è un difetto di questa fase, ma va scritta:** un portiere
risulta con **35 "contrasti"**. Non è un errore di attribuzione — è il motore che registra
`BallActionKind.Tackle` ogni volta che qualcuno *recupera* il pallone, e un portiere raccoglie tutto
quello che entra in area. Nel referto della fase 7 quella colonna è quindi chiamata **recuperi**, che
è quello che è; rinominare l'azione nel flusso toccherebbe il formato del replay e le didascalie del
dump, ed è materiale della fase 8.

### I due difetti che la misura ha trovato, e come sono corretti

**1. IL VOTO, ed è il rosso.** Media 8,37 sulle 200 partite del `pitch`, migliore 10,0, peggiore
3,3. La causa è una sola e si legge nella riga sopra del referto: **questo motore produce 306
contrasti, 233 spazzate e 192 intercetti a partita**, cioè **circa trentatré azioni difensive per
uomo**, dove il calcio vero ne conta due o tre. Avevo dato un decimo per azione — nel calcio vero
vale — e trentatré decimi sono **tre punti e mezzo regalati a chiunque scenda in campo**.

La correzione non è abbassare il peso (domani la fase 8 cambia di nuovo la frequenza e siamo da
capo): **il lavoro senza palla si paga sullo SCARTO dalla media di quella partita**, proporzionato
ai minuti giocati. Chi fa la sua parte prende zero, chi ne fa metà in più prende qualcosa, chi fa il
passeggero perde. È auto-tarante: legge lo stesso che il motore conti tre recuperi a testa o trenta.
Stesso trattamento per i duelli, che valgono come **bilancio** (vinti meno persi) e non come
conteggio — vincerne venti e perderne venti è un pomeriggio faticoso, non un bel pomeriggio — ed
entrambi i termini hanno un tetto (±1,5 punti), perché nessuna quantità di corsa vale più di un gol.
Media attesa dopo la correzione: **6,0-6,2**, con i portieri a ~6,2 (2,5 parate a +0,3 contro 1,33
gol subiti a −0,4).

**2. L'xG diceva 2,00 a squadra dove se ne segnavano 1,33.** Un xG che vale una volta e mezza i gol,
su ogni singola partita, è un numero che mente sul referto. La causa non è il modello: è che
**ogni tiro di questo motore parte da dentro l'area** (mediana ~5 m — è la cosa che la fase 6 ha
lasciato aperta), e la geometria di quei tiri nel calcio vero vale ~15% di conversione mentre questo
motore converte al **9,7%**. Quindi il picco è stato **tarato sul motore invece che sul calcio**:
`XgPeakPermille` 380 → **250**, che riporta l'xG a ~1,33 a squadra. È scritto nella manopola, con la
misura accanto: **quando la fase 8 darà una distanza ai tiri, va rimisurato.**

*(E c'è una terza cosa, che non è un difetto ma la prima diagnosi che questa fase produce: il campo
crea la stessa quantità di occasioni del calcio vero e le converte a due terzi del suo ritmo. È
materiale per la fase 8.)*

### Cosa fa la fase

La fase 6 ha reso il campo la verità. Questa lo rende **leggibile**: la partita che è stata giocata
adesso si racconta, uomo per uomo e squadra per squadra.

**La riga di ogni uomo** (`PlayerMatchStats`): minuti veri, chilometri percorsi, posizione media,
passaggi tentati e riusciti, palle lunghe, cross, **passaggi chiave**, conduzioni, tiri e tiri nello
specchio, **xG**, gol, **assist**, contrasti, intercetti, spazzate, **tiri murati**, duelli vinti e
persi, parate e gol subiti per il portiere, falli fatti e subiti, fuorigioco, cartellini, e un
**voto in decimi** (60 = 6,0).

**Il rapporto di ogni squadra** (`TeamMatchStats`): possesso, territorio nei tre terzi *dal proprio
punto di vista*, tiri e xG, precisione dei passaggi, **larghezza e profondità del blocco difendendo
e attaccando**, **quanto alto stava** (la X media dei dieci mentre l'altra squadra aveva la palla —
il numero che la Mentalità dovrà muovere in fase 8), falli, cartellini, corner, e la **mappa dei
passaggi**: quante volte una maglia ha giocato a un'altra, e quante volte è arrivata.

### La cosa che rende tutto questo sicuro

**Niente di tutto ciò è prodotto dal simulatore mentre gioca. È LETTO dal filmato quando la partita
è finita.** `MatchStatsBuilder` prende un `MatchReport` già chiuso e il suo `PositionStream`, non
tocca nessuno dei due, non estrae un solo numero casuale — esattamente il contratto che
`MatchAnalyzer` rispetta dalla fase 0, applicato un giocatore alla volta invece che una squadra alla
volta.

Da cui le tre conseguenze che contano:

1. **`MatchReportHasher` non è stato toccato**, e i dati sulle prestazioni sono deliberatamente
   FUORI dall'hash: un golden master è l'hash di quello che è *successo*, e questo è una lettura di
   quello che è successo. **`0xB0052E0B3942206A` resta il numero della fase 6.** Un test lo inchioda
   giocando due volte lo stesso seme, con le statistiche accese e spente, e confrontando gli hash.
2. **`balance.ps1` non si muove di una cifra**, perché il percorso veloce del mondo non ha filmato,
   e senza filmato non ci sono statistiche: le migliaia di partite di sfondo di una stagione non
   pagano niente e non cambiano niente.
3. **Niente `MatchEngine.Version`, niente bump del save, niente migrazione.** I replay della fase 6
   restano disegnabili: il campo nuovo è additivo e un replay vecchio semplicemente non lo porta.

### Il pezzo che mancava al filmato: le sostituzioni

Il flusso porta **un id giocatore per maglia**, e una sostituzione lo sovrascrive — giusto per chi
disegna (nomina l'uomo in campo), inutile per una statistica: a fine partita l'array dice che il
subentrato ha giocato novanta minuti e l'uomo che è uscito non è mai esistito.

Quindi `PositionStream` guadagna `Changes`: **chi è entrato, per chi, e a quale frame**. È l'unica
riga di questa fase dentro `MatchSimulator`, sta nel punto in cui l'id veniva sovrascritto, e non
consuma niente. Un cambio cade sempre su un confine di minuto (la panchina viene interrogata una
volta al minuto), quindi i minuti che ne escono sono interi e **gli undici di una squadra fanno
sempre 990**, a meno che qualcuno non sia stato espulso — ed è esattamente il controllo che l'harness
e i test fanno, perché è l'aritmetica che dimostra che tutta l'attribuzione regge. Un espulso smette
di giocare al minuto del rosso e la sua maglia non viene più riempita.

### Le scelte di merito, dette per nome

**L'xG è una STIMA GEOMETRICA, e si chiama così.** Non è il numero contro cui il modello di tiro ha
tirato il dado: il campo decide un tiro dalle qualità di chi tira e dai corpi davanti, e questo
legge *da dove* è partito. Distanza con caduta `h²/(h²+d²)` (h = 9 m) e una penalità d'angolo,
tutto in aritmetica intera: ~0,26 da sei metri, ~0,15 da undici, ~0,09 dal limite, ~0,03 da trenta.
Un rigore vale 0,76 per decreto, come nel calcio vero.

**Un passaggio chiave è il passaggio il cui destinatario tira** (entro quindici secondi e prima che
la squadra perda palla), **un assist è quello che finisce in rete.** Un gol deviato o una mischia non
danno assist a nessuno: qualsiasi cosa spezzi l'azione — un contrasto, un tiro murato, una spazzata,
una rimessa, il fischio — azzera la memoria.

**Il voto è un'opinione, e l'unico modo onesto di pubblicarne una è dire ad alta voce di cosa è
fatta.** È una somma di cose contate, ognuna col suo peso in `PerformanceBalance`, dentro
`BalanceConfig` come ogni altra manopola del progetto: si parte da 6,0 ("c'era, e non è successo
niente"), un gol vale +1,2, un assist +0,7, un rosso −1,5, la precisione dei passaggi conta solo
sopra i dieci palloni giocati, **il portiere è giudicato sulle parate e sui gol subiti** e non sul
suo passaggio, e **chi entra al 75' non può prendere nove**: lo scarto dal 6,0 è scalato sui minuti
giocati. Tutto intero, perché quel numero può finire dentro il modello di sviluppo e deve
riprodursi identico su .NET, Mono e IL2CPP.

### Il terzo punto della fase — condizione e sviluppo — è SCRITTO MA SPENTO

Il piano chiede che i dati alimentino anche condizione e sviluppo, «che oggi non hanno dati di
partita». Il cablaggio c'è ed è completo:

- `ConditionProgressor.Participation` accetta i **minuti veri** per giocatore. Oggi accredita novanta
  minuti a ogni titolare e zero a chiunque altro, per cui un subentrato non si stanca mai;
- `OnlineSeasonTick.EvolveWeek` accetta i **voti** per giocatore, che finiscono in
  `DevelopmentContext.PerformanceRating` — il campo che esiste dal 4.4 e che è sempre stato una
  costante neutra perché non c'era una prestazione da metterci;
- `SeasonProgressor.EvolveCondition` ha `useMatchMinutes`, e `MatchPerformanceFeed` costruisce i due
  dizionari da una giornata di partite.

**E sono tutti spenti di default: si accendono passando i dati.** Non è timidezza, è la regola del
progetto: minuti veri e voti cambiano *come le rose si stancano e come i giocatori crescono*, cioè
sono una modifica di bilanciamento, e una modifica di bilanciamento passa davanti all'harness da
1.000 partite prima di diventare il default — non entra di soppiatto dentro una statistica. Finché
non si accende, **ogni cifra di `balance.ps1` legge quello che leggeva prima**.

La decisione su quando accenderla è sua, e la misura che serve per prenderla è un `balance.ps1`
prima e dopo. Sospetto che il primo effetto visibile sia sulla rotazione — la cosa che la fase 6 ha
lasciato aperta («quanto morde la condizione sul campo»), perché con i minuti veri un subentrato
finalmente paga quello che gioca.

### Cosa è stato toccato

**Nuovi** (`shared/Sim.Core/`): `Match/Analysis/PlayerMatchStats.cs` (i tre DTO più `PassLink`),
`Match/Analysis/MatchStatsBuilder.cs` (la lettura), `Match/Analysis/MatchRatingModel.cs` (il voto),
`Career/MatchPerformanceFeed.cs` (il ponte verso condizione e sviluppo).
**Nuovo test**: `Sim.Core.Tests/Match/PerformanceDataTests.cs` — **22 test**, metà su flussi
costruiti a mano con la risposta calcolata a penna (un passaggio che arriva contro uno che no, il gol
accreditato all'uomo che era in quella maglia e non al subentrato, quanto vale un tiro da sei metri
contro uno da venticinque) e metà sugli invarianti del motore vero (undici uomini per novanta
minuti, ogni gol appartiene a qualcuno, leggere la partita non la cambia).
**Modificati**: `Match/MatchReport.cs` (`Stats`), `Match/PositionStream.cs` (`SlotChange` +
`Changes`), `Match/MatchEngine.cs` (costruisce le statistiche dopo la partita, con una manopola
`buildStats`), `Match/Movement/MatchSimulator.cs` (**una** aggiunta: registra il cambio prima di
sovrascrivere l'id), `Config/BalanceConfig.cs` (`PerformanceBalance`), `Condition/ConditionProgressor.cs`,
`Career/SeasonProgressor.cs`, `Career/OnlineSeasonTick.cs`.
**Harness**: `tools/BalanceHarness/PitchScenario.cs` stampa il blocco `the men` (voto medio, migliore
e peggiore, chilometri, passaggi e precisione, passaggi chiave, assist, parate, xG a squadra) e
aggiunge **due check di contratto**: gli undici per novanta minuti, e ogni gol su una riga.
`tools/BalanceHarness/PitchDump.cs` (dopo il primo run) porta la **pagella dentro `replay.html`**:
le due tabelle giocatore per giocatore, la riga tattica delle due squadre con le corsie di passaggio
più battute, e la **mappa delle posizioni medie** su un campo disegnato. È il modo in cui l'occhio
controlla questa fase, ed è codice di harness: non entra in `Sim.Core` e non muove un numero.

### Il costo, detto prima che si veda

Leggere una partita è **tre passate sul filmato** (i portieri, i frame, le azioni) su 10.801
fotogrammi per ventidue uomini, con una radice quadrata intera per uomo per frame. Mi aspetto
**qualche decina di millisecondi**, contro i ~306 ms che la partita costa a giocarla sulla sua
macchina: nell'ordine del 5-10% sullo scenario `pitch` e su quella parte della suite che guarda le
partite. Se il `ms/match` stampato dal `pitch` sale più di così, la manopola c'è: `buildStats: false`
sul motore, o `Performance` fuori dal giro.

Il referto cresce di **una quindicina di KB** per partita giocata (ventidue righe di interi), contro
i 794 KB del filmato compresso: il 2%. Se preferisce non persisterle, `report.Stats = null` prima di
salvare, esattamente come già si fa con le posizioni.

### ✅ Cosa deve girare sulla sua macchina (terzo giro: solo la conferma del reparto)

    .\tools\build-simcore.ps1
    dotnet test
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchStrict -PitchDump .\replay.html

Attesi: **399 verdi** in `Sim.Core.Tests` (i 375 della fase 6 più i 24 di `PerformanceDataTests`),
**639 in totale** — e `Api.Tests` verde, col limite della scala allineato. Il `pitch` deve restare
20/20 e 25/25 con il voto medio ancora intorno al 6,3 e l'xG a 1,33.

La riga nuova da leggere è **`[perf-lines]`**: la media dei quattro più arretrati e quella dei tre
più avanzati, che devono stare vicine. E poi `replay.html`: **il marcatore di una partita deve stare
in cima alla sua pagella**, non sotto il proprio centrale.

### Cosa era stato chiesto al secondo giro (fatto)

    .\tools\build-simcore.ps1
    dotnet test
    .\tools\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchStrict -PitchDump .\replay.html
    .\tools\balance.ps1        # gli altri scenari NON devono muoversi di un numero

Attesi: **398 test verdi** in `Sim.Core.Tests` (i 375 della fase 6 più i 23 di
`PerformanceDataTests`), **638 in totale** con `Api.Tests`; `[DeterminismCheck]` e
`[server-determinism]` che stampano ancora **`0xB0052E0B3942206A`** — se questo numero si muove, la
fase ha toccato la partita e va fermata; lo scenario `pitch` che esce con **codice 0**, **20/20 in
banda** e **25/25 check**; `balance.ps1` **28/28 con ogni cifra invariata**.

Il primo giro ha già dimostrato il determinismo, le bande, i due check nuovi e il costo: quello che
questo secondo giro deve dire è **solo il blocco `the men`**. Le due cifre da leggere:

- **voto medio 6,0-6,3**, migliore sopra 7 e peggiore sotto 5 (era 8,37, ed è il rosso corretto);
- **xG intorno a 1,3 a squadra** contro i 1,33 gol che il motore segna (era 2,00).

E poi **l'occhio, che adesso ha qualcosa da guardare**: `replay.html` porta in fondo **la pagella** —
una riga per uomo con minuti, voto, chilometri, passaggi e precisione, passaggi chiave, tiri, xG,
gol, assist, contrasti, intercetti, spazzate, duelli e falli; la riga tattica delle due squadre
(possesso, territorio, blocco difendendo e attaccando, quanto alto, corsie di passaggio più battute);
e **la mappa delle posizioni medie**, ventidue maglie disegnate dove ognuno ha davvero passato la
partita. Quello è il controllo che nessun test può fare: **le posizioni medie devono somigliare a una
formazione** (portiere sulla linea, difensori bassi e larghi, punte alte), e il voto più alto deve
essere di qualcuno che ha fatto qualcosa.

### Aperto, per scelta

- **Condizione e sviluppo sono spenti** (sopra). È la decisione che questa fase lascia a lei.
- **Il client non mostra ancora niente.** I dati esistono nel referto; la schermata che li fa vedere
  — la pagella e il rapporto tattico a fine partita — è lavoro di UI e cade dentro la fase 14 del
  ROADMAP, non dentro il motore.
- **Il server non li serve ancora a nessuno.** `ReplayStore` li persisterà dentro il replay perché
  fanno parte del referto; esporli come endpoint proprio (pagella di una partita di lega) è un passo
  server da fare quando il client li vuole.
- **L'xG è geometrico** e non viene dal modello di tiro. Se un giorno il tiro pubblicherà la sua
  probabilità, quella sarà la cifra migliore.
- **I duelli sono i contrasti**, non ogni contatto: l'uomo che vince un pallone conta un duello
  vinto, quello a cui è stato tolto uno perso. Un duello aereo non esiste perché nel modello non
  esiste la palla alta.

---

## 15. Riferimenti

- RoboCup Soccer Simulator — https://rcsoccersim.readthedocs.io/en/latest/overview.html
- RoboCup 2D Soccer Simulation League — https://en.wikipedia.org/wiki/RoboCup_2D_Soccer_Simulation_League
- Football Manager 26, positional play e autenticità della partita —
  https://www.footballmanager.com/features/truer-football-motion-match-authenticity-positional-play
- open-football (Rust) — https://github.com/ZOXEXIVO/open-football
- openengine — https://github.com/atas76/openengine
- Openfoot Manager — https://openfootmanager.com/
- Buckland, *Programming Game AI by Example*, cap. 4 (base dell'implementazione attuale)
