# dstfarm - Project Context

## Project Overview

**dstfarm** is a Windows console application that deploys and supervises a **Don't
Starve Together** dedicated server tuned for idle farming of Klei item drops. It
installs the server through SteamCMD, generates a cluster whose world cannot kill an
unattended character, keeps the shards alive, and reports what the running world is
actually doing — all from a full-screen terminal interface or plain commands.

**Platform**: .NET 10 (`net10.0`), win-x64, Windows 10/11
**Language**: C# 14
**Domain**: Game server management / unattended operation tooling

> **Scope note**: The tool never touches the game client. It does not launch it, emulate
> input, or influence drop rates. Klei credits drops to *the account of a player who is
> in the game*, so the server's only job is to provide a world that survives being
> ignored. See [docs/farming.md](docs/farming.md).

---

## Technical Stack

### Runtime & Frameworks

| Layer | Technology | Notes |
|-------|-----------|-------|
| Application | .NET 10 console (`net10.0`) | `dstfarm.exe`, self-contained single file in Release |
| Terminal UI | Spectre.Console 0.57.2 | Alternate screen buffer + `Live` display |
| Tests | xUnit 2.9.3 | 167 tests, all in `DstFarm.Core.Tests` |
| Managed server | DST Dedicated Server, Steam app `343050` | Installed via SteamCMD, anonymous login |
| Distribution | GitHub Releases + `install.ps1` | SHA-256 published in the release notes |

### Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Spectre.Console` | 0.57.2 | Panels, tables, progress bars, live full-screen layout |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | Test host |
| `xunit` | 2.9.3 | Test framework |
| `xunit.runner.visualstudio` | 3.1.4 | Test discovery |
| `coverlet.collector` | 6.0.4 | Coverage collection |

No other runtime dependencies. `Release` builds set `SelfContained` and
`PublishSingleFile`, so the target machine needs no .NET installation.

### Codebase Size

Hand-written C# only, blank lines excluded.

| Project | Files | Code lines |
|---------|------:|-----------:|
| `DstFarm.Core` | 17 | 1,847 |
| `DstFarm.Cli` | 4 | 1,230 |
| `DstFarm.Core.Tests` | 18 | 1,572 |
| **Total** | **39** | **4,649** |

---

## Architecture Overview

dstfarm is a supervisor wrapped around a game server it does not control directly. The
server is a black box that reads configuration files at startup and writes a log; every
capability in this tool is built on those two surfaces plus its standard input.

```
                            GitHub Releases            Klei / Steam
                          (dstfarm.exe + SHA)      (app 343050, auth, drops)
                                   |                        |
                              SelfUpdater              SteamCmdInstaller
                                   |                        |
  +--------------------------------|------------------------|-------------------+
  |  dstfarm.exe                   v                        v                   |
  |                                                                             |
  |   DstFarm.Cli                                                               |
  |     Program        -- command dispatch, status, install, update, console     |
  |     Dashboard      -- full-screen panel (settings | status | live log)       |
  |                                                                             |
  |   DstFarm.Core                                                              |
  |     FarmConfig     -- config.json, derived paths, farm profile flags         |
  |     ClusterWriter  -- cluster.ini, server.ini, worldgenoverride.lua          |
  |     ServerSupervisor -- spawn, restart, graceful stop, stall watchdog        |
  |     LogTail / WorldProtections / PlayerWatch -- read the truth from the log  |
  |     ConsoleQueue   -- commands into the running server's stdin               |
  +-----------------------------|-----------------------------------------------+
                                |
                    stdin (c_shutdown, console commands)
                    stdout (buffered - not trusted for live output)
                                v
  +---------------------------------------------------------------------------+
  |  dontstarve_dedicated_server_nullrenderer_x64.exe   (shard "Master")       |
  |     reads   Documents\Klei\DoNotStarveTogether\<cluster>\...               |
  |     writes  <cluster>\Master\server_log.txt   <- live, unbuffered          |
  +---------------------------------------------------------------------------+
```

### Design Principles

1. **The log is the source of truth.** The config on disk and the running world drift
   apart, because DST bakes world settings in at generation time. Anything the interface
   claims about the live world (`WorldProtections`, `PlayerWatch`) is parsed from what
   the server itself reported, never from what was requested.
