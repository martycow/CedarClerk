# Design QA

## Target

Hybrid of the selected editorial-studio concepts: the bounded, tactile workbench from direction 3
with the stable list → work area → inspector hierarchy from direction 1.

Reference: `.e2e-audit/editorial-studio-hybrid-target.png`.

## Implemented surfaces

- Posts Manager: read-first editorial overview, publication journey, performance trend and activity.
- Metrics: stronger filter/readout hierarchy and a wider audience rail.
- Projects: bounded card grid and a larger two-column New Project choice matrix.
- Editor: bounded workspace, wider inspector and clearer paper frame.
- Publish: wider destination rail and more readable parallel settings.
- Settings: centred 1280px form measure, larger section headings and roomier cards.

## Mechanical checks

- `cedar test`: passed, 2376 tests.
- Backend, frontend units, icon inventory, contrast and density: passed.
- Production Angular build: passed with the existing initial-bundle budget warning.

## Visual comparison

Captured with Playwright against the isolated E2E stack (`Scripts/e2e.ps1 -Serve` + `ng serve`),
1440×900 in both themes and 2560×900 by day: Posts Manager (post selected), Metrics, Projects, the
editor's Write and Preview tabs (Blog, X single and thread, Bluesky thread), Settings. Against the
reference: the studio card, the publication journey, the performance readout and the activity list
stand where the reference puts them; the working measure stays bounded at 2560. Two things the
reference draws that the data cannot answer are not drawn (view history on a fresh database, a
destinations table per post). Fixed from the pass: the post list's *Not published* chip truncated
every title beside it (now *Draft*), the journey's *not published* wrapped under its icon, and the
inspector lacked the reference's created / last-edited dates.

## Result

Mechanical checks and a screenshot pass done; the by-eye pass at the maintainer's own ultrawide size
is still theirs to make (`docs/tasks/TASKS.md` §Live verification).
