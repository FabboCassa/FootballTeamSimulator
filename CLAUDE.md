# CLAUDE.md — session context for Football Team Simulator

Read ARCHITECTURE.md (design) and ROADMAP.md (plan + current status via checkboxes) before doing anything.

## Working agreement (do not violate)
- **NEVER run `git commit` / `git add` / any stateful git command** — the user commits from his Windows machine. Concurrent git access through the synced folder corrupts the index. Touch source files only.
- One roadmap task at a time. The user tests every task on his machine (`dotnet test`, Unity) before it is marked `[x]` in ROADMAP.md. Mark `[~]` while in progress.
- The sandbox has NO dotnet SDK and cannot reach Microsoft/dot.net domains — code cannot be compiled here. Write conservative, standard C# and let the user build. If a test might be statistically fragile, say so and ask for the output.
- Balance-sensitive changes go through the 1,000-match harness; the user pastes the printed distribution and we judge together before closing the task.
- Language: chat in Italian, all code/comments/docs in English.

## Environment facts
- User machine: Windows, .NET 10 SDK, Unity 6.3 LTS (6000.3.17f1), project folder `C:\Users\Fabbo\FootballTeamSimulator`.
- Unity project = `client/` subfolder (opened via Unity Hub); git repo = monorepo root, single GitHub repo for everything.
- After ANY change in `shared/`, the user must run `.\tools\build-simcore.ps1` so Unity gets fresh DLLs (Sim.Core.dll + Fts.Contracts.dll → `client/Assets/Plugins/SimCore/`, gitignored, .meta committed).
- Test command: `dotnet test` (all) or with harness output:
  `dotnet test shared/Sim.Core.Tests/Sim.Core.Tests.csproj --logger "console;verbosity=detailed"`
- Current test count: 72 green.

## Architecture quick reference
- `shared/Sim.Core` (netstandard2.1, no Unity deps, no I/O, deterministic): ALL game rules. Namespaces: Domain, Generation, Match, Config, Random (+ empty: Market, Development, Condition, Tactics, Career).
- Determinism rules: only `Pcg32`/`IRandomSource` for randomness; no `Math.Pow/Exp/Log/Sin` (use IntPow pattern); no DateTime.Now; integer math where possible. This enables replays = (seed + inputs) and the IL2CPP cross-check (task 1.6).
- Every tunable lives in `Config/BalanceConfig.cs` (sections: Generation, Match). No magic numbers in systems.
- `server/` = ASP.NET Core skeleton (Api/Application/Infrastructure) with /health; docker-compose (postgres 17, redis 8). Real backend work starts Phase 7.
- Unity client: MVP + VContainer + UniTask + UI Toolkit, asmdefs FTS.App/Services/Presenters/Views/MatchView. Only Boot scene exists so far.
- Localization: ALL user-facing strings go through ILocalizationService (tables in client/Assets/Resources/Localization/{en,it}.json, flat key->text). Views can't reference Services, so they receive a Func<string,string> translate delegate from presenters. Missing keys fall back to en, then render the key itself. NEVER hardcode UI strings in views/presenters. Dev console logs stay English.

## Status (2026-06-12)
- Phase 0 done (0.5 CI: workflow exists, verified on first GitHub push).
- Phase 1 done. 2.1 done (app shell: MessageBroker, ScreenNavigator with per-screen child scopes + Reveal() hook, placeholder screens, AppLifetimeScope/GameSessionService/AppEntryPoint; views code-built, UXML arrives 2.4+; Boot scene has UIDocument + PanelSettings + AppLifetimeScope).
- 2.2 done (career setup & save: Newtonsoft via manifest; CareerState; CareerFactory; LocalJsonSaveRepository gzip+atomic+FNV-hash log in persistentDataPath/career.sav; CareerSetup screen; MainMenu Continue with friendly errors; user verified save/load/corruption).
- 2.3 done (calendar & advance day: Sim.Core Fixture/Season/LeagueTable/FixtureGenerator/SeasonProgressor, SeasonBalance config, save v2 + v1→v2 migration, LocalClock, Hub Advance Day + console table; 59 tests green, user verified).
- **2.4 squad screen IN PROGRESS**: code written — Sim.Core: Match/LineupPlan (serializable, From/Materialize/TryMaterialize), SeasonProgressor.AdvanceDay now takes optional per-club LineupPlan dict and returns List<MatchOutcome> (fixture+report; invalid plans fall back to BestEleven silently); 5 new/updated tests (expect 64 green). Client: CareerState.UserLineup (additive, still save v2, null = auto), LocalClock passes user plan + logs user match result/scorers/XI-source to console, SquadView+SquadScreenPresenter (slot list + roster, tap-slot-then-player with swap semantics, Auto Pick/Save; player *value* column deferred to 5.1, formations beyond 4-3-3 to 3.2). NEEDS: build-simcore.ps1 + dotnet test + Play-mode test. MatchEngine.Version = 2 since 1.5 (position stream; score/events per seed unchanged from v1).
- 2.4 done (squad screen: LineupPlan in Sim.Core, SeasonProgressor plans+MatchOutcome, SquadView/presenter, user XI used in sim; 64 tests green, user verified).
- Localization infra done early (pulled from 6.2): LocalizationService + en/it JSON tables, all existing UI strings migrated, language toggle in MainMenu (cycles en/it, persisted in PlayerPrefs, default from system language). User verified.
- 2.5 done (league screen: ScorerTally/Season.Scorers in Sim.Core, LeagueView 3 tabs table/fixtures/scorers; 65 tests green, user verified).
- 2.6 done (match day instant: UserMatchLog handoff, MatchResultScreen with event timeline, LineupStrength harness best 72 vs worst 16 pts; 66 tests green, user verified).
- 2.7 done (season rollover: two-division worlds with DivisionStrengthStep 14, SeasonRollover P/R 3 + ages+1 + new fixtures from Pcg32(seed ^ year*mix, 777+division), CareerState v3 Leagues + legacy "League" JSON adapter + v2→v3 migration generating div2, SeasonService/SeasonEndScreen, Hub Next Match/End Season; 72 tests green, user verified). CareerFactory consts Div2GenSequence 55 / FixtureSequence 777 / Div2FixtureSequence 778 / Div2FirstClubId 101 / Div2FirstPlayerId 5001 — NEVER change (migrations depend on them).
- **🏁 PHASE 2 COMPLETE — vertical slice: a full season is playable end-to-end. Phase-end audit passed (determinism greps clean, no hardcoded UI strings, en/it tables at parity incl. format placeholders, asmdef layering respected, no file >300 lines in Sim.Core, no magic numbers outside BalanceConfig). Next: Phase 3 (3.1 match renderer) — not started.**
- 1.6 verified: combined hash 0xCDEA5A2F7B9E5CF6 identical on .NET, Unity Mono and IL2CPP (50 matches). DeterminismProbe stays in client/Assets/Scripts/App (disabled/removable from Boot scene).
- Match engine calibration (accepted by user): GoalCoefficient 0.92 → avg 2.44 goals/match, draws ~25%, team scoring 3+ in ~24% of matches, strong-vs-weak 82%, home advantage +9pp on home wins.
- Known quirks: generated defense ratings sit slightly above attack ratings (role-weight inflation) — GoalCoefficient compensates; extra per-match variance will come from form/fitness in 4.1.
