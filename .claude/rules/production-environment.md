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
- **⚠ The unit is `disabled`, so it does NOT come back after a reboot.** DigitalOcean reboots droplets
  for host maintenance, and the site would simply stay down until someone noticed. The one-time fix is
  `sudo systemctl enable cedarclerk` — it needs the sudo password, so Marty has to run it himself.
- **sudo is scoped**: `NOPASSWD` covers exactly `/bin/systemctl start|stop|restart cedarclerk` and
  nothing else. Every other privileged command prompts for a password, which over a non-interactive
  `ssh` simply fails. Use `ssh -t` when a password prompt is genuinely wanted.
- **Logs read fine without sudo** — corrected 12.08.2026 against the running machine (ADR-118). The
  unit runs `User=martycow`, so its journal entries belong to that user and
  `ssh martycow@deploy.mooexe.dev "journalctl -q -u cedarclerk -n 50 --no-pager"` returns them. What
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
- **SSH**: key-based to `martycow@deploy.mooexe.dev` (165.227.155.148). `raspberrypi.local` is dead as a
  deploy target; `Scripts/deploy.ps1` defaults to the droplet and takes `-CloudHost` to override.
- **Timezone is UTC** (the Pi ran local time). Anything that reads the wall clock on the server — cron,
  log timestamps, a scheduled post's idea of "tonight" — now means UTC.

## Backups — read this before assuming data is safe

**What exists**: DigitalOcean's paid droplet backup, **weekly** (Marty enabled it 11.08.2026). That is a
whole-machine image, taken by the platform, retained for four weeks.

**What no longer exists**: the Pi's daily `sqlite3 .backup` + `rsync` to a microSD with 14 dated copies
(`~/bin/cedar-backup.sh`, cron 3:30 AM). It did not move. **There is no crontab and no `~/bin` on the
droplet** — verified 11.08.2026.

Three consequences that are not obvious, and none of them is a reason to panic — just to be honest:

1. **The worst case went from losing a day to losing a week.** A weekly image is the only copy, so a
   failure the day before the snapshot costs six days of posts, media and accounts.
2. **Restoring is all-or-nothing.** A droplet image restores the whole machine to that moment — app,
   database, media, config. There is no "put yesterday's database back and keep today's code".
3. **The copy lives in the same account as the thing it protects.** An accidental destroy, a billing
   lapse or a compromised login takes the backup with the droplet. The Pi's copy was on a card in
   another device; that property was lost in the move, not gained.

`sqlite3` is installed on the droplet, so restoring the daily copy is a small cron job (backlog `T-071`,
raised to High on 11.08.2026), and an off-box target — DO Spaces or anything `rclone` reaches — is what
closes point 3. Neither is done. Until they are, "the data is backed up" is true only in the weekly,
same-account, whole-machine sense, and any sentence about backups should say which one it means.

## What the move retired

- `T-070` (Pi OS Bullseye → 64-bit, planned for ~August 2026 to get arm64 and a newer .NET) is **moot**:
  the new host is a supported 64-bit Ubuntu already. Closed on the backlog for that reason, not done.
- The microSD backup target, `/mnt/backup`, and `~/bin/cedar-backup.sh` — see above.
- The coordination constraint that the same machine ran Marty's Freenove electronics projects.

See `docs/ARCHITECTURE.md` for the deploy pipeline that targets this environment, ADR-113 for why that
pipeline is shaped the way it is, and `docs/integrations-setup.md` for provider-key setup on top of it.
