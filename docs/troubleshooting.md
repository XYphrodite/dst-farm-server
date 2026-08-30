# Troubleshooting

Русская версия: [ru/troubleshooting.md](ru/troubleshooting.md).

Start with the shard logs: `.runtime\logs\master.log` (and `caves.log` if caves are enabled).
They hold the full server output, not just what fit in the panel.

## The server does not start

**"cluster_token.txt is missing"** — the token is absent or shorter than 20 characters. Get it
from the game: Account → Games → Servers → Add New Server, then `dstfarm token <TOKEN>`.

**"the server is not installed"** — the install step never ran. `dstfarm install` or the `I` key.

**`Your Server Will Not Start` in the log** — almost always a broken or foreign token.
Generate a new one in your Klei account.

## A port is in use

`dstfarm status` shows every server port as free or in use. When the server and the game
client share a machine, Steam may take 27018 for itself. Move the ports:

```
dstfarm config --set MasterServerPort=27030 AuthenticationPort=8790
dstfarm init --force
```

Details in [same-machine.md](same-machine.md).

## The install breaks off

On a clean machine the first steamcmd run goes into updating itself: it exits with code 7
without installing anything. dstfarm recognises that and retries on its own, up to three
times — the log says "steamcmd updated itself, retrying the install".

If the install still broke off, run `dstfarm install` again: the download resumes where it
stopped. When the error repeats, delete `.runtime\steamcmd` and try once more.

Antivirus software and SmartScreen sometimes block `steamcmd.exe` — check the quarantine.

## The server started but nobody can see it

* Make sure the client and the server are on the same version. Update with `dstfarm install`.
* On your own PC: look at the **LAN** tab.
* For other people over the internet: forward UDP `10999` and `27018` on the router
  (plus `11000` and `27019` for caves), and allow
  `dontstarve_dedicated_server_nullrenderer_x64.exe` through the Windows firewall.
* A password in `cluster.ini` hides the server from people who do not know it, but does not
  remove it from the list.

## No drops

Check in order:

1. The client is really in the game, not in the main menu. Time in the world is what counts.
2. `cluster.ini` says `offline_cluster = false` (dstfarm always writes it that way).
3. The weekly Klei cap is not reached. Uptime beyond the cap gives nothing.

More in [farming.md](farming.md).

## Which protections is the world actually running with

Settings are baked into the world when it is generated, so the config on disk and the running
world can disagree. `dstfarm status` reads what the server itself reported and shows a
`world protections` row: how many of the expected overrides the world really applied, and
which ones are missing. If they are missing, the world predates the settings —
`dstfarm reset-world --yes` regenerates it.

## The character keeps dying

World settings apply at **generation** time. If the world was already created with the old
parameters, some overrides will not take effect. Press `G` (or run `dstfarm init --force`),
then delete the `save` folder inside the shard — the world is recreated and the progress is lost.

## The log panel freezes

Before 0.1.6 the panel showed the server's captured stdout. Windows buffers it in blocks when
the output goes into a pipe, so the lines stopped at roughly
`[00:00:05]: [200] Account Communication Success` while the server was running fine.

Since 0.1.6 the panel reads the server's own log, which it writes immediately:
`Documents\Klei\DoNotStarveTogether\<cluster>\Master\server_log.txt`. The full output is still
mirrored into `.runtime\logs\master.log`.

If the panel is still silent, read both files directly — the server writes `server_log.txt`
regardless of us.

## The full-screen mode does not open

The message "the full-screen mode needs a real terminal" means input or output is redirected —
which happens when running from a pipe, a script or CI. Run `dstfarm` directly in a console
or use the commands.

## The world rolled back after a restart

The server saves on autosave and on a graceful `c_shutdown(true)`. If the process was killed
through Task Manager, everything since the last autosave is lost. Stop it with `S` or
`dstfarm stop`.

## Removing everything

Delete the `.runtime` folder (server, steamcmd, logs, statistics) and the cluster folder under
`Documents\Klei\DoNotStarveTogether`. Settings live in `config.json` next to the exe.
