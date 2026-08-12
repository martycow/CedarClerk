# Production environment (do not break these assumptions)

**Production moved from the Raspberry Pi to a DigitalOcean droplet on 11.08.2026.** Everything below
was read off the running machine that day, not remembered. The Pi is no longer production; anything
in `CHANGELOG.md`, `docs/DECISIONS.md` or `TASKS.md` that talks about "the Pi" is history, and the
checklist the move followed is `docs/migration-to-digitalocean.md`.

- **Host**: DigitalOcean droplet `cedarclerk-periwinkle` (hostname `cedarclerk-ubuntu-s-1vcpu-2gb-fra1`),
  region **fra1**, 1 vCPU / 2 GB RAM / 48 GB disk (45 GB free), **Ubuntu 24.04.4 LTS, x86_64**.
  **No swap is configured** — 2 GB of RAM is all there is, and an out-of-memory kill takes the service
  with it rather than slowing it down.
- **Architecture note that stopped mattering**: the Pi was armhf (32-bit userland), so builds had to be
  `linux-arm`. The droplet is x86_64, and the deploy publishes **framework-dependent with no RID** —
  portable IL that neither machine cares about. That is what made the move a copy rather than a port.
  Target framework stays `net8.0` across all four projects.
- **.NET**: ASP.NET Core **runtime 8.0.29 only**, installed at `~/.dotnet` (no SDK — the server never
  builds anything). .NET 8 goes out of support in **November 2026**; that deadline moved with the app
  and is still real.
- **Paths**: app at `/home/martycow/cedarclerk/app`; data at `/home/martycow/cedarclerk/data`
  (SQLite `cedar.db` ~15 MB + `-wal`/`-shm`, `media/` ~937 MB, `thumbs/`, `dataprotection-keys/`,
  `import-tmp/` — 952 MB in total), injected via the systemd drop-in env var `CEDAR_DATA_DIR`.
  The deploy also keeps `app.prev` (the previous release, for `-Rollback`) and `staging/` (the uploaded
  tarball) next to them — see ADR-113.
- **Service**: systemd unit `cedarclerk.service`, `User=martycow`,
  `ExecStart=/home/martycow/.dotnet/dotnet CedarClerk.Server.dll`, `Restart=always`, `RestartSec=5`.
  Two drop-ins in `/etc/systemd/system/cedarclerk.service.d/`: `data.conf` (secrets, see `secrets.md`)
  and `override.conf`.
- **The unit is `enabled`** since 12.08.2026 (`T-143`, done by Marty — it needed his sudo password), so
  it comes back after a DigitalOcean maintenance reboot. It was `disabled` for the first day after the
  move, which would have left the site down until someone noticed.
- **sudo is scoped**: `NOPASSWD` covers exactly `/bin/systemctl start|stop|restart cedarclerk` and
  nothing else. Every other privileged command prompts for a password, which over a non-interactive
  `ssh` simply fails. Use `ssh -t` when a password prompt is genuinely wanted.
- **Logs read fine without sudo** — corrected 12.08.2026 against the running machine (ADR-118). The
  unit runs `User=martycow`, so its journal entries belong to that user and
  `ssh martycow@periwinkle.mooexe.dev "journalctl -q -u cedarclerk -n 50 --no-pager"` returns them. What
  `martycow` still cannot see is *other* units' output (not in `adm`/`systemd-journal`), and without
  `-q` journalctl prints a "you are not seeing messages from other users" hint that reads like a
  refusal but isn't. `sudo journalctl` also works over `ssh -t`, it is simply not required.
  **Always bound the query**: the service logs every EF statement, which is ~1.5 million lines a day.
- **Networking**: public URL `https://cedarclerk.mooexe.dev` via **Cloudflare Tunnel**
  (`cloudflared.service`, running as root from `/etc/cloudflared/config.yml`) → `http://127.0.0.1:8080`.
  TLS terminates at Cloudflare. **Kestrel listens on loopback only** — the droplet exposes nothing but
  SSH (port 22) to the internet, and that is deliberate: the tunnel is the only way in. Port 8080 is
  fixed by the tunnel config. Blog (`blog.mooexe.dev`) is host-routed inside the same Kestrel process
  (`Program.cs` `MapWhen` on `Host.Host`).
