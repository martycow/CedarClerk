---
owner: marty
last_verified: 2026-08-20
source_of_truth_for: the brief handed to Claude Design for narrow-screen Cedar Bench layouts (ADR-147, T-236)
guard: none
---

# Claude Design brief — Cedar Bench on narrow screens

A ready brief for a design run. Everything below the `## PROMPT` heading is written as an address to
the design tool and is copied into it whole; everything above is for whoever launches it.

**Context for the launcher** (do not copy):

- Why this exists — `docs/adr/ADR-147.md`. The shell (Stage 3 of `docs/design/UI-V2-PLAN.md`) is built
  to the kit's fixed 1440×900 proportions and adds no width breakpoint of its own; the narrow
  behaviour is deferred to this deliverable rather than invented in the port. Nothing in this
  repository can make the deliverable arrive, and `T-034` cannot close until it does.
- The design system being extended is mirrored read-only at `.design-sync/ds-v2/` — the same project
  the three reference screens came from. This is an extension of that project, so run it there rather
  than starting a new one.
- **Tokens are not copied into this file.** A verbatim block here goes stale the moment `styles.scss`
  moves, and that has already happened once to the other brief in this folder. Before each run, paste
  fresh values from `cedarclerk-web/src/styles.scss` into the marked block below; the map of what each
  name means is `docs/design/DESIGN.md`.
- Answers come back as screens, and each one that lands turns into porting work under `T-237`. Read
  `.claude/rules/ui-changes.md` before any of it reaches code.

---

## PROMPT

### What the product is

Cedar Clerk is a self-hosted writing and production tool for one indie game developer. It began as a
rich-text editor that publishes long posts to a Telegram channel and to a mirrored blog; it is now a
toolkit where a post is one document type among several, and documents live inside a project — a
project being a game. Around them sit tasks, sprints, an index of the game's local asset files, and
audience metrics.

The user is one person wearing every hat: programmer, designer, writer, composer, marketer, analyst.
They work daily and for long stretches, and know the interface by heart. Density beats friendliness,
and onboarding hints are dead weight almost everywhere.

### What V2 is — the Cedar Bench

The interface is a workshop bench. There is exactly **one look** with **one axis of variation**: a
light theme and a dark theme. There is no second palette, no second type scale, no second icon set.

The material rule the whole system rests on: **the reading surface is paper, and the chrome is wood
and brass.** Paper is where text is written and read — sheets, cards, forms; it is generously spaced
and generously sized. Wood is the frame around it — the rail across the top, the tool rail down the
left, the shelf panels, the drawer, the ruler along the bottom; it is dark, dense and small-typed.

That split is a hard contract, and it is the reason this brief exists:

| | Chrome — wood and brass | Paper — everything read |
|---|---|---|
| Control minimum | 30px (`--hit-chrome`) | 44px (`--hit-target`) |
| Text | 11–13px, numbers always monospace | from 14px; reading text 17/1.75 |
| Members | rail, tool rail, docks, drawer, ruler, tabs | cards, tasks, forms, the sheet |

### Tokens — mandatory, use them verbatim

> **[PASTE BEFORE RUNNING]** — current values from `cedarclerk-web/src/styles.scss`:
> the light palette (`:root`), the dark overrides (`:root[data-theme="dark"]`), radii `--radius-*`,
> spacing `--space-*`, font stacks `--font-*`, the size roles
> `--fs-caption/meta/ui/body/title/read` with `--lh-read`, icon sizes `--icon-*`, the motion set
> `--motion-*` + `--ease*`, the density table `--dens-*`, and the bench layout block
> (`--bench-rail-h --bench-tool-w --bench-dock-w --bench-dock-w-wide --bench-panel-hd
> --bench-drawer-lip --bench-drawer-open --bench-ruler-h --hit-chrome --hit-target --text-chrome
> --text-chrome-sm --page-max --page-pad`).
> No verbatim copy is kept in this file on purpose: a duplicated token block drifts from the code
> silently.

Rules on top of the values, which do not change: spacing steps roughly double and **values between
steps do not exist** (if 11px is needed, 12px is what is meant); font sizes are whole pixels and are
reached through the meaning roles, never through raw numbers; the serif face is **only** for reading
surfaces; icons are Phosphor and no set is mixed in.

### What already exists, and is being extended rather than restarted

Three reference screens are already built in this project, each declaring a fixed `1440×900` frame.
They are the desktop answer, they are correct, and they are not being redrawn:

- **Hub** (`ui_kits/hub.html`) — the project bench: a 262px left column of projects, a flexible middle
  of module plates in a 3-column grid, a 322px right shelf with the sprint and what to pick up next.
