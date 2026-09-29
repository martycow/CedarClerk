# Production environment (do not break these assumptions)

DigitalOcean droplet since 11.08.2026. Anything in CHANGELOG/ADRs about "the Pi" is history.

## Host

| | |
|---|---|
| Droplet | `cedarclerk-periwinkle`, fra1, 1 vCPU / 2 GB RAM / 48 GB, Ubuntu 24.04 x86_64. **No swap** — OOM kills the service |
| SSH | `martycow@periwinkle.mooexe.dev` (165.227.155.148), key-based. DNS record must stay **DNS only** in Cloudflare. |
| .NET | ASP.NET Core runtime in `~/.dotnet`, no SDK. The app targets **net10.0** (ADR-305); the droplet had 8.0.29 — install the 10 runtime (`dotnet-install.sh --runtime aspnetcore --channel 10.0 --install-dir ~/.dotnet`, no sudo) **before the next deploy** |
| Build | Framework-dependent, no RID — portable IL, builds on any OS |
| Timezone | **UTC** — cron, logs, "tonight" in scheduled posts |

## Paths and service

- App `/home/martycow/cedarclerk/app` (+ `app.prev` for rollback, `staging/` for the upload — ADR-113).
- Data `/home/martycow/cedarclerk/data` via `CEDAR_DATA_DIR`: `cedar.db` (+`-wal`/`-shm`), `media/`, `thumbs/`, `dataprotection-keys/`, `import-tmp/`, `backups/`.
- `cedarclerk.service`: `User=martycow`, `Restart=always`, **enabled** (survives reboots). Drop-ins: `data.conf` (secrets, `secrets.md`), `override.conf`.
- **sudo NOPASSWD covers only** `systemctl start|stop|restart cedarclerk`. Anything else prompts → fails over plain `ssh`; use `ssh -t`.
- Logs need no sudo: `journalctl -q -u cedarclerk -n 50 --no-pager`. Keep `-q` (otherwise a misleading "other users" hint). **Always bound the query.** EF commands log at Warning by default (ADR-302).

## Network

- `https://cedarclerk.mooexe.dev` → **Cloudflare Tunnel** (`cloudflared.service`, root, `/etc/cloudflared/config.yml`) → `127.0.0.1:8080`. Kestrel is loopback-only; only SSH (22) is exposed. Port 8080 is fixed by the tunnel.
- Blog is host-routed in the same process (`Program.cs` `MapWhen`).
- **Cloudflare caches `/media/*` by extension, status included.** A response without `Cache-Control` sticks at the edge for hours (ADR-300). Per-reader responses must send `private, no-store`. Check: `curl -sD - -o /dev/null <url> | grep -iE 'cf-cache-status|cache-control|age'`.

## Full disk

`cannot rollback - no transaction is active` usually hides `SQLITE_FULL`. Diagnose with `df -h /`, `journalctl -q --disk-usage`, `ls -lhS /var/log`. A green `/api/health` doesn't prove writes work. **Never delete** db/WAL/media/backups/`app.prev` to free space; truncating system logs needs explicit approval.

## Backups — say which one you mean

| Backup | What | Where | Retention |
|---|---|---|---|
| Nightly DB | `~/bin/backup.sh`, cron **03:30 UTC**: `sqlite3 .backup` → `cedar-YYYY-MM-DD.db.gz` | `data/backups/` | 14 days |
| Off-box | same script: `rclone copy` DB, `rclone sync media/ --backup-dir media-removed/<date>` | Cloudflare R2 (a second account, deliberately not DO Spaces) | DB 30 days |
| Machine image | DigitalOcean weekly backup | DO (same account) | 4 weeks |

Load-bearing details:
- Source is `Scripts/server/backup.sh`; the running copy is installed **by hand** (no deploy touches it; `backup.sh.prev` holds the previous one).
- The cron log redirect needs `data/backups/` to already exist — otherwise cron never starts the script.
- Count copies by `cedar-*.db.gz`, not `*`: `backup.log` lives in the same directory.
- `rclone` is called by full path (`~/bin` is not on cron's PATH).
- `--backup-dir`, not bare sync: a deletion here must not become a deletion there.
- Two separate healthchecks (local / off-box). `~/.config/cedar-backup.env` holds `R2_REMOTE`, `HC_DB_URL`, `HC_OFFSITE_URL` (secrets). Missing `R2_REMOTE` → off-box half skips silently.

Worst case: a lost droplet loses ≤1 day of DB + media changed since 03:30 UTC. Restore DB: stop the service, `gunzip` a dated copy over `cedar.db`. Media: `rclone copy` back from R2. Setup: `docs/for_user/integrations-setup.md` §5.
