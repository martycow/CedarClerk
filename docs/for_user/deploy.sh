#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
    cat <<'HELP'
Usage: bash docs/for_user/deploy.sh [--build-only]
Build and verify Cedar Clerk, then deploy to production (default).
--build-only runs the complete local gate and packages without contacting production.
Requires clean master, a matching version tag, and (for deployment) a local LIVE tag.
See deploy.md beside this script. No checks are skipped.
HELP
}
mode=deploy
case "${1:-}" in
    --help|-h) usage; exit 0 ;;
    --build-only) mode=build ;;
    '') ;;
    *) usage >&2; exit 2 ;;
esac
[[ $# -le 1 ]] || { usage >&2; exit 2; }
fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$root"
for tool in git dotnet npm node python3 tar tee find; do command -v "$tool" >/dev/null || fail "Missing tool: $tool"; done
[[ $(git branch --show-current) == master ]] || fail 'Deploy/build releases from master only.'
[[ -z $(git status --porcelain --untracked-files=all) ]] || fail 'Commit or preserve outstanding changes first; the release tree must be clean.'
sha=$(git rev-parse HEAD)
version=$(python3 -c 'import re; print(re.search(r"CurrentVersion\s*=\s*\"([0-9]+\.[0-9]+\.[0-9]+)\"",open("CedarClerk.Core/Consts.cs").read()).group(1))')
[[ $(git rev-parse "refs/tags/$version^{commit}" 2>/dev/null) == "$sha" ]] || fail "Tag $version must point at HEAD."
host=martycow@periwinkle.mooexe.dev
ssh_args=(-o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=3)
old_version=''
if [[ $mode == deploy ]]; then
    for tool in ssh rsync; do command -v "$tool" >/dev/null || fail "Missing tool: $tool"; done
    live_ref=$(git rev-parse refs/tags/LIVE)
    live=$(git rev-parse 'refs/tags/LIVE^{commit}') || fail 'Set LIVE to the verified production commit first.'
    old_version=$(git show "$live:CedarClerk.Core/Consts.cs" | python3 -c 'import sys,re; print(re.search(r"CurrentVersion\s*=\s*\"([0-9]+\.[0-9]+\.[0-9]+)\"",sys.stdin.read()).group(1))')
    [[ $old_version != "$version" ]] || fail 'Bump the release version before deploying another build.'
    ssh "${ssh_args[@]}" "$host" 'bash -s' <<'REMOTE'
set -euo pipefail
for tool in bash rsync tar sha256sum python3 sqlite3 curl flock nohup; do command -v "$tool" >/dev/null; done
"$HOME/.dotnet/dotnet" --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.'
"$HOME/.dotnet/dotnet" --list-runtimes | grep -q '^Microsoft.NETCore.App 10\.'
systemctl is-active --quiet cedarclerk
sudo -n -l /usr/bin/systemctl stop cedarclerk >/dev/null
sudo -n -l /usr/bin/systemctl start cedarclerk >/dev/null
test -f "$HOME/cedarclerk/data/cedar.db"
REMOTE
fi
artifact=$(mktemp -d "${TMPDIR:-/tmp}/cedar-release.XXXXXX")
printf 'Artifacts and logs: %s\n' "$artifact"
exec > >(tee "$artifact/build.log") 2>&1

dotnet test
(
    cd cedarclerk-web
    npm ci
    if [[ $(node -p 'Number(process.versions.node.split(".")[0])') -ge 26 ]]; then
        export NODE_OPTIONS="${NODE_OPTIONS:-} --no-experimental-webstorage"
    fi
    npm test -- --watch=false
    npm run check:icons
    npm run check:contrast
    npm run check:density
    npm run build
)
# Publish only tracked source: ignored local settings and old wwwroot builds must not travel.
mkdir "$artifact/source"
git archive HEAD | tar -xf - -C "$artifact/source"
dotnet publish "$artifact/source/CedarClerk.Server" -c Release -r linux-x64 \
    --self-contained false -p:UseAppHost=false -o "$artifact/app"
mkdir -p "$artifact/app/wwwroot"
cp -R cedarclerk-web/dist/cedarclerk-web/browser/. "$artifact/app/wwwroot/"
python3 - "$artifact/app" "$version" "$sha" <<'PY'
import json,pathlib,sys
p=pathlib.Path(sys.argv[1])
for name in ['CedarClerk.Server.dll','CedarClerk.Server.runtimeconfig.json','wwwroot/index.html']:
    assert (p/name).is_file(), name
(p/'wwwroot/deployment.json').write_text(json.dumps(dict(version=sys.argv[2],commit=sys.argv[3])))
PY
[[ $(git rev-parse HEAD) == "$sha" && -z $(git status --porcelain --untracked-files=all) ]] || fail 'Repository changed during build.'
COPYFILE_DISABLE=1 tar -czf "$artifact/release.tar.gz" -C "$artifact/app" .
digest=$(python3 - "$artifact/release.tar.gz" <<'PY'
import hashlib,sys
with open(sys.argv[1],'rb') as f: print(hashlib.file_digest(f,'sha256').hexdigest())
PY
)
count=$(find "$artifact/app" -type f | wc -l | tr -d ' ')
[[ $mode != build ]] || { printf 'Verified release: %s/release.tar.gz\n' "$artifact"; exit 0; }
run="$(date -u +%Y%m%dT%H%M%SZ)-${sha:0:12}-$$"
remote="cedarclerk/staging/$run"
ssh "${ssh_args[@]}" "$host" "mkdir -p '$remote'"
cp Scripts/server/deploy-release.sh "$artifact/deploy-release.sh"
uploaded=false
for attempt in 1 2 3; do
    if rsync --partial -e 'ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=3' \
        "$artifact/release.tar.gz" "$artifact/deploy-release.sh" "$host:$remote/"; then uploaded=true; break; fi
    printf 'Upload interrupted; retry %s/3\n' "$attempt"
done
[[ $uploaded == true ]] || fail "Upload failed; production was not stopped. Artifacts: $artifact"
ssh "${ssh_args[@]}" "$host" "nohup bash '$remote/deploy-release.sh' '$run' '$digest' '$count' '$version' '$sha' '$old_version' > '$remote/deploy.log' 2>&1 < /dev/null &"
printf 'Remote run: %s (continues if this terminal disconnects)\n' "$remote"
for ((i=0; i<300; i++)); do
    result=$(ssh "${ssh_args[@]}" "$host" "cat '$remote/result' 2>/dev/null || true") || fail "Connection lost; inspect $remote/deploy.log and result before retrying."
    if [[ -n $result ]]; then
        ssh "${ssh_args[@]}" "$host" "tail -n 60 '$remote/deploy.log'" || true
        [[ $result == success ]] || fail "Deployment result: $result. Inspect remote log; LIVE unchanged."
        [[ $(git rev-parse HEAD) == "$sha" && $(git rev-parse 'LIVE^{commit}') == "$live" ]] || fail 'Remote deploy succeeded, but local refs changed. Update LIVE manually after review.'
        git update-ref --stdin <<REFS
start
update refs/tags/LIVE-PREV $live
update refs/tags/LIVE $sha $live_ref
prepare
commit
REFS
        printf 'Deployed %s (%s). LIVE updated locally; tags were not pushed.\n' "$version" "$sha"
        exit 0
    fi
    sleep 2
done
fail "Timed out waiting. Worker may still be running: inspect $remote/result and deploy.log; do not launch another deployment yet."
