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
    'wood-ink',
    'series-1', 'series-2', 'series-3', 'series-4', 'series-5', 'series-6',
    'shadow', 'asoft', 'abord', 'font-sans', 'font-mono', 'font-serif',
    'fs-read', 'lh-read', 'radius-sm', 'radius-md', 'radius-lg',
];
