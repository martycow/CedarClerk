#!/usr/bin/env bash
# The droplet's nightly backup: a local database copy (T-071) and an off-box copy of the database
# and media (T-147). Runs from Marty's crontab at 03:30 UTC.
#
# This file is the source of truth; the copy that runs lives at ~/bin/backup.sh on the droplet and is
# installed by hand — there is no deploy path for it, because the deploy replaces the app
# directory and nothing else. When this changes, copy it across (the checklist is in
# docs/integrations-setup.md).
#
# Every URL and key is read from ~/.config/cedar-backup.env, which is not in the repo: a healthchecks
# ping URL is a secret in the sense that matters — anyone holding it can silence the alarm.
set -euo pipefail

DATA=/home/martycow/cedarclerk/data
DB="$DATA/cedar.db"
DEST="$DATA/backups"
STAMP=$(date -u +%F)

ENV_FILE="$HOME/.config/cedar-backup.env"
[ -f "$ENV_FILE" ] && . "$ENV_FILE"

ping() {
    [ -n "${1:-}" ] || return 0
    curl -fsS -m 10 --retry 3 "$1" > /dev/null || true
}

# --- local copy -------------------------------------------------------------------------------
# sqlite3's own .backup, not cp: the database is live, and a file copy taken mid-write is a torn
# page rather than a backup.
mkdir -p "$DEST"
sqlite3 "$DB" ".backup '$DEST/cedar-$STAMP.db'"
gzip -f "$DEST/cedar-$STAMP.db"
find "$DEST" -name 'cedar-*.db.gz' -mtime +13 -delete

ping "${HC_DB_URL:-}"

# --- off-box copy (T-147) ---------------------------------------------------------------------
# Skipped silently when unconfigured, so the local copy above never depends on it.
[ -n "${R2_REMOTE:-}" ] || exit 0

# By full path: rclone lives in ~/bin, which cron's PATH does not include — `command -v rclone`
# answers yes in a login shell and no at 03:30.
RCLONE="${RCLONE:-$HOME/bin/rclone}"
[ -x "$RCLONE" ] || { echo "rclone missing at $RCLONE; off-box copy skipped"; exit 0; }

# media is mirrored rather than accumulated, but a deletion here must not become a deletion there:
# --backup-dir moves what sync would remove into a dated folder instead. Ransomware and a fat-finger
# rm look identical to sync, and this is the difference between a mirror and a backup.
if "$RCLONE" copy "$DEST/cedar-$STAMP.db.gz" "$R2_REMOTE/db/" \
    && "$RCLONE" delete "$R2_REMOTE/db/" --min-age 30d \
    && "$RCLONE" sync "$DATA/media" "$R2_REMOTE/media" --backup-dir "$R2_REMOTE/media-removed/$STAMP"
then
    ping "${HC_OFFSITE_URL:-}"
else
    # A separate check from the database one: "the copy on the droplet failed" and "the copy off the
    # droplet failed" are different emergencies and must not silence each other.
    ping "${HC_OFFSITE_URL:+${HC_OFFSITE_URL}/fail}"
    exit 1
fi