2. **Never trust the child's stdout for liveness.** Windows buffers a piped stdout in
   blocks, so the panel froze while the server was healthy. Live output is tailed from
   the server's own log file; captured stdout is kept only as a mirror and a fallback.
3. **Verify against the game's own scripts.** World settings are validated against
   `scripts/map/customize.lua` from the installed server build. An unknown key or value
   is only a warning in DST — the setting is silently dropped — so a guard test asserts
   every generated pair exists in the game.
4. **Destructive actions require an explicit `--yes`.** `reset-world`, `uninstall-server`
   and `uninstall` first print what they would remove and how much space it frees.
5. **Silence is a bug.** A server that stalls after authentication, settings that were
   never applied, an idle player about to be dropped — each is surfaced in words rather
   than left for the operator to notice.

---

## Project Components

### 1. **DstFarm.Core**
**Type**: Class library
**Location**: `src/DstFarm.Core/`
**Purpose**: Everything except the user interface.

**Key types**:

| Type | Responsibility |
|------|----------------|
| `FarmConfig` | Settings, derived paths, farm-profile flags, `config.json` round-trip |
| `ClusterWriter` | Generates `cluster.ini`, per-shard `server.ini` and `worldgenoverride.lua`; `MatchesDisk` detects config/world drift; `ResetWorld` deletes the saved world |
| `SteamCmdInstaller` | Downloads SteamCMD, installs app `343050`, retries the self-update exit |
| `SteamProgress` / `SteamCmdOutput` | Parses both SteamCMD progress formats into a progress bar |
| `ServerSupervisor` | Spawns shards, restarts after a crash, scheduled restart, `c_shutdown(true)`, stall watchdog, join commands |
| `SupervisorControl` | Controls a supervisor living in another process (pid file, stop flag) |
| `LogTail` | Follows an appended file, survives truncation and a writer holding it open |
| `WorldProtections` | Parses `OVERRIDE: setting` lines and compares them with the expected set |
| `PlayerWatch` | Parses connect/authenticate/disconnect lines into the current player list |
| `PortProbe` | Reports which of the server's UDP ports are already in use |
| `ConsoleQueue` | File-backed queue delivering console commands into the server's stdin |
| `SelfUpdater` | GitHub release lookup, SHA-256 verified download, running-exe swap |
| `ServerInstall` / `Uninstaller` | Removal of the server build, and of dstfarm itself |
| `UptimeTracker` | Accumulated uptime across sessions |
| `Loc` | Russian/English strings, paired at the call site |
| `ListWindow` | Which slice of a long list to render around the selection |

### 2. **DstFarm.Cli**
**Type**: Console application -> `dstfarm.exe`
**Location**: `src/DstFarm.Cli/`
**Purpose**: Commands and the full-screen interface.

**Key pieces**:
- `Program` — command dispatch, `status`, install/update progress, destructive-action
  confirmations. Falls back to printing status when stdin/stdout are redirected.
- `Tui/Dashboard` — alternate-screen `Live` layout: farm settings on the left, status on
  the right, live server log at the bottom. Caches log-derived state so a panel redrawn
  several times per second does not re-read a large file.
- `Tui/SettingItem` — one settings row: flag, choice, number or text, each knowing how to
  render itself and what the arrow keys do.
- `Tui/LogBuffer` — ring buffer of log lines.

### 3. **DstFarm.Core.Tests**
**Type**: xUnit test project
**Location**: `tests/DstFarm.Core.Tests/`
**Purpose**: 167 tests over config generation, the farm profile, log parsing and the
destructive paths.

**Notable coverage**:
- `WorldGenValidityTests` — every generated key/value must exist in the game's own list.
- `LogTailTests` — append, truncation on restart, reading a file the server holds open.
- `WorldProtectionsTests` / `PlayerWatchTests` — the real tab-separated log format.
- `UninstallerTests` — PATH editing keeps foreign entries, ignores case and trailing slashes.
- `ClusterSyncTests` / `ClusterRewriteTests` — config/world drift detection, including
  hand-edited files and the file DST rewrites for itself after generating a world.

---

## The Farm Profile

What the generated world does, and why. Keys verified against `scripts/map/customize.lua`.

