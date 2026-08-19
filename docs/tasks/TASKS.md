---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: what is in progress now, decisions waiting on Marty, the live-verification checklist
guard: none
---

# Tasks

Short horizon: what is in progress, what waits on Marty, and the live-verification checklist.
Phase status: `docs/tasks/ROADMAP.md`; open tasks: the board in `docs/tasks/BACKLOG.md`; history: `CHANGELOG.md`.

## Now

- [ ] Growth anchors — T-159 (project showcase), T-160 (onboarding templates), T-154 (landing: EN, devlog-first, waitlist — prerequisite of any acquisition); T-158 (devlog assembler, ADR-132) and T-161 (Discord webhook, ADR-131) are done, hands-on checks below #growth P1
- [ ] Cheap safety — T-175 (EXIF/GPS stripping) is done, ADR-130; hands-on check below, old files on the droplet are T-202. T-174 (OG tags) is done, hands-on check below #security P1
- [ ] Registration blockers — T-172 remainder: media into R2 (quotas cut, ADR-129; first backup restore tested 18.08 — monthly cadence per `docs/product/BUSINESS.md` §5) #infra P1

## Waiting on Marty

- [ ] Show Terms/Privacy to a lawyer — before public registration opens; the texts are filled (13.08), no lawyer has seen them — a gate in `docs/product/BUSINESS.md` §2 #legal P1

## Live verification

Code is written and covered by tests, but never checked by hand or on a device:

- [ ] cedar restart — the CLI's only destructive command, never run (drops the blog together with the app for seconds; run when it costs nothing) #cli P2
- [ ] cedar build --installer and cedar deploy --desktop — not run end-to-end since the pipeline moved to C# (ADR-119); installer build and Cloudflare distribution were verified only on the old pipeline (0.10.7–0.10.9) #desktop P2
- [ ] Desktop installer on a clean machine — T-121: builds and is verified on artifacts, never executed #desktop P2
- [ ] Desktop after ADR-117 — the list in `docs/tech/DESKTOP.md` §Risks: cloud `/projects` without the price table, a scan of a real Unity/Blender folder, the orientation of a real `.blend` preview, cancelling mid-preview-pass and resuming, zero processes after closing the window, `curl` to the agent without a token → 401 #desktop P2
- [ ] Bluesky post with an image — Marty only: `uploadBlob` against real bsky.social (list building and text are unit-tested, ADR-109) #publishing P2
- [ ] flush-on-hide on a real iPhone — remainder of T-018 (the 29.07 incident was on iOS; Safari kills a tab differently than desktop) #mobile P2
- [ ] Save guards + restore from history — remainder of T-060: see the wipe-save refusal, "restore stored" and a version rollback by hand; confirm a heavy honest edit does not false-positive #editor P2
- [ ] OG previews (T-174) — via @WebpageBot or opengraph.xyz with `?v=2` on the URL (Telegram caches the old scrape): public post — full card with image and description; semi-public — title + fallback only; private and the gate — nothing. `/og-default.png` serves from the blog host #blog P1
- [ ] EXIF stripping (T-175) — upload a phone photo with GPS, download it back from `/media/`, confirm the coordinates are gone (any EXIF viewer); the Telegram derivative too #media #security P1
- [ ] Paste/drop into the editor (T-177) — a screenshot from the real clipboard (Win+Shift+S → Ctrl+V) and a multi-file drag&drop; the progress panel appears, files land at the drop point #editor P1
- [ ] Media library (T-177) — insert from the library → publish → media renders on the blog and Telegram; deleting a used asset → 409 with post titles; a free one — the file AND the `_tg` derivative disappear from disk #media P1
- [ ] The `[[` trigger on a Russian layout (T-181) — how the suggester behaves when `[` needs the Latin layout; if unreachable from Russian, an alternative is needed (a button/command) #editor P1
- [ ] A series of 3 posts (T-178) — the `/series/{slug}` page, prev/next on posts, "Part N of M"; a private unlisted post does not shift a stranger's numbering #blog P1
- [ ] The tree (T-181) — moving via the "Move under…" menu, up/down among siblings, editor breadcrumbs open the document, the backlinks chip counts correctly after saving with a `[[` link #editor P1
- [ ] Discord webhook (T-161) — connect a real channel webhook in Settings → Integrations, publish a post with an image: the announcement lands, the blog link unfurls into a card with the OG image, no @everyone ping even if the text contains one #integrations P1
- [ ] Sprint → devlog (T-158) — on a real sprint with done/open tasks and a released build: the "Devlog draft" button on the planner card assembles the three sections in the document's language and opens the editor #editor P1
- [ ] Old unverified small things — once each, no rush: incremental re-translation preserving manual edits; DeepL's uk/be/ka refusal with a clear message; the glossary tooltip on a live published post; per-language cross-links; tag rename/delete; audit paging past page one; Russian wording screen by screen #misc P3

## Notes

- Production: 0.12.0 on the droplet, `LIVE` = `0.12.0`; master is ahead by the 0.12.1 session (OG tags, series, media library, tree + wiki-links, metrics dictionary) — deploying is Marty's call. `LIVE`/`LIVE-PREV` never go to origin (the local-tag rule in CLAUDE.md).
- The `indiedev_module` branch is merged and deleted; the module lives in master behind `Cedar:Modules:IndieDev` (reversibility: ADR-101). `dev` is a stale pointer behind master with no commits of its own.
