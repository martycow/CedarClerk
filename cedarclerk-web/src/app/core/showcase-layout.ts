export type ShowcaseBlockKind =
    'hero' | 'about' | 'links' | 'trailer' | 'gallery' | 'downloads' | 'devlog' | 'follow' | 'roadmap';

export interface ShowcaseBlock {
    id: string;
    kind: ShowcaseBlockKind;
    visible: boolean;
    title: string | null;
    body: string | null;
}

export interface ShowcaseLayoutDocument {
    version: 1;
    blocks: ShowcaseBlock[];
}

export const SHOWCASE_BLOCK_KINDS: readonly ShowcaseBlockKind[] = [
    'hero', 'about', 'links', 'trailer', 'gallery', 'downloads', 'devlog', 'follow', 'roadmap',
];

const DEFAULT_BLOCKS: readonly ShowcaseBlockKind[] = [
    'hero', 'links', 'trailer', 'gallery', 'downloads', 'devlog', 'follow', 'roadmap',
];

export function defaultShowcaseLayout(): ShowcaseLayoutDocument {
    return { version: 1, blocks: DEFAULT_BLOCKS.map(newShowcaseBlock) };
}

export function newShowcaseBlock(kind: ShowcaseBlockKind): ShowcaseBlock {
    return { id: kind, kind, visible: true, title: null, body: null };
}

export function normalizeShowcaseLayout(value: unknown): ShowcaseLayoutDocument {
    if (!value || typeof value !== 'object' || !Array.isArray((value as { blocks?: unknown }).blocks))
        return defaultShowcaseLayout();

    const seen = new Set<ShowcaseBlockKind>();
    const blocks: ShowcaseBlock[] = [];
    for (const candidate of (value as { blocks: unknown[] }).blocks) {
        if (!candidate || typeof candidate !== 'object') continue;
        const raw = candidate as Partial<ShowcaseBlock>;
        if (!SHOWCASE_BLOCK_KINDS.includes(raw.kind as ShowcaseBlockKind) || seen.has(raw.kind as ShowcaseBlockKind))
            continue;
        const kind = raw.kind as ShowcaseBlockKind;
        seen.add(kind);
        blocks.push({
            id: kind,
            kind,
            visible: kind === 'hero' || raw.visible !== false,
            title: bounded(raw.title, 120),
            body: bounded(raw.body, 2000),
        });
    }
    if (!blocks.length) return defaultShowcaseLayout();
    if (!seen.has('hero')) blocks.unshift(newShowcaseBlock('hero'));
    return { version: 1, blocks };
}

export function parseShowcaseLayout(json: string | null | undefined): ShowcaseLayoutDocument {
    if (!json?.trim()) return defaultShowcaseLayout();
    try { return normalizeShowcaseLayout(JSON.parse(json)); }
    catch { return defaultShowcaseLayout(); }
}

export function serializeShowcaseLayout(blocks: ShowcaseBlock[]): string {
    return JSON.stringify(normalizeShowcaseLayout({ blocks }));
}

function bounded(value: unknown, max: number): string | null {
    if (typeof value !== 'string' || !value.trim()) return null;
    return value.trim().slice(0, max);
}
