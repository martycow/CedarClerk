// T-016 — the words of a stored document, for reading a version before deciding to restore it.
// Deliberately not a renderer: the question a version-history preview answers is "is this the
// text I lost", and a second TipTap instance is a lot of machinery to answer it.
export function plainTextOf(cedarJson: string): string {
    let doc: unknown;
    try {
        doc = JSON.parse(cedarJson);
    } catch {
        return '';
    }

    const lines: string[] = [];
    const walk = (node: any, into: string[]) => {
        if (!node || typeof node !== 'object') return;
        if (typeof node.text === 'string') { into.push(node.text); return; }
        if (Array.isArray(node.content)) {
            // Every top-level block becomes its own line — the block boundaries are what make a
            // wall of text readable, and they are exactly what the diff counts.
            const isBlock = node.type !== 'doc' && !INLINE_TYPES.has(node.type);
            const target = isBlock ? [] : into;
            for (const child of node.content) walk(child, target);
            if (isBlock) lines.push(target.join(''));
        }
    };

    const root = doc as any;
    if (Array.isArray(root?.content)) for (const block of root.content) walk(block, lines);
    return lines.filter(line => line.trim().length > 0).join('\n');
}

const INLINE_TYPES = new Set(['text', 'hardBreak']);
