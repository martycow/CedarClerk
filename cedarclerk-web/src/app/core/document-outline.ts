// The writer's structure shelf, as a pure function over the document's top-level children
// (ADR-162). Nothing here knows about ProseMirror: the editor hands over a plain shape so the
// arithmetic and the labelling can be tested without a schema.

import { IconName } from '../shared/icon-data.generated';
import { fileNameOf } from './selection-spec';

export type OutlineKind =
    | 'heading' | 'paragraph' | 'list' | 'quote' | 'code' | 'divider'
    | 'image' | 'video' | 'audio' | 'gallery' | 'embed' | 'table' | 'block';

export interface OutlineNodeLike {
    typeName: string;
    attrs: Record<string, unknown>;
    /** Direct children — items in a list, rows in a table. */
    childCount?: number;
    /** The node's own words, if it has any. */
    text?: string;
    /** True for a node the schema will hand a NodeSelection (ADR-162 clause 4). */
    selectableAtom?: boolean;
}

export interface OutlineEntry {
    /** Index among the document's top-level children — the same number the rule counts. */
    index: number;
    kind: OutlineKind;
    icon: IconName;
    /** Steps of indent, 0–2. Read off the nearest heading above, never off the tree. */
    indent: number;
    /** Heading level, 1–6. Absent on everything else. */
    level?: number;
    /** The node's own words, clipped. Empty when the block has none to give. */
    text: string;
    /** Items the block holds, when holding them is what describes it. */
    count?: number;
    selectableAtom: boolean;
}

/** Long enough to tell two paragraphs apart, short enough that the column never carries a page. */
const LABEL_MAX = 120;
const MAX_INDENT = 2;

const KINDS: Record<string, OutlineKind> = {
    heading: 'heading', paragraph: 'paragraph',
    bulletList: 'list', orderedList: 'list', taskList: 'list',
    blockquote: 'quote', codeBlock: 'code', horizontalRule: 'divider',
    image: 'image', video: 'video', audio: 'audio',
    carousel: 'gallery', collage: 'gallery',
    youtube: 'embed', poll: 'embed', toggle: 'embed', tableOfContents: 'embed',
    table: 'table',
};

const ICONS: Record<OutlineKind, IconName> = {
    heading: 'text-h', paragraph: 'text-aa', list: 'list-bullets', quote: 'quotes',
    code: 'code-block', divider: 'list-dashes', image: 'image', video: 'video-camera',
    audio: 'waveform', gallery: 'images', embed: 'cube', table: 'table', block: 'file',
};

/** Kinds described by how many they hold rather than by what they say. */
const COUNTED = new Set<OutlineKind>(['list', 'gallery', 'table']);

function clip(text: string): string {
    const flat = text.replace(/\s+/g, ' ').trim();
    return flat.length > LABEL_MAX ? flat.slice(0, LABEL_MAX).trimEnd() + '…' : flat;
}

function headingLevel(node: OutlineNodeLike): number {
    const level = node.attrs['level'];
    return typeof level === 'number' && level >= 1 && level <= 6 ? level : 1;
}

function countOf(kind: OutlineKind, node: OutlineNodeLike): number | undefined {
    if (!COUNTED.has(kind)) return undefined;
    if (kind === 'gallery') return Array.isArray(node.attrs['images']) ? node.attrs['images'].length : 0;
    return node.childCount ?? 0;
}

/** A media block names itself by its file rather than by its type. */
function mediaLabel(node: OutlineNodeLike): string {
    const src = node.attrs['src'];
    if (typeof src === 'string' && src !== '') return fileNameOf(src);
    const videoId = node.attrs['videoId'];
    return typeof videoId === 'string' ? videoId : '';
}

export function buildOutline(nodes: OutlineNodeLike[]): OutlineEntry[] {
    let lastHeading = 0;
    return nodes.map((node, index) => {
        const kind = KINDS[node.typeName] ?? 'block';
        const level = kind === 'heading' ? headingLevel(node) : undefined;
        if (level !== undefined) lastHeading = level;

        const media = kind === 'image' || kind === 'video' || kind === 'audio' || kind === 'embed';
        const entry: OutlineEntry = {
            index,
            kind,
            icon: ICONS[kind],
            indent: Math.min(level !== undefined ? level - 1 : lastHeading, MAX_INDENT),
            text: clip(media ? mediaLabel(node) : node.text ?? ''),
            selectableAtom: node.selectableAtom === true,
        };
        if (level !== undefined) entry.level = level;
        const count = countOf(kind, node);
        if (count !== undefined) entry.count = count;
        return entry;
    });
}

/**
 * Where the document's `index`-th top-level child begins, in ProseMirror positions. Null when the
 * index is past the end — the list is debounced (ADR-162 clause 3), so an entry on screen can
 * outlive the block it describes, and resolving a position that no longer exists throws.
 */
export function topLevelStart(sizes: number[], index: number): number | null {
    if (index < 0 || index >= sizes.length) return null;
    let pos = 0;
    for (let i = 0; i < index; i++) pos += sizes[i];
    return pos;
}
