# Server and client on one machine

Русская версия: [ru/same-machine.md](ru/same-machine.md).

This is the normal and most common setup: dstfarm holds the dedicated server while the game
sits next to it and connects as a client. Here is what differs from a separate box.

## Joining your own server

In the game: **Browse Games** → the **LAN** tab. A server on the same machine always shows up
there, even before it appears in the public list. A password, if set, is asked at join time.

The server does not have to be reachable from the internet, but `offline_cluster` must stay
`false` — otherwise the time does not count and no Klei drops are earned. dstfarm guarantees that.

## Ports: the main trap

The server takes three UDP ports per shard:

| Port | Setting | Purpose |
| --- | --- | --- |
| 10999 | `ServerPort` | the world itself |
| 27018 | `MasterServerPort` | steam master |
| 8768 | `AuthenticationPort` | steam auth |

The problem is that the Steam client on the same machine uses part of the 27015–27050 range.
If it grabbed 27018 first, the server will not come up and the log shows an opaque socket error.

Check what is taken:

```
dstfarm status
```

Every port is listed as free or in use. The supervisor also warns about busy ports in the log
at startup.

On a conflict, move the ports:

```
dstfarm config --set MasterServerPort=27030 AuthenticationPort=8790
dstfarm init --force
```

The same rows exist in the interface: **Steam master port** and **Steam auth port**.
`ServerPort + 1`, `MasterServerPort + 1` and `AuthenticationPort + 1` go to the caves shard
when it is enabled, so leave the neighbouring numbers free.

Nothing needs forwarding on the router when you play alone. The Windows firewall asks once at
the first server start — allow it for private networks.

## Load

The game client is heavier than the server, but together they add up. What helps:

* **Caves disabled** (the default). A second shard is a second server process with its own
  world, and it does nothing for uptime.
* **A `small` world** (the default). `huge` means more entities and more work every tick.
* **All threats disabled** (the default). No hounds and no bosses means no load spikes and no
  risk of an idle character being eaten.
* Lowering the graphics settings and capping the frame rate in the game changes nothing about
  farming, but keeps the machine quieter.

A small world does not ask for much memory, but keep in mind that the client and the server
load their assets independently.

## Leaving it overnight

The server takes care of itself: `dstfarm start --detach` or the `S` key, plus an automatic
restart after a crash. But the drops go to the client, so the game must stay open — the
character has to remain in the world.

Make sure the machine does not fall asleep: Settings → System → Power → Screen and sleep,
with sleep set to Never. Turning the display off does not affect farming.

A scheduled restart during a quiet hour is worth enabling — the server saves and comes back up:

```
dstfarm config --set DailyRestartHour=5
```

After it the client has to reconnect: the server restarts and the player drops out of the world.

## Game updates

When Klei ships a patch, the client updates through Steam and the server does not. The versions
diverge and joining fails. The cure:

```
dstfarm stop
dstfarm install
dstfarm start --detach
```

dstfarm itself is updated by `dstfarm update`, independently of the game version.