| Setting | Value | Reason |
|---------|-------|--------|
| `day` | `onlyday` | Eternal day; Charlie never touches an idle character |
| `autumn` / others | `verylongseason` / `noseason` | One mild season: no freezing, no overheating |
| `hunger` | `nonlethal` | Starving cannot kill. **The meter still drains** — the game has no setting that stops it |
| `darkness`, `shadowcreatures`, `brightmarecreatures` | `nonlethal` / `never` | There is no `sanity` world setting; these are what actually threaten a low-sanity character |
| `temperaturedamage` | `nonlethal` | Belt and braces with eternal autumn |
| Raid bosses, hounds, hunts, lightning, earthquakes, wildfires | `never` | Scheduled threats |
| Hostile creatures, **both groups** | `never` | Behaviour *and* worldgen placement — clockworks, tentacles, spider dens, frogs, wasps, merms, tallbirds |
| `world_size` | `small` | Fewer entities, less CPU over a long uptime |
| `game_mode` | `endless` | Death does not end the world |
| `pause_when_empty` | `false` | The world never pauses |
| `offline_cluster` | `false` (**fixed**) | An offline world earns no drops; the interface cannot change this |

⚠️ **World settings are baked in at generation.** Editing the configs afterwards changes
nothing for an existing world. `dstfarm status` reports the drift; `dstfarm reset-world`
resolves it.

`HungerPaused` is deliberately **not** a world setting: it runs `Hunger:Pause()` through
the console on every player join, because that state lives only in the component and
survives neither a rejoin nor a restart.

---

## Commands

| Command | What it does |
|---------|--------------|
| `dstfarm` | Full-screen interface |
| `dstfarm install [--no-validate]` | SteamCMD + server install/update with a progress bar |
| `dstfarm init [--force]` | Generates the cluster files |
| `dstfarm token <TOKEN>` | Writes `cluster_token.txt` |
| `dstfarm start [--detach]` | Supervisor: starts the shards, restarts them after a crash |
| `dstfarm stop` | Graceful stop through `c_shutdown(true)` — the world is saved |
| `dstfarm status` | State, uptime, ports, connected players, applied world protections |
| `dstfarm console "<lua>"` | Runs a command in the live server's console |
| `dstfarm reset-world [--yes]` | Deletes the world so it is generated again |
| `dstfarm update [--check]` | Updates itself from the GitHub release |
| `dstfarm uninstall-server [--yes] [--all]` | Removes the ~4.2 GB server build |
| `dstfarm uninstall [--yes] [--all]` | Removes dstfarm, its files and its PATH entry |
| `dstfarm config [--set KEY=VALUE ...]` | Shows or changes settings |

### Interface keys

```
arrows up/down     move between rows
arrows left/right  change the value (toggle for flags)
Enter              edit a text field;  Del clears it
S                  start / stop the server
I                  install or update the server
G                  write the settings into the cluster files
U                  update dstfarm
F1                 key list
Q / Esc            quit (settings are saved)
```

---

## Build & Distribution

### Local build

```bash
dotnet build
dotnet test
dotnet run --project src/DstFarm.Cli
```

### Release build

```bash
dotnet publish src/DstFarm.Cli -c Release
```

Produces a single self-contained `dstfarm.exe` (~71 MB) for win-x64.

### Installation

```powershell
irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1 | iex
```

[install.ps1](install.ps1) resolves the latest release, verifies the SHA-256 published in
the release notes, installs into `%LOCALAPPDATA%\Programs\dstfarm` and adds it to the
user PATH. No administrator rights required.

⚠️ **`install.ps1` is deliberately ASCII-only and BOM-less.** Windows PowerShell 5.1
reads a BOM-less script as ANSI, while `irm | iex` chokes on a leading BOM. ASCII is the
only encoding that satisfies both paths, which is why its messages are English.

---

## Directory Structure

```
dst-farm-server/
├── src/
│   ├── DstFarm.Core/            # config, cluster generation, steamcmd, supervisor, log parsing
│   └── DstFarm.Cli/             # commands + full-screen interface
│       └── Tui/                 # Dashboard, SettingItem, LogBuffer
├── tests/
│   └── DstFarm.Core.Tests/      # 167 xUnit tests
├── docs/                        # English documentation
│   └── ru/                      # Russian documentation
├── install.ps1                  # one-command installer (ASCII, no BOM)
├── Directory.Build.props        # version, nullable, TreatWarningsAsErrors, LangVersion 14
├── DstFarm.slnx                 # solution
├── README.md                    # English
└── README.ru.md                 # Russian
```