- **Writer** (`ui_kits/writer.html`) — the sheet on the bench: `224px | flexible | 330px` — a document
  outline on the left, a 640px writing sheet on a lit worktop in the middle, an inspector for the
  selected block on the right, and the checks journal in the drawer below.
- **Stats** (`ui_kits/stats.html`) — metrics as a page of a log book: one flexible column holding a
  multi-series ink chart on graph paper, a 320px right shelf for the audience breakdown, and leaf tags
  above the chart that are simultaneously the legend and the source filter.

The chrome common to all three, which is what this brief is really about:

| Piece | Desktop spec | Carries |
|---|---|---|
| **RailHeader** | 56px tall, sticky, full width, the only wood in the top | brand + version, project switcher tile, breadcrumb, and a right slot with save state, the primary action (Publish), the user, and one `dots` button for the rare rest — theme toggle, appearance, glossary, admin |
| **HookRail** | 52px wide, full height, left edge, pegboard with brass hooks | 5–7 tools, each an icon over a caption, each clearing 30px. This *is* the navigation — there is no menu bar and no header nav row |
| **ShelfPanel** | 300px, or 340px wide; header 30px | every dock, sidebar and inspector. A wooden board, a carved sign tile as header, a paper sheet inset into it. A panel's own commands live in its header, never in a global toolbar |
| **BenchDrawer** | 32px lip closed, 172px open, bottom edge | the journal — checks, build log, console. Closed by default on writing and reading screens; the lip shows one summary line, which is what justifies leaving it shut |
| **RulerBar** | 30px, bottom | a brass carpenter's rule: read-only readouts in monospace — counts, state words, version. One line only; if it would wrap, readouts are cut rather than the bar grown |
| **IndexTabs** | 30px painted index tiles | switching what a panel or drawer *shows*. Never for navigating between screens |

### What to draw

Narrow versions of all three reference screens — **hub, writer, stats** — in both themes.

Three device widths, because they are the actual devices this is used on, not round numbers:
**1180×820 in landscape on a touch tablet**, **820×1180 in portrait on the same tablet**, and
**390×844 on a phone in portrait**. If the design's own breakpoints do not land on those three,
draw the widths the design chooses *and* show what these three devices get.

Also draw, once, a chrome sheet: the six pieces above at each chosen width, side by side with their
desktop form, so the port has one place to read the answers off.

### The questions that must be answered

The chrome is what the shell is made of, and it is the part the desktop kits do not answer. Each of
these is a real fork with a real cost — please choose, and say why, rather than leaving it open.

**1. RailHeader — one row, or two?** It is sticky, so whatever it costs, it costs on every scroll. At
390px it cannot hold the brand, the project tile, a breadcrumb, the save state, a Publish button, the
user and the dots menu on one 56px line. Does it stay one row and shed contents — and in what order do
they go, version first, then crumb, then the project name down to an icon? Or does it become two rows,
which is another 56px permanently gone from an 844px-tall screen? Constraint: the `dots` button has to
survive at every width, because the theme toggle and the appearance settings live behind it and exist
nowhere else.

**2. HookRail — where does the navigation go?** Four collapses are each defensible and the kit chooses
none. *Rotate to a bottom bar*: thumb-reachable and the platform idiom, but it eats vertical space the
sheet needs and puts navigation at the opposite edge from the rail's own controls — and a rail of
hanging hooks that has been laid on its side is no longer hanging, so the material has to change or
the metaphor breaks. *Narrow to icons without captions*: keeps its place and its material, loses the
captions that make five destinations legible. *Collapse behind the `dots` control*: costs nothing but
makes every navigation two taps and restores exactly the hidden one-level-deep menu V2 deleted on
purpose. *Become an off-canvas overlay with a summon gesture*: keeps captions, costs a gesture nobody
is taught. Pick one, and say what the pegboard becomes in it.

**3. ShelfPanel docks — at what width does a dock stop being a column, and what does it become?**
Answer this **separately for the two roles**, because they behave differently: an outline or a project
list is *navigation* — it can be summoned and dismissed; an inspector is *editing the current
selection* — it needs to be visible at the same time as the thing it edits, and an overlay inspector
on a 390px screen covers the very block being edited. Options worth costing: an overlay sheet over the
worktop, a tab inside the BenchDrawer, an IndexTabs body stacked above the sheet, or dropped
entirely at the narrowest width. And a second part: does the ShelfPanel's own visual language — board,
sign tile, inset paper — still read when the panel is full-bleed, or does a full-width shelf just look
like a heading above a list?

