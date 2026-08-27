# How farming actually works in Don't Starve Together

Русская версия: [ru/farming.md](ru/farming.md).

Worth reading before expecting results.

## Klei drops

Skins and chests are granted to **the account of a player who is in the game**, not to the
server. A dedicated server earns nothing by itself. Its role is different: to provide a world
where a character can stand around for days without dying, while barely loading the machine.

Three conditions, or no drops:

1. **A client is connected to the world.** Time in game counts, not server uptime.
2. **`offline_cluster = false`.** An offline world does not count. dstfarm always writes
   `false` and does not let the interface change it.
3. **`cluster_token.txt` is in place.** Otherwise the server simply will not come up.

Klei caps how many items you can receive per week. Once the cap is reached, further uptime
adds nothing until it resets.

## Farming resources

The other meaning of "farming" is materials in the world. Here worldgen settings decide, and
they are already tuned for a long safe session (see [settings.md](settings.md)): plenty of
time, no threats, seasons disabled. If you specifically want resources, raise `World size` to
`huge` and enable caves — but expect a noticeably higher CPU load.

## What this tool does not do

* It does not drive the game client and does not emulate input. Getting a character into the
  world is on you.
* It does not bypass Klei limits and does not affect the drop rate.
* It does not grant items that only come from events or purchases.
