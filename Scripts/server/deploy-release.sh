#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
[[ $# == 6 ]] || exit 2
run=$1 digest=$2 count=$3 version=$4 commit=$5 old_version=$6
[[ $run =~ ^[0-9]{8}T[0-9]{6}Z-[0-9a-f]{12}-[0-9]+$ ]] || exit 2
[[ $digest =~ ^[0-9a-f]{64}$ && $commit =~ ^[0-9a-f]{40}$ && $count =~ ^[0-9]+$ ]] || exit 2
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && $old_version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || exit 2
base="$HOME/cedarclerk"
stage="$base/staging/$run"
[[ -d $stage ]] || exit 2
stopped=false
moved=false
installed=false
complete=false
health() {
    local url=$1 expected=$2
    curl --location --fail --silent --show-error --max-time 5 "$url" | python3 -c \
        'import json,sys; actual=json.load(sys.stdin).get("version"); expected=sys.argv[1]; sys.exit(0 if actual == expected else f"Version mismatch: expected {expected}, server reports {actual}. Verify the local LIVE tag before deploying.")' "$expected"
}
wait_local() {
    local expected=$1 deadline=$((SECONDS + 120))
    while (( SECONDS < deadline )); do
        if systemctl is-active --quiet cedarclerk && health http://127.0.0.1:8080/api/health "$expected" 2>/dev/null; then return 0; fi
        sleep 2
    done
    return 1
}
finish() {
    local rc=$?
    trap - EXIT HUP INT TERM
    set +e
    if [[ $complete == true ]]; then
        printf 'success\n' > "$stage/result"
        exit 0
    fi
    printf 'Deployment failed (exit %s).\n' "$rc"
    if [[ $stopped == true ]]; then
        # Startup migrations may have changed data; never silently overwrite it with a backup.
        if sudo -n /usr/bin/systemctl stop cedarclerk; then
            if [[ $installed == true ]]; then
                mv "$base/app" "$stage/app.failed" || { printf 'rollback-failed\n' > "$stage/result"; exit 1; }
            fi
            if [[ $moved == true ]]; then
                mv "$base/app.prev" "$base/app" || { printf 'rollback-failed\n' > "$stage/result"; exit 1; }
            fi
            if sudo -n /usr/bin/systemctl start cedarclerk && wait_local "$old_version"; then
                printf 'Previous application restored and local health verified. Database not restored.\n'
                printf 'rolled-back\n' > "$stage/result"
            else
                printf 'Rollback health failed. Inspect service logs and the pre-deploy database backup.\n'
                printf 'rollback-failed\n' > "$stage/result"
            fi
        else
            printf 'Could not stop service for rollback; directories preserved.\n'
            printf 'rollback-failed\n' > "$stage/result"
        fi
    else
        printf 'failed-before-stop\n' > "$stage/result"
    fi
    exit 1
}
trap finish EXIT
trap 'exit 130' HUP INT TERM
exec 9> "$base/deploy.lock"
flock -n 9 || { printf 'Another deployment is running.\n'; exit 1; }
[[ ! -e $stage/app.new && ! -e $stage/result ]] || { printf 'Run already started; use a new run directory.\n'; exit 1; }
printf '%s  %s\n' "$digest" "$stage/release.tar.gz" | sha256sum --check --status
"$HOME/.dotnet/dotnet" --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.'
"$HOME/.dotnet/dotnet" --list-runtimes | grep -q '^Microsoft.NETCore.App 10\.'
[[ -d $base/app && ! -L $base/app && ! -L $base/app.prev ]]
systemctl is-active --quiet cedarclerk
health http://127.0.0.1:8080/api/health "$old_version"
python3 - "$stage" "$count" "$version" "$commit" "$base" <<'PY'
import json,pathlib,shutil,sys,tarfile
stage=pathlib.Path(sys.argv[1]); base=pathlib.Path(sys.argv[5])
with tarfile.open(stage/'release.tar.gz') as archive:
    members=archive.getmembers()
    for m in members:
        p=pathlib.PurePosixPath(m.name)
        assert not p.is_absolute() and '..' not in p.parts, m.name
        assert m.isfile() or m.isdir(), m.name
    assert sum(m.isfile() for m in members)==int(sys.argv[2]), 'File count mismatch'
    need=sum(m.size for m in members)+2*(base/'data/cedar.db').stat().st_size+128*1024**2
    assert shutil.disk_usage(base).free > need, 'Insufficient space for release and database backup'
    archive.extractall(stage/'app.new',filter='data')
p=stage/'app.new'
for name in ['CedarClerk.Server.dll','CedarClerk.Server.runtimeconfig.json','wwwroot/index.html']:
    assert (p/name).is_file(), name
assert json.loads((p/'wwwroot/deployment.json').read_text())==dict(version=sys.argv[3],commit=sys.argv[4])
config=json.loads((p/'CedarClerk.Server.runtimeconfig.json').read_text())
assert config['runtimeOptions']['tfm']=='net10.0'
PY
printf 'Archive and current release verified. Stopping for backup and directory swap.\n'
# Set before stop so an interrupted stop still attempts to restore service availability.
stopped=true
sudo -n /usr/bin/systemctl stop cedarclerk
! systemctl is-active --quiet cedarclerk
mkdir -p "$base/data/backups"
python3 - "$base/data/cedar.db" "$base/data/backups/predeploy-$run.db" <<'PY'
import sqlite3,sys,pathlib
assert not pathlib.Path(sys.argv[2]).exists()
with sqlite3.connect('file:'+sys.argv[1]+'?mode=ro',uri=True) as source:
    with sqlite3.connect(sys.argv[2]) as target:
        source.backup(target)
        assert target.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
PY
if [[ -e $base/app.prev ]]; then mv "$base/app.prev" "$base/app.prev.$run"; fi
mv "$base/app" "$base/app.prev"
moved=true
mv "$stage/app.new" "$base/app"
installed=true
sudo -n /usr/bin/systemctl start cedarclerk
wait_local "$version"
public=https://cedarclerk.mooexe.dev
deadline=$((SECONDS + 120))
verified=false
while (( SECONDS < deadline )); do
    if health "$public/api/health?release=$commit" "$version" 2>/dev/null && \
       curl --location --fail --silent --show-error --max-time 5 "$public/deployment.json?release=$commit" | \
       python3 -c 'import json,sys; assert json.load(sys.stdin)==dict(version=sys.argv[1],commit=sys.argv[2])' "$version" "$commit" 2>/dev/null; then
        verified=true; break
    fi
    sleep 2
done
[[ $verified == true ]]
curl --location --fail --silent --show-error --max-time 10 "$public/login" -o "$stage/login.html"
grep -qi '<app-root' "$stage/login.html"
printf 'Local/public version, release marker, service and login shell verified: %s (%s).\n' "$version" "$commit"
complete=true
