# -*- coding: utf-8 -*-
import io, os, sys

ROOT = os.path.join(os.environ['HOME'], 'mnt', 'FootballTeamSimulator')
CRLF = {}

def load(rel):
    p = os.path.join(ROOT, rel)
    with io.open(p, 'r', encoding='utf-8', newline='') as f:
        raw = f.read()
    CRLF[p] = '\r\n' in raw
    return p, raw.replace('\r\n', '\n')

def save(p, s):
    if CRLF.get(p):
        s = s.replace('\n', '\r\n')
    with io.open(p, 'w', encoding='utf-8', newline='') as f:
        f.write(s)

def sub(text, old, new, rel, label, count=1):
    if old not in text:
        print('MISS  %s :: %s' % (rel, label)); sys.exit(1)
    n = text.count(old)
    if n != count:
        print('COUNT %s :: %s (%d, wanted %d)' % (rel, label, n, count)); sys.exit(1)
    print('ok    %s :: %s' % (rel, label))
    return text.replace(old, new)

# ---------------------------------------------------------------- CLAUDE.md
p, t = load('CLAUDE.md')

t = sub(t,
"golden master **0x222F723B4993ED25** (engine v8, engine rework phase 5 + the wall fix 5c, measured in the container on .NET 8 and **NOT yet re-run on the user's machine** — his verified phase-5 value was 0x436E4440B6350A7B, identical digit for digit to the container's, and the wall fix invalidated it;",
"golden master **0x222F723B4993ED25** (engine v8, engine rework phase 5 + the wall fix 5c, verified on the user's machine 2026-09-06 in 577.6 s and identical digit for digit to the container's .NET 8 value; the value BEFORE the wall fix was 0x436E4440B6350A7B, also verified on his machine, and the wall fix invalidated it;",
'CLAUDE.md', 'test-count line')

t = sub(t,
"## Current position — \U0001F7E1 ENGINE REWORK PHASE 5 + THE WALL FIX (2026-09-05): there is a REFEREE\n\n**⚠ 5c — THE WALL, FIXED AFTER HIS RUN: THE GOLDEN MASTER MOVED AGAIN AND HE MUST RE-RUN.**\nPhase 5 was verified on his machine (the block below), then the eye found the one thing the numbers\ncould not:",
"## Current position — \U0001F3C1 ENGINE REWORK PHASE 5 CLOSED, WALL FIX INCLUDED (2026-09-06): there is a REFEREE\n\n**5c — THE WALL, FIXED AFTER HIS FIRST RUN AND RE-VERIFIED ON HIS MACHINE 2026-09-06.**\nPhase 5 was verified on his machine, then the eye found the one thing the numbers\ncould not:",
'CLAUDE.md', 'current position heading')

t = sub(t,
"""**Golden master → `0x222F723B4993ED25`**; his verified phase-5 run read `0x436E4440B6350A7B`, which
the wall fix invalidated — the stream changed, the result model did not (goals still 2.52).
**So `.\\tools\\build-simcore.ps1` and `dotnet test` have to be run again.**
Only two files moved: `MatchSimulator.cs` and `BalanceConfig.cs`.""",
"""**Golden master → `0x222F723B4993ED25`**; the phase-5 run before the fix read `0x436E4440B6350A7B`,
which the wall fix invalidated — the stream changed, the result model did not (goals still 2.52).
Only two files moved: `MatchSimulator.cs` and `BalanceConfig.cs`.

### Verified by the user, 2026-09-06 (the wall fix)

    .\\tools\\build-simcore.ps1                                          Sim.Core + Contracts OK
    dotnet test                                                         604/604 green, 0 failed, 577.6 s
                                                                        (Sim.Core.Tests 364 + Api.Tests 240)
    .\\tools\\balance.ps1 -Scenario pitch -PitchMatches 200 -PitchDump .\\replay.html
                                                                        19/20 in band · 3/3 checks PASS
                                                                        311.7 ms a match · "Balance checks PASSED"
    .\\tools\\balance.ps1                                                28/28 PASS, every figure unchanged

`[DeterminismCheck]` and `[server-determinism]` both printed **`0x222F723B4993ED25`** — cross-runtime
determinism (his .NET 10 against the container's .NET 8) survives the wall fix. The `pitch` scenario
reproduced **every single number** of the container's 200-match run: throw-ins 33.4, corners 10.7,
offsides 4.1, fouls 20.9, goals 2.52, shots 25.2 (15.0), passes 877.4 at 78.2%, defending 38.7 × 32.1,
attacking 45.2 × 39.6, 11.07 km. `[laws-*]` likewise to one decimal: 37.0 throw-ins / 11.0 corners /
17.5 goal kicks and 0 held-ball-on-a-line frames, 4.2 offsides, 21.9 fouls · 2.95 yellows · 0.15 reds
· 0.10 penalties, 8.5 fouls from a side of 90 tacklers against 11.2 from a side of 20, 10 sent off in
40 matches, 6 intervals. And `balance.ps1` came back **28/28 with every figure unchanged**, which is
the proof the wall fix did not touch the result model.

**Outputs that look like faults and are not:** `[HIGH ] shots on target 15.0 want 6.0-11.0` is the
one band left open on purpose (the result model's share — phase 6), and it does NOT fail the run
because `--pitch-strict` is off; the `NU1903 SQLitePCLRaw.lib.e_sqlite3` advisory on `Api.Tests` is
pre-existing and unrelated; and the `Application started / shutting down` blocks are `Api.Tests`
spinning the test host up and down.""",
'CLAUDE.md', 'verified-by-user block')
save(p, t)
print('WROTE CLAUDE.md')

# --------------------------------------------- docs/engine/MATCH_ENGINE_PLAN.md
p, t = load('docs/engine/MATCH_ENGINE_PLAN.md')

