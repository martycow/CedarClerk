---
name: tasks
description: Show a table of the most recently added OPEN tasks from docs/tasks/BACKLOG.md (newest first, by T-ID number). Use whenever the user runs /tasks or asks to see tasks, recent items, "what's in the backlog", the latest board rows — even if the word "table" isn't said.
---

# /tasks — latest open board tasks

Run the script and show its output as-is (it's a ready-made markdown table):

```
pwsh -File .claude/skills/tasks/scripts/tasks.ps1
```

If a number is passed (`/tasks 20`) that's the row count: add `-Count 20`.
With no argument the script shows 35 (the middle of the requested 30–40 range).

What the script does and why:

- **Sorted by ID number descending = "most recently added"**: T-numbers are issued
  sequentially and never reused (the board rule in BACKLOG.md's header), so the
  number is an honest freshness signal. The board doesn't store an added-date, and
  a git archaeology pass on every call would be slow and expensive.
- **"Open" = anything not marked done**: rows checked `[x]` are skipped. Per the
  board's own rules such rows get deleted rather than left checked, but between
  sessions they sometimes hang around marked.
- Only `T-xxx` rows are shown; `Q-xx` questions aren't tasks, the script doesn't pick them up.
- Descriptions are deliberately left out of the table — 35 rows with full
  descriptions would be unreadable; the last output line reminds that descriptions
  live in `docs/tasks/BACKLOG.md`.

If the script fails (e.g. BACKLOG.md moved) — fix the cause, don't assemble the
table by hand: a hand-picked selection will drift from the board on the very next call.
