// What the inspector may say about the selected block (ADR-159 clause 6).
//
// Every field here is read off the node's own attributes. Resolution, byte size and the originating
// file are properties of the file, not of the document, so they are never copied onto a node — a
// stored copy goes stale the moment the bytes change. The node carries `assetId` and the shelf asks
// the server for the rest (ADR-238 clause 3); a node with neither an id nor a resolvable media path
// gets no such rows at all, since an empty row would assert the property exists and is unfilled.

export type SelectionKind =
    | 'image' | 'video' | 'audio' | 'youtube' | 'carousel' | 'collage'
    | 'table' | 'codeBlock' | 'poll' | 'toggle' | 'annotation' | 'footnote'
    | 'wikilink' | 'datetime' | 'block';

export interface NodeLike {
    typeName: string;
    attrs: Record<string, unknown>;
    /** Direct children, for the containers that are described by how many they hold. */
    childCount?: number;
}

export interface SelectionSpec {
    kind: SelectionKind;
    /** The raw ProseMirror type name, so an unmapped node still names itself. */
    typeName: string;
    /** File name of the source, path and query stripped — the whole URL never fits the column. */
    source?: string;
    alt?: string;
    caption?: string;
    /** Frames in a gallery, options in a poll, rows in a table. */
    count?: number;
    /** Free-text summary line: a poll question, a toggle's summary, a footnote's text. */
    text?: string;
    /** The one property the document can be wrong about: media with no alt text. */
    altMissing?: boolean;
    /** ADR-238 — the Asset this media came from, when the node was inserted knowing it. */
    assetId?: string;
}

const KINDS: Record<string, SelectionKind> = {
    image: 'image', video: 'video', audio: 'audio', youtube: 'youtube',
    carousel: 'carousel', collage: 'collage', table: 'table', codeBlock: 'codeBlock',
    poll: 'poll', toggle: 'toggle', annotation: 'annotation', footnote: 'footnote',
    wikilink: 'wikilink', datetime: 'datetime',
};

function str(value: unknown): string | undefined {
    return typeof value === 'string' && value !== '' ? value : undefined;
}

/** The last path segment of a URL or a data URI's type, whichever the source turns out to be. */
export function fileNameOf(src: string): string {
    if (src.startsWith('data:')) return src.slice(5, src.indexOf(';') === -1 ? 5 : src.indexOf(';')) || 'data';
    const withoutQuery = src.split(/[?#]/)[0];
    const tail = withoutQuery.split('/').filter(Boolean).pop();
    return tail || withoutQuery;
}

/**
 * The `Asset.LocalPath` behind a media src, or null when the src is not one of ours. This is what
 * lets a document written before `assetId` existed still answer: `/media/{path}` is the shape the
 * library has always inserted, so the path is a second key into the same row.
 */
export function mediaPathOf(src: string | undefined | null): string | null {
    if (!src) return null;
    const withoutQuery = src.split(/[?#]/)[0];
    const at = withoutQuery.indexOf('/media/');
    if (at === -1) return null;
    return withoutQuery.slice(at + '/media/'.length) || null;
}

export function describeSelection(node: NodeLike | null): SelectionSpec | null {
    if (!node) return null;

    const spec: SelectionSpec = { kind: KINDS[node.typeName] ?? 'block', typeName: node.typeName };
    const src = str(node.attrs['src']);
    if (src) spec.source = fileNameOf(src);
    const assetId = str(node.attrs['assetId']);
    if (assetId) spec.assetId = assetId;
    const caption = str(node.attrs['caption']);
    if (caption) spec.caption = caption;

    switch (spec.kind) {
        case 'image':
        case 'video': {
            spec.alt = str(node.attrs['alt']);
            // Only the two that reach a reader as a bare element: Telegram and the blog both emit
            // the alt attribute, so an empty one is a real defect and not a style preference.
            spec.altMissing = !spec.alt;
            break;
        }
        case 'audio':
            spec.text = str(node.attrs['title']);
            break;
        case 'youtube':
            spec.source = str(node.attrs['videoId']);
            break;
        case 'carousel':
        case 'collage':
            spec.count = Array.isArray(node.attrs['images']) ? node.attrs['images'].length : 0;
            break;
        case 'poll': {
            spec.text = str(node.attrs['question']);
            spec.count = Array.isArray(node.attrs['options'])
                ? node.attrs['options'].filter(o => typeof o === 'string' && o !== '').length
                : 0;
            break;
        }
        case 'toggle':
            spec.text = str(node.attrs['summary']);
            break;
        case 'footnote':
            spec.text = str(node.attrs['text']);
            break;
        case 'wikilink':
            spec.text = str(node.attrs['label']);
            break;
        case 'table':
            spec.count = node.childCount ?? 0;
            break;
    }

    return spec;
}
