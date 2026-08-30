# Settings

Русская версия: [ru/settings.md](ru/settings.md).

Everything lives in `config.json` next to the exe. Three ways to change it: the full-screen
interface, `dstfarm config --set KEY=VALUE`, or editing the file by hand.

World settings must then be applied to the cluster files: the `G` key in the interface or
`dstfarm init --force`. Until that happens the values on screen differ from the ones the
server actually runs with — the interface says so in its header, and `dstfarm status` adds a
`settings: not applied` row.

The server also has to be restarted: it reads the configs at startup.

> An already generated world keeps the settings baked into it at creation time: editing the
> configs afterwards changes nothing for it. To apply new world settings, regenerate the world
> with `dstfarm reset-world --yes` — that wipes the progress.

## Farm profile

| Key | Default | What it does |
| --- | --- | --- |
| `OnlyDay` | `true` | `day = "onlyday"` — eternal day, Charlie never touches an idle character |
| `EternalAutumn` | `true` | eternal autumn: `autumn = "verylongseason"`, other seasons `noseason` |
| `NoHunger` | `true` | `hunger = "nonlethal"` — starving cannot kill. The hunger meter still drains, nothing in the game stops that |
| `NoSanityDrain` | `true` | `darkness = "nonlethal"`, `shadowcreatures` and `brightmarecreatures` set to `never` — the dark and the shadows cannot hurt you. Sanity itself still drains; the game has no setting for that |
| `DisableThreats` | `true` | hounds, hunts, every raid boss, lightning, earthquakes, wildfires **and the entire hostile-creature group** (frogs, wasps, mosquitos, spiders, merms, bats, and the cave ones) set to `never` |
| `WorldSize` | `small` | `small` / `medium` / `default` / `large` / `huge`. Smaller world, less CPU |
| `GameMode` | `endless` | `endless` does not end the world on death. `survival` and `wilderness` also exist |
| `EnableCaves` | `false` | second shard. Doubles the load and is useless for uptime |
| `HungerPaused` | `false` | freezes hunger completely through the server console. Not a world setting: `Hunger:Pause()` stops both the meter and the starvation damage. Reconfirmed once a minute while a player is in the world — a single command at join time lands while `AllPlayers` is still empty |
| `AllRecipes` | `false` | unlocks every recipe, so nothing needs prototyping and no Science Machine is required. Maintained the same way |
| `OnPlayerJoin` | empty | console commands run whenever a player joins |

## Server

| Key | Default | What it does |
| --- | --- | --- |
| `Cluster` | `FarmCluster` | cluster folder name under `Documents\Klei\DoNotStarveTogether` |
| `ClusterName` | `Farm Idle Server` | name in the server list |
| `ClusterPassword` | empty | join password |
| `ClusterToken` | empty | Klei token, mirrored into `cluster_token.txt` |
| `ServerPort` | `10999` | UDP world port. Caves take `ServerPort + 1` |
| `MaxPlayers` | `6` | player limit |
| `MasterServerPort` | `27018` | steam master port. Caves take `+1` |
| `AuthenticationPort` | `8768` | steam auth port. Caves take `+1` |

The steam ports are configurable for a reason: the Steam client on the same machine occupies
part of the 27015-27050 range, and the server then fails to start. Check with
`dstfarm status`; details in [same-machine.md](same-machine.md).

## Supervisor

| Key | Default | What it does |
| --- | --- | --- |
| `RestartOnExit` | `true` | bring a shard back up if the process died |
| `RestartDelaySeconds` | `10` | pause before restarting |
| `DailyRestartHour` | `-1` | scheduled restart at the given hour, `-1` disables it |
| `ExtraArguments` | empty | extra command line arguments for the server |

## Interface

| Key | Default | What it does |
| --- | --- | --- |
| `Language` | `auto` | `auto`, `ru` or `en`. `auto` follows the system language |

## Paths

| Key | Default | What it does |
| --- | --- | --- |
| `Root` | `.runtime` next to the exe | root for steamcmd, the server, logs and state |
| `ServerDirectory` | `<Root>\server` | where steamcmd installs the server |
| `SteamCmdDirectory` | `<Root>\steamcmd` | where steamcmd is unpacked |
| `ConfDirectory` | `Documents\Klei\DoNotStarveTogether` | cluster directory |

When `ConfDirectory` is left empty, the server is not given `-persistent_storage_root` and
`-conf_dir` — it finds the standard path itself. Set it only deliberately.

## What cannot be changed

`offline_cluster` is always `false`: an offline world does not count as play time and earns no
Klei drops. That is the whole point of this tool.