### Runtime layout on a client machine

```
%LOCALAPPDATA%\Programs\dstfarm\
├── dstfarm.exe
├── config.json                  # all settings
└── .runtime\
    ├── steamcmd\                # ~50 MB
    ├── server\                  # ~4.2 GB, the DST dedicated server build
    ├── logs\master.log          # captured stdout, appended across sessions
    └── state\                   # pid file, stop flag, console queue, uptime.json

%USERPROFILE%\Documents\Klei\DoNotStarveTogether\<cluster>\
├── cluster.ini
├── cluster_token.txt
└── Master\
    ├── server.ini
    ├── worldgenoverride.lua
    ├── modoverrides.lua         # never overwritten - user mods live here
    ├── save\                    # the world
    └── server_log.txt           # live, unbuffered - the interface reads this
```

---

## Development Status

### Implemented ✅
- [x] SteamCMD deployment of the server, with retry across its self-update exit
- [x] Cluster generation with a farm profile verified against the game's own script list
- [x] Supervisor: crash restart, scheduled restart, graceful `c_shutdown(true)`, stall watchdog
- [x] Live log from the server's own file, immune to stdout buffering
- [x] Full-screen interface: scrolling settings, status, live log, in-place editing
- [x] Russian and English, following the system language by default
- [x] Self-update from GitHub releases with SHA-256 verification
- [x] `status` reports which protections the running world actually applied
- [x] Connected-player display parsed from the server log
- [x] Console commands into the live server, and commands replayed on every player join
- [x] Complete hunger freeze via `Hunger:Pause()`, reapplied automatically
- [x] Port conflict detection (the Steam client occupies part of 27015-27050)
- [x] Removal of the server build and of dstfarm itself, including its PATH entry

### Planned 📋
- [ ] Detect and report an incoming Klei drop — the server has a `giftreceiver`
      component and an `OpenGift` RPC, but no log marker has been observed yet
- [ ] Warn before the 30-minute idle disconnect (`IdleTimeout: 1800s`) drops the player
- [ ] Caves shard verification — the second shard is generated but never run in anger
- [ ] A CI pipeline; releases are currently built and published from a workstation

### Known Issues ⚠️
- **Drops cannot be observed from the server.** Searching the shard log for gift markers
  has so far returned nothing, so `OnPlayerJoin` automation cannot react to a drop.
- **The idle timeout is not configurable.** The server reports `IdleTimeout: 1800s` and
  no `cluster.ini` key for it is known, so a genuinely unattended session ends after
  30 minutes regardless of settings.
- **A stall after authentication was observed once** (2026-08-29) and did not recur after
  the world was regenerated. `CURL ERROR: (dst.metrics.klei.com) Resolving timed out` in
  the same log suggests flaky DNS rather than configuration. The supervisor now reports
  the condition instead of sitting silent.
- **The Caves shard is untested.** Its settings are generated and unit-tested, but no
  two-shard cluster has been run.

---

## Glossary

| Term | Meaning |
|------|---------|
| **Cluster** | A DST server configuration: one `cluster.ini` plus one directory per shard |
| **Shard** | One server process and one world; `Master` is the surface, `Caves` the underground |
| **Cluster token** | Per-account token from Klei; without it an online server refuses to start |
| **Worldgen override** | `worldgenoverride.lua` — settings baked into a world when it is generated |
| **Drop** | An item Klei credits to a player's account for time spent in game |
| **Nonlethal** | A world setting that keeps a hazard from killing, without removing it |
| **Idle farming** | Leaving a character in a safe world to accumulate play time |

---

## Document Information

**Document Version**: v1.0
**Last Updated**: 2026-08-30
**Application Version**: v0.4.5
**Status**: Active
**Repository**: `c:\Repos\dst-farm-server` (branch `main`) — https://github.com/XYphrodite/dst-farm-server
**Authoring style**: per [ProjectContext_UserRules.md](../Reborn/desktop-client/ProjectContext_UserRules.md)
**Related docs**: [README.md](README.md), [README.ru.md](README.ru.md),
[docs/quickstart.md](docs/quickstart.md), [docs/farming.md](docs/farming.md),
[docs/settings.md](docs/settings.md), [docs/same-machine.md](docs/same-machine.md),
[docs/troubleshooting.md](docs/troubleshooting.md),
[docs/architecture.md](docs/architecture.md)