**4. BenchDrawer — what is a bottom drawer on a screen taller than it is wide?** Does it stay at the
bottom, and does 172px open stay 172px, become a proportion of the viewport, or become a full-height
sheet? On a 390×844 phone, 56 rail + 30 ruler + 32 lip leaves about 580px for the work before the
drawer opens at all; keeping the lip always visible spends 32px permanently on one summary line. Is
that summary line still worth its 32px when it is alone on the row, or does the drawer become
something summoned rather than something always present — and if summoned, from what?

**5. RulerBar — does it survive at 390px?** It is read-only and it is the only place carrying word
count, sync state, sprint position and version. Deleting it means the phone has no status at all;
keeping it spends 30px on a strip that cannot wrap by its own rule. If it survives, which readouts
survive with it and in what order are the rest cut — and where does the cut information go, into the
drawer summary or nowhere?

**6. Worktop and the 640px sheet.** Does the writing sheet keep 640px and lose its margins, or shrink
to viewport-minus-padding? And at what width does the bench itself stop being drawn — the squared
pencil grid and the lamp wash are a guide under the content, and on a 390px screen the paper may
simply be the whole screen with no bench visible around it. If the bench does disappear there, say so
explicitly, because a worktop that fades out unannounced looks like a bug.

**7. IndexTabs at narrow width.** Horizontal scroll, wrap to a second row, or collapse into a select?
Named because one of them is load-bearing: the metrics screen is reached as a **tab**, not from the
tool rail — it has no hook and no breadcrumb of its own — so on a phone the tab strip is the only way
to reach it.

**8. The density contract under a coarse pointer — the question our own answer would be a guess.**
The app forces a 44px minimum on every control when the pointer is coarse. The chrome is specified at
30px with 11–13px text. These contradict, and **width cannot arbitrate**: a tablet in landscape is
1180px wide *and* entirely touch-driven, so any rule phrased as "narrow means touch" is wrong on the
device this is used on most. Which is it:

- the chrome grows to 44px on touch and gives up the density contract — meaning a taller rail, taller
  dock headers, a taller ruler, on the device with the least room;
- the chrome keeps its 30px *appearance* and gets an invisible 44px hit area — which works until two
  chrome controls sit next to each other, and in the rail's right slot they always do;
- or touch gets a **different composition** — fewer chrome controls, each at a full 44px, with the
  rest moved behind the dots menu and the panel headers.

Please answer for the tablet in landscape explicitly, since it is the case that breaks the easy rules.

**9. Which breakpoints does the design assume?** The system currently states none — the mirror has no
width rule anywhere. Name the widths, and give the reason for each number. Say also whether anything
other than width ever decides: orientation, pointer type, or the presence of a hardware keyboard. If
the answer is that width alone is not enough, that is a useful answer and worth stating plainly.

### One thing the app decided differently from the kit

The writer's tool strip is **adaptive**: one row of controls when they fit, two rows when they do not,
with group captions appearing only when there is room for them, and no user customization. Whatever
the narrow-screen answer is, it has to say what that strip does below the width at which even two rows
fit — scroll horizontally, drop groups behind an overflow control, or move into a panel.

### Conditions that hold for every screen

- **Both themes.** Light and dark for each screen. Dark is not post-processing: if something works in
  only one of them, it is not finished.
- **WCAG AA contrast**, checked automatically in the repository, so a violation will not ship. Related
  rule: the quietest text tone never carries information — it is placeholder, disabled state and
  decoration. Text that is quiet because it is secondary uses the middle tone at a smaller size.
- **Two languages.** The interface runs in Russian and English. Use German as a length stress test: a
  label **compresses and truncates with an ellipsis, and never widens its container**.
- **States are part of the deliverable.** For each screen: empty, loading, error, and far too much
  data. An empty state says what belongs there rather than apologising.
- **Focus.** One global `:focus-visible` ring in the accent colour, 2px with a 2px offset. A component
  that removes the outline owes a replacement.

### What not to do

- Do not invent a second palette, a second size scale or a second icon set — the narrow layout is the
  same materials rearranged.
- Do not redraw the desktop screens. They are the reference; the deliverable sits beside them.
- Do not design a phone-only visual language. This is one product that has to be recognisable as
  itself at 390px, not a companion app.
- Do not resolve the density contradiction by quietly dropping the chrome's monospace numerals or its
  small type — if the chrome has to grow on touch, say that it grows.
- Do not design collaboration features. There is one user; an assignee is a text field.
