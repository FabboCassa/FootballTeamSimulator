# Football Team Simulator

Cartoonish football-manager-like game. Single player + online (public ranked & private leagues).
See [ARCHITECTURE.md](ARCHITECTURE.md) for design, [ROADMAP.md](ROADMAP.md) for the plan.

## Repo layout

| Path | What |
|---|---|
| `shared/Sim.Core` | All game rules (deterministic, no Unity deps). Used by client AND server. |
| `shared/Contracts` | DTOs shared client/server |
| `client/` | Unity project (Unity 6.3 LTS, UI Toolkit, VContainer, UniTask) |
| `server/` | ASP.NET Core backend (net10.0) + docker-compose (PostgreSQL, Redis) |
| `tools/` | Build scripts |

## Prerequisites

- **.NET 10 SDK** — https://dotnet.microsoft.com/download (verify: `dotnet --version`)
- **Unity Hub + Unity 6.3 LTS (6000.3.x)** with modules: *WebGL*, *Windows*, *Android* (iOS later)
- **Docker Desktop** (only for backend work, Phase 7+)

## First-time setup (order matters)

```powershell
# 1. Build shared DLLs and copy them into the Unity project (required BEFORE opening Unity)
.\tools\build-simcore.ps1

# 2. Run the tests
dotnet test

# 3. Open client/ with Unity Hub (Add project from disk -> select the client folder)
#    First open generates Library/ and remaining ProjectSettings (slow once, ~minutes).
#    If your installed 6000.3 patch differs from ProjectVersion.txt, let Unity upgrade.

# 4. (Phase 7+) Backend dev infrastructure
cd server && docker compose up -d
dotnet run --project server/Api    # -> http://localhost:5080/health
```

After any change to `shared/`, re-run `tools/build-simcore.ps1` so Unity picks up the new DLLs.

## Conventions

- Game rules ONLY in `Sim.Core` (deterministic, seeded RNG, no I/O) — see ARCHITECTURE.md §4.1
- Unity: MVP pattern, DI via VContainer, no singletons/god classes, asmdef per layer
- Every roadmap task is tested before being ticked off (see ROADMAP.md working agreement)

## Still to do in-editor (cannot be scaffolded from files)

1. Create `Assets/Scenes/Boot.unity`, add an empty GameObject with the `Boot` component, set it as first scene in Build Settings.
2. (Recommended for 2D cartoon style) Install **URP** via Package Manager and create a 2D Renderer pipeline asset.
3. Verify the Console logs `App started. Sim.Core v0.1.0` in Play mode → Roadmap tasks 0.2/0.3 done.
