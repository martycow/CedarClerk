/** The kinds of content a document can hold, in the order the matrix lists them. */
export type DocumentKind =
    | 'text' | 'headings' | 'lists' | 'links' | 'images' | 'video' | 'audio' | 'tables' | 'code' | 'math' | 'quotes';

export const DOCUMENT_KINDS: readonly DocumentKind[] =
    ['text', 'headings', 'lists', 'links', 'images', 'video', 'audio', 'tables', 'code', 'math', 'quotes'];

export type DocumentKindCounts = Record<DocumentKind, number>;

const NODE_KIND: Record<string, DocumentKind> = {
    paragraph: 'text',
    heading: 'headings',
    bulletList: 'lists',
    orderedList: 'lists',
    taskList: 'lists',
    image: 'images',
    carousel: 'images',
    collage: 'images',
    video: 'video',
    youtube: 'video',
    audio: 'audio',
    table: 'tables',
    codeBlock: 'code',
    math: 'math',
    mathBlock: 'math',
    blockquote: 'quotes',
    expandableBlockquote: 'quotes',
};

/**
 * How many of each kind a TipTap document holds — the rows of the publish matrix that are lit.
 * A gallery counts each picture; a link is a mark on text, counted once per text node that wears it.
 */
export function documentKinds(cedarJson: string): DocumentKindCounts {
    const counts = Object.fromEntries(DOCUMENT_KINDS.map(k => [k, 0])) as DocumentKindCounts;
    let doc: unknown;
    try {
        doc = JSON.parse(cedarJson);
    } catch {
        return counts;
    }
    walk(doc, counts);
    return counts;
}

function walk(node: unknown, counts: DocumentKindCounts) {
    if (Array.isArray(node)) { for (const child of node) walk(child, counts); return; }
    if (!node || typeof node !== 'object') return;
    const n = node as { type?: string; attrs?: { images?: unknown[] }; marks?: { type?: string }[]; content?: unknown };
    const kind = n.type ? NODE_KIND[n.type] : undefined;
    if (kind === 'images' && (n.type === 'carousel' || n.type === 'collage')) {
        counts.images += Array.isArray(n.attrs?.images) ? n.attrs.images.length : 0;
    } else if (kind) {
        counts[kind] += 1;
    }
    if (n.type === 'text' && n.marks?.some(m => m.type === 'link')) counts.links += 1;
    walk(n.content, counts);
}
