// The token names a server-rendered surface may reference (ADR-137).
//
// One list, two readers: generate-design-tokens.mjs copies exactly these out of styles.scss into
// CedarClerk.Core/DesignTokens.generated.cs, and check-contrast.mjs refuses a gradient on any of
// them — a gradient here is not a failing ratio, it is a gradient string painted by the blog and
// the landing page where a flat colour is expected. A second copy of the list is how the two
// halves of that rule would come apart.
export const CONTRACT = [
    'bg', 'canvas', 'surface', 'sheet', 'alt', 'border', 'border-strong',
    'text', 't2', 't3', 'accent', 'danger', 'ok', 'warn',
    // The wall carries one ink and it is not --text (ADR-141), so a server-rendered surface that
    // paints the wall has nothing readable to put on it without this name.
    'wood-ink', 'wood-ink-soft',
    'series-1', 'series-2', 'series-3', 'series-4', 'series-5', 'series-6',
    'shadow', 'asoft', 'abord', 'font-sans', 'font-mono', 'font-serif',
    'fs-read', 'lh-read', 'radius-sm', 'radius-md', 'radius-lg',
];

// The bench materials a server-rendered surface paints with (ADR-177).
//
// The contract above answers "what is this for" and holds flat colours because the drift test pins
// it and the appearance service rewrites part of it. This one answers "what is this made of", and
// a material is allowed to be the gradient, the texture URL or the shadow it actually is — the
// gradient refusal walks CONTRACT and deliberately not this list.
//
// A name earns its place by having a call site today and by holding a value rather than a synonym:
// --paper and --ink are --sheet and --text under a second name, so they are absent, while
// --pine-hi/--pine-deep are mixed from the accent and are the only place that ramp exists.
export const MATERIALS = [
    // The plaster wall the page stands on, and the lamp over it. The two stops travel because
    // --surface-page dereferences them.
    'wall-hi', 'wall-lo', 'surface-page', 'lamp',
    // Bare bench wood, and the shelf board and carved sign tile cut from it. A server-rendered
    // page that paints a bench needs the board as well as the rail — the rail is the header,
    // the board is every panel under it (ADR-215).
    'wood-hi', 'wood', 'wood-lo', 'wood-edge', 'shelf-frame', 'shadow-shelf',
    'sign-tile-hi', 'sign-tile-lo', 'grad-sign-tile', 'bench-panel-hd',
    // Dark park-sign wood: the board a public header is cut from, and the buttons mounted on it.
    'rail-hi', 'rail-mid', 'rail-lo', 'rail-edge', 'rail-ink', 'rail-ink-soft',
    'surface-rail', 'rail-btn-face', 'rail-btn-face-hover', 'border-rail-btn', 'shadow-rail', 'tex-wood',
    // The rail's own height. A server-rendered header that is a rail has to stand exactly as tall
    // as the app's, or the two halves of one product disagree at the first pixel a visitor sees.
    'bench-rail-h',
    // Paper stock, its edge and its noise; the shadows that hold a sheet off the wall.
    'paper-bright', 'paper-edge', 'tex-paper', 'border-paper',
    'shadow-paper', 'shadow-paper-sm', 'shadow-sheet', 'shadow-field-inset',
    'radius-paper', 'radius-stamp', 'radius-plaque', 'radius-field',
    // The pine ramp. --accent is the flat role and stays on the contract; these are the lit and
    // shaded faces of one button, and they follow the user's preset because they are mixed from it.
    'pine', 'pine-hi', 'pine-deep', 'grad-pine', 'text-on-pine', 'shadow-pine-btn',
    // The conifer beside the wordmark. Held rather than mixed, because it is the one green painted
    // on wood and the accent is the user's to change.
    'pine-mark',
    // Marks: leaves for tags and filters, brass for hardware and the rule.
    'leaf-bg', 'leaf-bg-2', 'leaf-ink', 'leaf-dried-bg', 'leaf-dried-edge',
    'brass', 'brass-hi', 'brass-lo', 'brass-edge', 'brass-ink', 'grad-brass', 'focus-halo',
    // Resin: attention, and the one orange the product has — the feed button on the blog is
    // painted with it because a feed mark is recognised by its colour before its shape.
    'resin', 'resin-hi',
    // The state washes a stamp is painted on (ADR-145), and the brass one a version mark takes.
    'ok-soft', 'brass-soft',
    // Carved lettering, the readout face (ADR-180), and the pencil rules the wall is ruled with.
    'font-display', 'font-readout', 'font-note', 'rule-ink', 'rule-ink-soft',
];

/** Everything DesignTokens carries, which is what a server sheet may name. */
export const SERVED = [...CONTRACT, ...MATERIALS];
