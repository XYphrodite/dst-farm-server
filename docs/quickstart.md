# Quick start

Русская версия: [ru/quickstart.md](ru/quickstart.md).

## 1. What you need

* Windows x64.
* A free Klei account — the cluster token comes from it.
* About 5 GB of free space: the server build downloads compressed (~2.9 GB of traffic)
  and unpacks to ~4.2 GB. Klei ships the dedicated server as almost the full game build —
  all assets plus both the 32- and 64-bit binaries.
* A copy of Don't Starve Together on Steam, so you can join the server with a client.
  The server itself is installed separately and needs no Steam account.

.NET is not required on the target machine: the build is self-contained.

## 2. Install dstfarm

In PowerShell:

```powershell
irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1 | iex
```

The script downloads the latest release, verifies its SHA-256, puts the exe into
`%LOCALAPPDATA%\Programs\dstfarm` and adds that directory to PATH. No administrator rights
needed. Use `-InstallDir` for another location — see [README](../README.md#install).

## 3. Get a cluster token

1. Start the game.
2. Account → Games → Servers → **Add New Server**.
3. Copy the long token string.

A second way, if the account page is unavailable: open the in-game console with `~` and run
`TheNet:GenerateClusterToken()` — a `cluster_token.txt` appears in the client settings
directory, and its contents are what you need.

Without the token the server will not start and no drops are earned.

## 4. Install the DST server

```
dstfarm install
```

This downloads steamcmd and then the server build (app 343050, anonymous login). A progress
bar shows the stage, the percentage and the size: `verifying update  1.5 GB / 4.2 GB  —  36.1%`.

The first steamcmd run goes into updating itself, after which it exits without installing
anything. dstfarm detects that and retries on its own — that is expected, do not interrupt it.

## 5. Configure and start

```
dstfarm
```

The full-screen interface opens:

1. Move down to **Cluster token**, press Enter, paste the token, press Enter.
2. Adjust the server name, password and farm flags if you like.
3. `G` writes the settings into the cluster files.
4. `S` starts the server.

The same thing with commands:

```
dstfarm token <YOUR_TOKEN>
dstfarm init --force
dstfarm start
```

## 6. Join with the client

In the game: Browse Games → the **LAN** tab (if the server runs on the same PC), or find it
by name in the public list. Join with a character and leave the client running — that time
is exactly what earns drops. Ports and load when both run on one machine are covered in
[same-machine.md](same-machine.md).

Check that everything is alive:

```
dstfarm status
```

## 7. Stop

`S` in the interface or `dstfarm stop`. The server receives `c_shutdown(true)` and saves the
world before exiting — do not kill the process by hand, you would lose everything since the
last autosave.

## Start automatically on login

```
dstfarm start --detach
```

Put this command into Task Scheduler with an "At log on" trigger.
