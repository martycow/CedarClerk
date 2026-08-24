# Repo hygiene — private today, may go public

The repo is private right now, but treat that as a schedule, not a shield: write as if a stranger
could open the file next commit, and there's nothing to unwind later.

**Secrets** — covered fully by `.claude/rules/secrets.md`; don't repeat that logic here, just the
rule of thumb: if it's a key, token, password, or a real ping/webhook URL, it goes in the systemd
drop-in or an untracked local file, never in a tracked one.

**Local paths and machine identity** — covered by `docs/DOCS-FLOW.md` §File placement: no drive letters
or absolute local paths in docs (`D:\Moo.exe\...`); portable forms (`%APPDATA%\…`) and the droplet's
own paths (`/home/martycow/…`) are fine, since the droplet's layout is already documented elsewhere.

**Personal mentions in context files** — `AGENTS.md`, `CLAUDE.md`, `.claude/rules/*`, `.claude/skills/*`
load into every session and get read by whoever opens the repo, including anyone the repo is ever
shared with. Identify the maintainer once where it's load-bearing (skill level, infra experience —
this genuinely shapes how an assistant should explain things), then use role language instead of
repeating a name. This does **not** apply to dated decision records — ADRs, `docs/tasks/CHANGELOG.md`,
`docs/tasks/BACKLOG.md`, archived docs — where "the maintainer decided X on Y" is the content itself,
not personal-info noise; don't scrub those.

**Third-party PII** — no real names, emails, or identifying details about anyone other than the
maintainer, in code comments, docs, or test fixtures. Use placeholder data.

## Before the repo actually flips to public

Run this once, right before visibility changes — not before every commit:

1. `git log --all -p | grep -iE 'api[_-]?key|secret|password|BEGIN (RSA|OPENSSH|PGP)'` (or an
   equivalent history scan) — a clean `git grep` on the current tree doesn't prove history is clean.
2. Confirm `.gitignore` still excludes `appsettings.Development.json`, `*.db`/`*.db-wal`/`*.db-shm`,
   `dataprotection-keys/`, and anything else listed under `# === Secrets and local data===`.
3. Re-grep the context files above for personal mentions that crept back in since the last pass.
4. Read `docs/for_user/integrations-setup.md` once end to end — it documents *how* to get provider
   keys, deliberately with no real values in it; confirm that's still true.
