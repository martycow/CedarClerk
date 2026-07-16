# Production environment (do not break these assumptions)

- **Host**: Raspberry Pi 4 8GB, Raspbian 11 Bullseye. Kernel is 64-bit but **userland is armhf (32-bit)** — any native binary (runtime, published app) must target armhf/linux-arm, not arm64.
- **.NET**: ASP.NET Core **runtime 8.0.x only** on the Pi (no SDK — the Pi never builds anything), installed at `~/.dotnet`. Target framework stays `net8.0` across all four projects.
- **Paths**: app at `/home/martycow/cedarclerk/app`; data at `/home/martycow/cedarclerk/data` (SQLite `cedar.db` + `media/`), injected via systemd drop-in env var `CEDAR_DATA_DIR`.
- **Service**: systemd unit `cedarclerk.service`. Secrets in a drop-in at `/etc/systemd/system/cedarclerk.service.d/data.conf` — see `secrets.md`.
- **Networking**: public URL `https://cedarclerk.mooexe.dev` via Cloudflare Tunnel (`cedarpi`) → `http://localhost:8080`. TLS terminates at Cloudflare; Kestrel listens plain HTTP on :8080 — this port is fixed because the tunnel config depends on it. Blog (`blog.mooexe.dev`) is host-routed inside the same Kestrel process (`Program.cs` `MapWhen` on `Host.Host`).
- **Backups**: cron 3:30 AM runs `~/bin/cedar-backup.sh` (`sqlite3 .backup` + rsync to a microSD at `/mnt/backup`, 14 daily copies retained). Anything valuable must live under `~/cedarclerk/data` to be covered. Cloud backup duplication (rclone) is acknowledged tech debt, not yet done.
- **SSH**: key-based (ed25519) to `martycow@raspberrypi.local`; passwordless sudo is scoped to `systemctl start/stop/restart cedarclerk` only.
- **Known future migration**: Bullseye → a fresh 64-bit Raspberry Pi OS (gets arm64 runtime + lets .NET move past its Nov-2026 EOL) is planned for ~August 2026, coordinated with Marty since the same Pi also runs Freenove electronics projects — not to be done casually mid-session.

See `docs/ARCHITECTURE.md` for the deploy pipeline that targets this environment, and `docs/integrations-setup.md` for provider-key setup on top of it.
