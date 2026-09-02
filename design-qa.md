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

---

# Discovery design QA

## Visual truth

- Source: `C:\Users\marty\.codex\generated_images\01a06243-ba07-76c1-94de-1546365966e0\exec-e39f6a79-1910-4ea3-a717-9b60ea394ba3.png`
- Source pixels: 1487 x 1058
- Target state: English, populated `All` feed, desktop navigation, Saturday stage, independent blogs, and Project Showcase.
- Desktop implementation: `C:\Users\marty\AppData\Local\Temp\cedar-discovery-filled-v3-1536.png`
- Mobile implementation: `C:\Users\marty\AppData\Local\Temp\cedar-discovery-mobile-v2-390.png`
- Settings focus: `C:\Users\marty\AppData\Local\Temp\cedar-settings-discovery-1536.png`
- Admin focus: `C:\Users\marty\AppData\Local\Temp\cedar-admin-discovery-1536.png`

The implementation was rendered at a 1536 x 1024 desktop viewport and a 390 x 844 mobile viewport. The source is a fixed one-screen concept; the implementation extends below the comparable first screen because the requested Devlogs and category directory are functional sections rather than hidden navigation targets.

## Comparison record

| Pass | Finding | Severity | Resolution |
|---|---|---:|---|
| Initial desktop comparison | The stage was an image overlay instead of the reference's image, editorial copy, and category rail. | P1 | Rebuilt the stage as the same three-part composition and reduced its height. |
| Initial desktop comparison | A separate oversized introduction pushed the two-column feed below the fold. | P1 | Removed the redundant block and moved the public-sharing note into the section lead. |
| Mobile review | Standard project rows and category cards were too tall at 390 px. | P2 | Kept compact project rows and a two-column category grid until the 340 px breakpoint. |
| Final combined comparison | Hierarchy, stage proportions, filter bar, warm paper feed, two-column split, card density, type scale, borders, and restrained cedar/brass palette match the selected direction. | Pass | No actionable P0, P1, or P2 mismatch remains. |

The populated QA database deliberately reuses one existing local blog image. Production cards render each author's real public cover or gallery image, so image subjects and avatar presence are data-dependent rather than fabricated to imitate the concept.

## Functional checks

- `All`, `Projects`, and `Blogs` filters update the query and visible item types.
- Search narrows both projects and matching devlogs.
- Shuffle produces a new randomized feed order.
- `#ScreenshotSaturday` uses only exact public tags with a usable public image.
- Standalone posts are labelled `Blog`; project-linked posts are labelled `Devlog`.
- Project categories are visible and remain two columns at 390 px.
- The 390 px render has no horizontal overflow.
- Profile Discovery consent is off by default and describes the public-only rule.
- Admin Discovery exposes global section switches, eligibility counts, bilingual copy, and a public-page link.
- Browser console inspection found no error-level messages in the settings or admin flows.

final result: passed
