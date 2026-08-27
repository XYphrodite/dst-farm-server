# Design

Русская версия: [ru/architecture.md](ru/architecture.md).

```
src/DstFarm.Core    logic without a user interface
src/DstFarm.Cli     commands and the full-screen screen on Spectre.Console
tests               xunit tests for config generation and the farm profile
```

.NET 10, C# 14, `TreatWarningsAsErrors`. A release build is a single self-contained
`dstfarm.exe` for win-x64 — .NET is not needed on the target machine.

## Core

**`FarmConfig`** — every setting and the paths derived from them. Serialised into `config.json`
next to the exe. `HasClusterToken()` lives here too: a token counts as valid when the file
exists and holds more than 20 characters.

**`ClusterWriter`** — generates the cluster directory: `cluster.ini`, one `server.ini` and one
`worldgenoverride.lua` per shard, an empty `modoverrides.lua`. Existing files are not
overwritten without `overwrite: true`, and `modoverrides.lua` is never touched — that is where
people put their mods. The farm profile lives in `BuildWorldGen`. `MatchesDisk` compares the
settings with what is actually on disk, so the interface cannot show values the server does
not run with.

**`SteamCmdInstaller`** — downloads `steamcmd.zip`, unpacks it, runs
`+login anonymous +app_update 343050`. On a clean machine the first run goes into updating
steamcmd itself, which then exits with code 7 without installing anything, so the install is
retried up to three times.

**`SteamProgress` / `SteamCmdOutput`** — parses the progress lines steamcmd prints. Two formats
matter: `app_update` in bytes and the steamcmd bootstrap in kilobytes.

**`LogTail`** — follows a file that is being appended to. Needed because the server's stdout
goes into a pipe and is buffered in blocks: the panel froze while the server was running fine.
DST writes its own log immediately, so that is what we read; if the file does not appear within
20 seconds we fall back to stdout so the panel is not silent.

**`ServerSupervisor`** — keeps the shards alive. Each shard is a `ShardRunner`: the server
process with stdout/stderr captured (mirrored into a file) and stdin held open, which is how
`c_shutdown(true)` is delivered on stop. A loop checks the processes every two seconds, brings
crashed ones back after `RestartDelaySeconds`, watches the stop flag and the scheduled restart
time. The process is killed only if it fails to exit within 45 seconds.

**`SupervisorControl`** — controls a supervisor running in another process: pid file, stop flag,
and starting this same exe with the `supervise` argument.

**`SelfUpdater`** — updates from GitHub releases: fetches the latest release, verifies the
SHA-256 published in the notes, and swaps the exe. Windows will not delete a running exe but
allows renaming it, so the old build moves to `.old` and is cleaned up on the next start.

**`UptimeTracker`** — accumulates total uptime in `.runtime/state/uptime.json`.

**`Loc`** — localisation. The string pairs live at the call site instead of in a key table, so
nothing can drift out of sync and nobody has to look up what a key means. The language follows
the system by default and is overridden by the `Language` setting.

## Cli

**`Program`** — argument parsing and the commands. Without arguments the full-screen mode opens;
when input or output is redirected it prints the status instead.

**`Dashboard`** — a screen built on `AnsiConsole.Live` inside the terminal's alternate buffer.
The settings are a list of `SettingItem` values (flag, choice, number, text), each knowing how
to render itself and what the arrows do. Changes are flagged until they are applied with `G`.

The supervisor runs as a task inside the interface process, which is why its log is visible
live in the bottom panel. For an unattended start there is `start --detach`, where the
supervisor becomes a separate process.

## Boundaries

The tool does not touch the game client: it does not launch it, does not emulate input and does
not influence drops. All it does is install the server, write configs and keep the process alive.
