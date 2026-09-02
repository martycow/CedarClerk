// The tool strip fits itself (ADR-150): the number of rows and whether group captions are drawn
// are read off measured widths, never off a stored preference and never off a width breakpoint —
// ADR-147 forbids inventing one of those until the narrow screens are commissioned.
//
// The ladder is ADR-150 clause 2's order, which is why captions are a rung of their own above
// wrapping rather than a consequence of it: a group whose caption is wider than its buttons is
// wider *because of* the caption, so dropping captions can buy a row that would otherwise wrap.

export interface ToolbarFit {
    rows: 1 | 2;
    captions: boolean;
    /** How many groups stay on the first row. Equals the group count when `rows` is 1. */
    firstRow: number;
}

export interface ToolbarMeasurements {
    /** Usable width inside the strip's padding. */
    available: number;
    /** Flex gap between two items on a row. */
    gap: number;
    /** Block type — pinned to the head of row 1; history lives in the always-present top bar. */
    lead: number;
    /** The AI chip and the view controls — pinned to the tail of row 1. */
    trail: number;
    /** Every group's outer width with its caption drawn, in render order. */
    withCaptions: readonly number[];
    /** The same groups measured with the captions suppressed. */
    withoutCaptions: readonly number[];
}

function rowWidth(items: readonly number[], gap: number): number {
    if (items.length === 0) return 0;
    return items.reduce((a, b) => a + b, 0) + gap * (items.length - 1);
}

/** The longest prefix of `groups` that fits beside the pinned lead and trail. */
function firstRowCount(groups: readonly number[], m: ToolbarMeasurements): number {
    let count = 0;
    for (let n = 1; n <= groups.length; n++) {
        if (rowWidth([m.lead, ...groups.slice(0, n), m.trail], m.gap) > m.available) break;
        count = n;
    }
    return count;
}

function fitsTwoRows(groups: readonly number[], m: ToolbarMeasurements): number | null {
    const head = firstRowCount(groups, m);
    // Nothing on the second row means the first row already held everything, which is the rung
    // above this one; a caller reaching here has already measured that it does not.
    if (head >= groups.length) return null;
    if (rowWidth(groups.slice(head), m.gap) > m.available) return null;
    return head;
}

/**
 * The first rung of the ladder that fits, or the last rung when none does — at which point the
 * strip scrolls, which is what it does today and is a worse answer than any of the four, not a
 * different kind of answer.
 */
export function fitToolbar(m: ToolbarMeasurements): ToolbarFit {
    const n = m.withCaptions.length;

    for (const captions of [true, false]) {
        const groups = captions ? m.withCaptions : m.withoutCaptions;
        if (rowWidth([m.lead, ...groups, m.trail], m.gap) <= m.available) {
            return { rows: 1, captions, firstRow: n };
        }
    }

    for (const captions of [true, false]) {
        const head = fitsTwoRows(captions ? m.withCaptions : m.withoutCaptions, m);
        if (head !== null) return { rows: 2, captions, firstRow: head };
    }

    return { rows: 2, captions: false, firstRow: firstRowCount(m.withoutCaptions, m) };
}
