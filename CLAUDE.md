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
- Current test count: 44 green.

## Architecture quick reference
- `shared/Sim.Core` (netstandard2.1, no Unity deps, no I/O, deterministic): ALL game rules. Namespaces: Domain, Generation, Match, Config, Random (+ empty: Market, Development, Condition, Tactics, Career).
- Determinism rules: only `Pcg32`/`IRandomSource` for randomness; no `Math.Pow/Exp/Log/Sin` (use IntPow pattern); no DateTime.Now; integer math where possible. This enables replays = (seed + inputs) and the IL2CPP cross-check (task 1.6).
- Every tunable lives in `Config/BalanceConfig.cs` (sections: Generation, Match). No magic numbers in systems.
- `server/` = ASP.NET Core skeleton (Api/Application/Infrastructure) with /health; docker-compose (postgres 17, redis 8). Real backend work starts Phase 7.
- Unity client: MVP + VContainer + UniTask + UI Toolkit, asmdefs FTS.App/Services/Presenters/Views/MatchView. Only Boot scene exists so far.

## Status (2026-06-12)
- Phase 0 done (0.5 CI: workflow exists, verified on first GitHub push).
- Phase 1 done. **Next: 2.1 app shell & navigation** (Boot → MainMenu → Hub, ScreenNavigator, DI scopes, message bus). MatchEngine.Version = 2 since 1.5 (position stream; score/events per seed unchanged from v1).
- 1.6 verified: combined hash 0xCDEA5A2F7B9E5CF6 identical on .NET, Unity Mono and IL2CPP (50 matches). DeterminismProbe stays in client/Assets/Scripts/App (disabled/removable from Boot scene).
- Match engine calibration (accepted by user): GoalCoefficient 0.92 → avg 2.44 goals/match, draws ~25%, team scoring 3+ in ~24% of matches, strong-vs-weak 82%, home advantage +9pp on home wins.
- Known quirks: generated defense ratings sit slightly above attack ratings (role-weight inflation) — GoalCoefficient compensates; extra per-match variance will come from form/fitness in 4.1.