t = sub(t,
"""*⚠ **Dopo la sua verifica è arrivata la correzione della barriera (§12.5), che ha cambiato il
golden master in `0x222F723B4993ED25`**: i numeri di questo capitolo sono quelli del container DOPO
la correzione, e `build-simcore` + `dotnet test` vanno rifatti.*""",
"""*Dopo quella verifica è arrivata la correzione della barriera (§12.5), che ha cambiato il golden
master in **`0x222F723B4993ED25`**: i numeri di questo capitolo sono quelli DOPO la correzione, e
**anche quelli sono stati verificati dall'utente, il 6 settembre 2026** — 604 test verdi in 577,6 s,
i due hash identici, `pitch` 19/20 + 3/3 a 311,7 ms a partita con ogni numero uguale a quello del
container, e `balance.ps1` 28/28 con ogni cifra invariata.*""",
'PLAN', 'chapter 12 intro note')

t = sub(t,
"""**Verificato con i numeri, non con l'occhio**: ogni difensore della barriera sta a **91-95 dm dalla
palla** — 9,1-9,5 m, i 9,15 della regola — e ci resta. La misura del capitolo è quella dopo la
correzione: 364 test verdi, 19/20 in banda, 3/3 check PASS. **Golden master →
`0x222F723B4993ED25`** (era `0x436E4440B6350A7B` nella sua verifica): è cambiato lo stream, non il
modello risultato — i gol restano 2,52. File toccati: **`MatchSimulator.cs` e `BalanceConfig.cs`**.""",
"""**Verificato con i numeri, non con l'occhio**: ogni difensore della barriera sta a **91-95 dm dalla
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
20, 10 espulsi in 40 partite, 6 intervalli.""",
'PLAN', 'section 12.5 verification')

t = sub(t,
"""Attesi — **confermati dall'utente il 5 settembre 2026 sul corpo della fase, da riconfermare dopo la
correzione della barriera (§12.5), che ha cambiato il golden master**: **364 test verdi** in""",
"""Attesi — **confermati dall'utente il 5 settembre 2026 sul corpo della fase e di nuovo il 6 settembre
2026 dopo la correzione della barriera (§12.5), che ha cambiato il golden master**: **364 test verdi** in""",
'PLAN', 'how to verify note')

t = sub(t,
"""- [x] **Fase 5 — il regolamento** (2026-09-05, verificata dall'utente; la correzione della
      barriera §12.5 è arrivata dopo e va riverificata — golden master nuovo). Vedi §12.""",
"""- [x] **Fase 5 — il regolamento** (2026-09-05, verificata dall'utente; la correzione della
      barriera §12.5 è arrivata dopo ed è stata riverificata il 2026-09-06). Vedi §12.""",
'PLAN', 'checkbox phase 5')

t = sub(t,
"| 5 | il regolamento | ✅ fatta **e verificata dall'utente** — 🟡 correzione barriera (§12.5) da riverificare |",
"| 5 | il regolamento | ✅ fatta **e verificata dall'utente**, barriera (§12.5) compresa |",
'PLAN', 'status table row 5')
save(p, t)
print('WROTE MATCH_ENGINE_PLAN.md')

# ---------------------------------------------------------------- ROADMAP.md
p, t = load('ROADMAP.md')
t = sub(t,
"""  **5c — THE WALL (2026-09-05, after his run: THE GOLDEN MASTER MOVED, so `build-simcore` +
  `dotnet test` must be run again).**""",
"""  **5c — THE WALL (2026-09-05, **verified by the user 2026-09-06**: `dotnet test` **604/604 green in
  577.6 s**, `[DeterminismCheck]` and `[server-determinism]` both **`0x222F723B4993ED25`** identical
  digit for digit to the container's, the `pitch` scenario reproducing **every single number** of the
  container's 200-match run at **311.7 ms a match** and exiting 0, and `.\\tools\\balance.ps1`
  **28/28 PASS with every figure unchanged** — which is the proof the wall fix did not reach the
  result model).**""",
'ROADMAP', '5c heading')

t = sub(t,
"""  (his phase-5 run read `0x436E4440B6350A7B`, which this invalidated: the stream changed, the result
  model did not). Two files moved: `MatchSimulator.cs`, `BalanceConfig.cs`.""",
"""  (the phase-5 run before it read `0x436E4440B6350A7B`, which this invalidated: the stream changed,
  the result model did not). Two files moved: `MatchSimulator.cs`, `BalanceConfig.cs`.
  **His run, line by line:** `[laws-restarts]` 37.0 throw-ins · 11.0 corners · 17.5 goal kicks and 0
  frames in 30 matches with a held ball on a line · `[laws-offside]` 4.2 · `[laws-fouls]` 21.9 fouls ·
  2.95 yellows · 0.15 reds · 0.10 penalties · `[laws-tackling]` 8.5 fouls and 1.20 cards from a side
  of 90 tacklers against 11.2 and 1.60 from a side of 20 · `[laws-cards]` 10 sent off in 40 matches ·
  `[laws-halftime]` 6 intervals — every one the container's number to one decimal, on a different
  machine, a different OS and a different runtime. Expected non-faults in that output: `[HIGH ] shots
  on target 15.0` is the one band left open for phase 6 and does not fail the run (`--pitch-strict`
  is off), and the `NU1903 SQLitePCLRaw` advisory on `Api.Tests` is pre-existing and unrelated.
  🏁 **Phase 5 is CLOSED**; only `MatchRenderer`'s Play-mode look at the change of ends is left, and
  that is a client-side look, not engine work.""",
'ROADMAP', '5c verification detail')
save(p, t)
print('WROTE ROADMAP.md')
