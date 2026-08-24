---
name: task-format
description: Reformat any task list into the canonical Cedar Clerk task format so the Tasks board parses every item correctly. Load when asked to reformat/normalize/migrate TASKS.md, SPRINT.md, BACKLOG.md or ROADMAP.md, or when tasks are not showing up (or showing wrong) on the Cedar Clerk board. Invoke as /task-format [file|all].
---

# task-format — canonical Cedar Clerk task format

The board here is the **Cowtext app watching this repo** (its `tasks.rs` parser —
borrowed skill, the parser lives in the Cowtext project). It reads FOUR convention
files, searched in this directory order (first hit per name wins):
**project root → docs/ → docs/tasks/** — `TASKS.md`, `SPRINT.md`,
`BACKLOG.md`, `ROADMAP.md`. Anything else is invisible to the board.

In Cedar Clerk two of the four convention files exist, both in **`docs/tasks/`**:
`TASKS.md` and `BACKLOG.md` — no `SPRINT.md`, and `ROADMAP.md` was retired
24.08.2026 (it had drifted into a near-duplicate of `docs/tasks/CHANGELOG.md`; its
history is archived at `docs/archive/roadmap-phases-0-13.md`). Never recreate a
convention file in the root: the root copy would shadow the real file for the
board. Cedar Clerk's own board conventions stay binding when reformatting: `T-xxx`
ids are stable and never reused, BACKLOG holds open items only, done rows are
deleted (history lives in `docs/tasks/CHANGELOG.md`).

## The line Cedar Clerk actually writes

`BACKLOG.md` and `TASKS.md` already agree with each other, and it's narrower than
what the parser below tolerates — don't "upgrade" a line to `[>]`/`[?]`/`@agent`
just because the parser accepts them; that would just diverge the two docs again:

```
- [ ] T-xxx Name — description #tag1 #tag2 P1
```

Only `[ ]` (open) and `[x]` (done) are used. The `T-xxx`/`Q-xx` id is folded into
the start of Name, not a separate token. No `@agent` token is in use here.

## The canonical checklist line (what the Cowtext parser accepts, generically)

```
- [m] Name — description #tag1 #tag2 @agent P1
```

- **Marker `[m]` = status**: `[ ]` New · `[>]` In production · `[?]` In testing · `[x]` Done.
- **Name** — everything up to the first ` — ` (em-dash), ` - ` or `. ` boundary; keep it short.
- **description** — after the boundary, one line.
- **#tag** — any `#word` token becomes a tag.
- **@agent** — one `@name` token assigns the agent (file-name stem or display name of a
  `.claude/agents/*.md`; no token = the task belongs to **Producer**).
- **P0–P3** — a bare priority token (P0 danger, P1 amber on the board).
- Indentation is preserved; any line not matching a task shape is left alone.

## Sprint grouping (TASKS.md only)

The board groups TASKS.md into swimlanes by the nearest preceding `##` heading:

```
## Sprint 12 — polish
- [>] Fix funnel ports — hover state #ui P1
- [ ] Board drag #board @producer P2
```

No heading before a task → the "No sprint" lane. `#`-level-1 headings are ignored.

## Tables (also parsed, second-class)

Pipe tables parse when the header row has a name-like column. Recognized headers
(case-insensitive, first match): `name|task|title` · `tags` · `priority|prio` ·
`description|desc|details` · `phase` · `agent|assignee|owner` · `status|state`.
Status cells map: new/todo→New; in progress|in production|wip|doing→In production;
testing|in testing|review→In testing; done|closed→Done; anything else→New.
Prefer converting tables to checklist lines when reformatting UNLESS the table
carries a `phase` column (phase is table-only) or the file is ROADMAP.md history.

## ROADMAP time marks

The board's ROADMAP list shows a time chip from the FIRST token in the line that
matches an ISO date (`2026-08-18`), a quarter (`Q1`–`Q4`) or `Phase N`. When
reformatting ROADMAP, keep exactly one such token per line, early in the line.

## Reformat procedure

1. Locate the four files across the three convention dirs (`Glob` for
   `{,docs/,docs/tasks/}{TASKS,SPRINT,BACKLOG,ROADMAP}.md`). If a task list
   lives in a non-convention file, MOVE its items into the right convention
   file (ask which one when unclear: actionable→TASKS, scoped→SPRINT,
   someday→BACKLOG, plan/history→ROADMAP).
2. For every item, rewrite to the canonical checklist line: derive the status
   marker (done/✅→`[x]`, in progress→`[>]`, testing/review→`[?]`, else `[ ]`),
   pull tags into `#tag` tokens, assignee into ONE `@agent` token, priority into
   a `P0`–`P3` token, and split name — description on the first natural boundary.
3. TASKS.md: preserve/introduce `##` sprint headings; move Done items to the
   bottom of their lane rather than deleting them.
4. Preserve every non-task line (prose, headings, links) byte-for-byte.
5. NEVER lose information: anything that does not fit a token (dates in
   TASKS/SPRINT/BACKLOG, sub-bullets) stays in the description text.
6. Show the user a per-file summary (items reformatted / left untouched) and,
   if Cedar Clerk is running, remind them the board auto-refreshes via the watcher.

## Verify

After writing, re-read each file and check: every intended task line starts
with `- [` + one of ` >?x` + `] `; TASKS.md has its `##` lanes; ROADMAP lines
carry one time token. If the user reports the board still misses an item, the
line failed one of the shapes above — fix the line, not the parser.
