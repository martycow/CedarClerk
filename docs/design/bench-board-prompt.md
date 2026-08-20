---
owner: marty
last_verified: 2026-08-20
source_of_truth_for: the brief handed to Claude Design for the fourth Cedar Bench reference screen — the task board (ADR-165, T-229)
guard: none
---

# Claude Design brief — the Cedar Bench task board

A ready brief for a design run. Everything below the `## PROMPT` heading is written as an address to
the design tool and is copied into it whole; everything above is for whoever launches it.

**Context for the launcher** (do not copy):

- Why this exists — `docs/adr/ADR-165.md`. `project-tasks` is the one screen in the port with no
  reference screen behind it: three exist, and none of them is a board of columns a card moves
  between. The screen has been brought onto the bench's materials — every control is a bench
  component and every value a token — and its arrangement is untouched, waiting for this.
- Why it is a separate brief from `bench-responsive-prompt.md`: that one asks what the three
  existing screens become at three narrower widths, and all nine of its questions are about the
  shell's chrome. This one asks for a screen that does not exist yet, at desktop width. Folding them
  together would produce one run whose two halves cannot be accepted or rejected separately.
- The design system being extended is mirrored read-only at `.design-sync/ds-v2/` — the same project
  the three reference screens came from. Run it there rather than starting a new one.
- **Tokens are not copied into this file.** Paste fresh values from `cedarclerk-web/src/styles.scss`
  into the marked block below before each run; the map of what each name means is
  `docs/design/DESIGN.md`.
- What comes back turns into porting work under `T-229`. Read `.claude/rules/ui-changes.md` before
  any of it reaches code.

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
and onboarding hints are dead weight almost everywhere. **There is one user — an assignee is a text
field, and there is nobody to hand a card to.**

### What V2 is — the Cedar Bench

The interface is a workshop bench. There is exactly **one look** with **one axis of variation**: a
light theme and a dark theme. There is no second palette, no second type scale, no second icon set.

The material rule the whole system rests on: **the reading surface is paper, and the chrome is wood
and brass.** Paper is where text is written and read — sheets, cards, forms; it is generously spaced
and generously sized. Wood is the frame around it — the rail across the top, the tool rail down the
left, the shelf panels, the drawer, the ruler along the bottom; it is dark, dense and small-typed.

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
steps do not exist**; font sizes are whole pixels and are reached through the meaning roles, never
through raw numbers; the serif face is **only** for reading surfaces; icons are Phosphor and no set
is mixed in.

### What already exists, and is being extended rather than restarted

Three reference screens are built in this project, each declaring a fixed `1440x900` frame. They are
correct, they are the vocabulary this screen must be made of, and they are not being redrawn:

- **Hub** (`ui_kits/hub.html`) — the project bench: a 262px left column of projects, a middle of
  module plates, a 322px right shelf with the sprint and what to pick up next.
- **Writer** (`ui_kits/writer.html`) — the sheet on the bench, with an outline dock and an inspector.
- **Stats** (`ui_kits/stats.html`) — metrics as a page of a log book; leaf tags above the chart that
  are simultaneously the legend and the source filter.

The components this screen is already assembled from, and which it should keep using:

| Component | What it is | Where it is already used |
|---|---|---|
| **ShelfPanel** | a wooden board, a carved sign tile as header with a count and the panel's own command, a paper sheet inset into it | every dock and inspector; here, one per board column |
| **TaskTag** | a luggage tag on a brass hook: clipped top corners, a brass eyelet, a priority chip, an optional stamp, a due date, and a fade when done | the hub's "up next"; here, every card on the board |
| **StampBadge** | a rubber stamp in pine, brass, rust or ink | states and versions |
| **LeafTag** | a leaf that is a filter and a legend at once, idle / active / dried | the metrics source filter; here, the filter strip |
| **IndexTabs** | painted index tiles, 30px, switching what a panel shows — never navigating | here, board / list |
| **Button** | pine, paper, rail, danger | everywhere |

### What to draw

**One screen, at 1440x900, in both themes: the task board.** Plus the states listed below, and the
answers to the questions.

What is actually on it, and all of it exists in the data today:

- **Four columns, in this order and with these names**: Backlog, Planned, In progress, Done. The column
  set is fixed — there is no add-a-column, and there never will be.
- **A card** carries: a title (one to three lines), a priority of 1, 2 or 3 (1 is the only one that
  is visually loud), an optional due date which may be overdue, an optional sprint number (`S3`), an
  optional assignee as free text, and zero or more links to documents, assets or other tasks, each
  with an icon and a label. A card can be archived, and a card that is done fades rather than being
  struck through.
- **A filter strip** above the board: all / open / overdue as counted chips, then a divider, then one
  chip per sprint plus "no sprint" — between two and eight chips in practice, and they can wrap.