- **SSH**: key-based to `martycow@periwinkle.mooexe.dev` (165.227.155.148). **The name changed on
  12.08.2026** — it was `deploy.mooexe.dev`, whose DNS record is gone, so anything still saying
  `deploy.` fails at resolution, not at login. `CHANGELOG.md` and `docs/DECISIONS.md` still carry the
  old name because they record what was true then; everywhere else was updated. The record must stay
  **DNS only** in Cloudflare — a proxied record answers with Cloudflare's IPs, which do not take SSH.
  `raspberrypi.local` is dead as a deploy target; `cedar deploy` reads the host from
  `%APPDATA%\cedar\config.json` (`cedar config`) and takes `--host` to override it for one run.
- **Timezone is UTC** (the Pi ran local time). Anything that reads the wall clock on the server — cron,
  log timestamps, a scheduled post's idea of "tonight" — now means UTC.

## Backups — read this before assuming data is safe

There are now **two** backups, and they protect different things. Any sentence about backups has to say
which one it means.

**Nightly database copy** — `T-071`, closed 12.08.2026 by Marty on the server, not by this repo.
`~/bin/backup.sh` runs from his crontab at **03:30 UTC** (the droplet is UTC — that is 03:30 UTC, not
local): `sqlite3 .backup` into `/home/martycow/cedarclerk/data/backups/cedar-<YYYY-MM-DD>.db.gz`,
gzipped, `-mtime +13 -delete` keeping fourteen days, then a ping to healthchecks.io so that a *silent*
failure raises an alert instead of nothing. First copy verified by hand the same day: 15 MB database →
2.8 MB gz. The ping URL lives in the script on the server and is not in this repo — see `secrets.md`.

Two things about it that are easy to get wrong:

- **The destination path is shared with the CLI.** `cedar status` and `cedar backup verify` look in
  `{RemoteDataDir}/backups` (`CedarClerk.Cli/Server/ServerProbe.cs`, `Commands/BackupCommand.cs`).
  Moving the script's `DEST` without moving the CLI's path makes the status line report "no local copy"
  over a directory full of copies — which is exactly what happened on the first attempt, when the
  script wrote to `~/backups`.
- **The cron line's log redirect needs the directory to already exist.** `>> …/backups/backup.log`
  is opened by the shell *before* the script runs, so the script's own `mkdir -p` is too late: with no
  directory, cron fails to open the log and never starts the script at all. The first scheduled run
  would have died this way, silently, if the healthcheck had not been there to notice.

**Weekly whole-machine image** — DigitalOcean's paid droplet backup (enabled 11.08.2026), retained four
weeks, taken by the platform.

What the nightly copy does *not* fix, and what is therefore still open:

1. **It is on the same disk as the database it copies.** A lost droplet takes both. An off-box target —
   DO Spaces or anything `rclone` reaches — is the remaining work, and it is not done.
2. **It covers the database only.** `media/` is ~937 MB and is in the weekly image alone.
3. **The weekly image lives in the same account as the droplet.** An accidental destroy, a billing lapse
   or a compromised login takes it too. The Pi's microSD had that property; it was lost in the move.

Restoring the database alone is now possible (`gunzip` a dated copy over `cedar.db` with the service
stopped), which it was not between 11.08 and 12.08.2026. Restoring anything else still means restoring
the whole machine to the moment of the weekly image.

## What the move retired

- `T-070` (Pi OS Bullseye → 64-bit, planned for ~August 2026 to get arm64 and a newer .NET) is **moot**:
  the new host is a supported 64-bit Ubuntu already. Closed on the backlog for that reason, not done.
- The microSD backup target, `/mnt/backup`, and `~/bin/cedar-backup.sh` — see above.
- The coordination constraint that the same machine ran Marty's Freenove electronics projects.

See `docs/ARCHITECTURE.md` for the deploy pipeline that targets this environment, ADR-113 for why that
pipeline is shaped the way it is, and `docs/integrations-setup.md` for provider-key setup on top of it.
