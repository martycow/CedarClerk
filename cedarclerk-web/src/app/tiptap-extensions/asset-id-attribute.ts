/**
 * ADR-238 — the one link between a media node and the Asset it was inserted from. The three file
 * facts the inspector shows (resolution, byte size, originating file) are read from the server
 * through this id, never copied onto the node: they belong to the file and would go stale here.
 *
 * Null is the ordinary case, not an error — every document written before this existed has it, and
 * such a node renders, saves and exports exactly as before because nothing is emitted for a null.
 */
export const assetIdAttribute = {
    default: null as string | null,
    parseHTML: (element: HTMLElement) => element.getAttribute('data-asset-id'),
    renderHTML: (attributes: Record<string, unknown>) =>
        attributes['assetId'] ? { 'data-asset-id': attributes['assetId'] } : {},
};