- **A search field** filtering title, description and assignee.
- **A card, opened**: today a centred 560px modal holding the title, six fields (status, priority,
  due date, assignee, sprint, build), a plain-text description, the link list with an add control,
  three timestamps, and five commands — delete, archive, save, and mark done.
- **A list view** of the same tasks: a five-column table — task, status, priority, due, links — with
  sortable headers. It is not a fallback; the board answers *what is happening* and the table answers
  *what is due and in what order*.

### The questions that must be answered

Each of these is a real fork the three existing screens do not settle. Please choose, and say why.

**1. Is a column a ShelfPanel, and does the board scroll?** Four panels side by side at 1440px gives
each about 320px, which is the width the docks already use — so the board fits, once. What happens
when a fifth state is never added but a column holds forty cards: does the column scroll inside
itself with the header pinned, does the whole page scroll, or does the column cap and say how many
are hidden? And is a board column's header the same carved sign tile as a dock's, or does a column
need a header of its own kind — it carries a count and a single "add here" command, which is exactly
a panel header's shape, but four of them in a row is a lot of carved wood.

**2. How does a card move between columns?** Today it does not: a card is opened and its status is
changed in a field. Drag and drop is the obvious answer and it is the expensive one — it needs a
drag affordance, a drop target, a placeholder, an auto-scroll at the edges, a keyboard equivalent
and a touch equivalent, and the tag's own visual is a *hanging* one, on a hook, which is a strange
thing to drag. If the answer is drag, draw the three moments: the card lifted, the gap it would land
in, and the column highlighted. If the answer is not drag, draw what replaces it on the card itself —
a next/previous control on hover, a status chip that is a menu, something else.

**3. What is the tag's density on a board?** The hub hangs one "up next" tag with room around it. A
column holds fifteen, stacked. The hook and the eyelet are what make the component recognisable and
they cost vertical space per card; the clipped corners and the tilt cost horizontal. Does a card in
a column keep the hook, drop it, or share one rail per column with the tags hanging from it? Say
what the tilt does when fifteen tilted cards stack — alternating, all one way, or flat.

**4. Where do a card's links go?** A card can carry six link chips, each an icon and a label, and
they are the most useful thing on it — a task pointing at the document it is about. On a 320px card
they either wrap to three rows or truncate to icons with a count. Choose, and say what a link chip
looks like when its target has been deleted, which is a state the data really produces.

**5. The opened card: modal, or inspector?** Every other screen in V2 that edits a selected thing
uses a right-hand ShelfPanel inspector beside the thing it edits — the writer does exactly this. The
board uses a centred modal, which covers the board. An inspector on this screen would take a column's
worth of width from a four-column board. Choose one, and if it is the inspector say what the board
does with the width — three columns and a scroll, or narrower columns.

**6. The empty and the overfull states.** Draw: a board with no tasks at all, a board where the
filter matched nothing, a single empty column beside three full ones, and a column holding forty
cards. An empty state says what belongs there rather than apologising.

**7. What does the list view look like on the bench?** A five-column table on paper, or the same
tags in one column, or a spec-row list? It shares the filter strip and the search with the board, and
the switch between them is an IndexTabs pair. Say which surface the table's header row is — chrome
would make it a dock header, paper would make it part of the sheet.

**8. Does anything on this screen belong on the RulerBar?** The rule along the bottom carries
read-only readouts. This screen already publishes open-of-total and an overdue count there. If the
board wants a third — a sprint position, a column tally — say so; if the ruler is the wrong place for
it, say that instead.

### Conditions that hold for every screen

- **Both themes.** Light and dark for each state. Dark is not post-processing.
- **WCAG AA contrast**, checked automatically in the repository, so a violation will not ship. The
  quietest text tone never carries information — it is placeholder, disabled state and decoration.
- **Two languages.** The interface runs in Russian and English. Use German as a length stress test: a
  label **compresses and truncates with an ellipsis, and never widens its container**.
- **States are part of the deliverable**: empty, loading, error, and far too much data.
- **Focus.** One global `:focus-visible` ring — a dark brass outline inside a cream halo, 2px with a
  2px offset. A component that removes the outline owes a replacement. Note that a clipped shape
  cannot carry a ring on itself, which is why the task tag's ring is drawn around the whole tag and
  not around the clipped paper.

### What not to do

- Do not invent a second palette, a second size scale or a second icon set.
- Do not redraw the three existing screens. They are the reference; this one sits beside them.
- Do not design collaboration features. There is one user; an assignee is a text field, there are no
  avatars on cards, no mentions, no activity feed and nobody to assign to.
- Do not add a column, a swimlane, a WIP limit or a burndown to the board. The four statuses are the
  data model, sprints are a separate screen, and the chart on the metrics screen is about audience.
- Do not answer the narrow-screen question here. Widths below 1440px are the other brief's
  deliverable, and answering them twice is how two answers disagree.
