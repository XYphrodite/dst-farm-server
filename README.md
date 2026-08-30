# dstfarm

A .NET 10 console app with a full-screen TUI (Spectre.Console) that deploys a Don't Starve
Together dedicated server, generates a world tuned for idle farming, and keeps the server alive.

Русская версия: [README.ru.md](README.ru.md).

## How Klei drop farming actually works

Drops are credited to **the account of a player who is in the game**, not to the server.
A dedicated server farms nothing on its own — its job is to provide a world where a character
can stand around for days without dying, while barely loading the machine. You connect the
game client yourself and leave it running.

Two things are non-negotiable, or no drops are earned:

* `offline_cluster = false` in `cluster.ini` — an offline world does not count as play time.
  dstfarm always writes `false` and the interface cannot change it.
* `cluster_token.txt` — the token from your Klei account. Without it the server refuses to start.

Klei caps how many items you can receive per week; uptime beyond that cap adds nothing.

## Documentation

* [Quick start](docs/quickstart.md) — from nothing to a running server
* [How farming works](docs/farming.md) — what actually affects Klei drops
* [Server and client on one machine](docs/same-machine.md) — ports, load, overnight farming
* [Settings](docs/settings.md) — every `config.json` key
* [Troubleshooting](docs/troubleshooting.md)
* [Design](docs/architecture.md) — for anyone touching the code

## Install

In PowerShell:

```powershell
irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1 | iex
```

The script downloads the latest release, verifies the SHA-256 published in the release notes,
puts `dstfarm.exe` into `%LOCALAPPDATA%\Programs\dstfarm` and adds that directory to the user
PATH. No administrator rights needed.

A different directory or a specific version:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1))) -InstallDir 'D:\dstfarm' -Version v0.1.6
```

Parameters: `-InstallDir` where to install, `-Version` release tag (latest by default),
`-NoPath` leave PATH alone.

Manual install works too: grab `dstfarm.exe` from [releases](../../releases) — it is a
self-contained win-x64 build, so .NET is not required on the target machine.

`config.json` and `.runtime` (the server itself, logs, statistics) live next to the exe,
so install it where about 5 GB is free.

Updating is a command of its own:

```
dstfarm update
```

It compares the version with the latest release, downloads the new exe with a progress bar,
verifies the SHA-256 and swaps the file. Windows will not delete a running exe, so the old
build is renamed to `dstfarm.exe.old` and removed on the next run. `dstfarm update --check`
only reports whether an update exists. In the full-screen interface the same thing sits on
the `U` key. Stop the server before updating (`dstfarm stop`), otherwise the new version
only takes effect after a restart.

To uninstall: `dstfarm uninstall --yes`. It removes `.runtime`, `config.json`, the PATH entry
and finally the exe itself. Add `--all` to take the world and the cluster token with it.

## Build

```
dotnet build
dotnet run --project src/DstFarm.Cli
```

A release build is a single self-contained exe with no dependencies:

```
dotnet publish src/DstFarm.Cli -c Release
```

## Interface

Running without arguments opens the full-screen mode: farm settings on the left, status on
the right, live server log at the bottom.

```
arrows up/down   move between rows
arrows left/right  change the value (toggle for flags)
Enter            edit a text field (server name, password, token)
S                start / stop the server
I                install or update the server through steamcmd
G                write the settings into the cluster files
U                update dstfarm to the latest release
F1               key list
Q / Esc          quit (settings are saved)
```

If input or output is redirected (a pipe, CI), the full-screen mode is skipped and the app
prints the status instead.

## Language

The interface speaks English and Russian. By default it follows the system language: Russian
on a Russian Windows, English everywhere else. Override it with the **Language** row in the
interface or from the command line:

```
dstfarm config --set Language=en
```

Accepted values: `auto`, `ru`, `en`.

## Commands

| Command | What it does |
| --- | --- |
| `dstfarm` | full-screen interface |
| `dstfarm install [--no-validate]` | steamcmd + server install/update (app 343050) with a progress bar |
| `dstfarm init [--force]` | generates `cluster.ini`, the shards, `worldgenoverride.lua` |
| `dstfarm token <TOKEN>` | writes `cluster_token.txt` |
| `dstfarm start [--detach]` | supervisor: starts the shards, restarts them after a crash |
| `dstfarm stop` | graceful stop through `c_shutdown(true)` — the world is saved |
| `dstfarm status` | state, uptime, ports, connected players and which world protections actually applied |
| `dstfarm update [--check]` | update itself from the GitHub release |
| `dstfarm reset-world [--yes]` | delete the world so it is generated again with the current settings |
| `dstfarm console "<lua>"` | run a command in the running server's console |
| `dstfarm uninstall-server [--yes] [--all]` | remove the installed server (~4.2 GB); the world, token and settings stay |
| `dstfarm uninstall [--yes] [--all]` | remove dstfarm itself, its files and its PATH entry |
| `dstfarm config [--set KEY=VALUE ...]` | show or change settings |

First run: `install` → `token <TOKEN>` → `S` in the interface. The token comes from the game:
Account → Games → Servers → Add New Server.

## What is tuned for farming

The world (`worldgenoverride.lua`, Master shard):

* `day = "onlyday"` — eternal day, so Charlie never touches an idle character;
* eternal autumn (`autumn = "verylongseason"`, other seasons `noseason`) — no freezing, no overheating;
* `hunger = "nonlethal"` plus `darkness`, `shadowcreatures` and `brightmarecreatures` — hunger
  and the dark cannot *kill* an idle character. The meters still drain: the game has no setting
  that stops hunger or sanity from falling, only ones that make the result harmless;
* hounds, hunts, every raid boss, lightning, earthquakes, wildfires and rain are set to `never`;
* **both** hostile-creature groups are off — the one that decides what gets placed on the map
  (clockworks, tentacles, spider dens, tallbirds, walruses, killer bees) and the one that governs
  behaviour (frogs, wasps, mosquitos, merms, bats). Bosses are not what finishes off an idle character;
* `world_size = "small"` — fewer entities, less CPU over a long uptime.

The cluster (`cluster.ini`):

* `game_mode = endless` — death does not end the world;
* `pause_when_empty = false` — the world never pauses;
* `tick_rate = 15`, caves disabled by default (a second shard would double the load).

The supervisor restarts a crashed shard after a configurable delay, can restart the server at
a chosen hour, writes shard logs into `.runtime/logs/` and accumulates uptime in
`.runtime/state/uptime.json`.

## Layout

```
src/DstFarm.Core   config, cluster generation, steamcmd, supervisor
src/DstFarm.Cli    commands and the full-screen interface on Spectre.Console
tests              xunit tests for config generation and the farm profile
```

Settings live in `config.json` next to the exe, the cluster in
`%USERPROFILE%\Documents\Klei\DoNotStarveTogether\<cluster>`.

## Ports

Master listens on UDP `server_port` (10999 by default) plus the steam ports 27018/8768.
Caves, when enabled, take 11000 and 27019/8769. For internet play forward UDP 10999 and 27018;
when playing on the same machine there is nothing to forward.

`dstfarm status` shows every port as free or in use. The Steam client occupies part of the
27015-27050 range, so on a conflict move the server's steam ports:
`dstfarm config --set MasterServerPort=27030 AuthenticationPort=8790`, then
`dstfarm init --force`. Details in [same-machine.md](docs/same-machine.md).

## License

MIT — see [LICENSE](LICENSE).
