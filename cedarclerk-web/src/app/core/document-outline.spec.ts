import { OutlineNodeLike, buildOutline, topLevelStart } from './document-outline';

function node(typeName: string, extra: Partial<OutlineNodeLike> = {}): OutlineNodeLike {
    return { typeName, attrs: {}, ...extra };
}

function heading(level: number, text: string): OutlineNodeLike {
    return { typeName: 'heading', attrs: { level }, text };
}

describe('buildOutline', () => {
    it('walks the document in order and keeps each block index', () => {
        const entries = buildOutline([
            heading(1, 'Fog and light'),
            node('paragraph', { text: 'Two weeks of fixing depth.' }),
            node('image', { attrs: { src: '/media/fog_pass_02.png?v=9' }, selectableAtom: true }),
        ]);

        expect(entries.map(e => e.index)).toEqual([0, 1, 2]);
        expect(entries.map(e => e.kind)).toEqual(['heading', 'paragraph', 'image']);
        expect(entries[0].level).toBe(1);
        expect(entries[2].selectableAtom).toBe(true);
    });

    // The kit's writer screen, indent for indent: H1, then its paragraph one step in, then H2 at
    // that same step with its own blocks one deeper.
    it('indents from the nearest heading above, since the document has no tree to walk', () => {
        const entries = buildOutline([
            heading(1, 'Fog and light'),
            node('paragraph', { text: 'Lead' }),
            heading(2, 'Why fog'),
            node('paragraph', { text: 'Body' }),
            node('image', { attrs: { src: 'fog.png' } }),
        ]);

        expect(entries.map(e => e.indent)).toEqual([0, 1, 1, 2, 2]);
    });

    it('clamps the indent at two steps however deep the headings go', () => {
        const entries = buildOutline([heading(5, 'Deep'), node('paragraph', { text: 'Under it' })]);
        expect(entries.map(e => e.indent)).toEqual([2, 2]);
    });

    it('leaves a block before any heading at the outer edge', () => {
        const entries = buildOutline([node('paragraph', { text: 'Opening line' })]);
        expect(entries[0].indent).toBe(0);
    });

    it('names a media block by its file rather than by its type', () => {
        const [image, video, embed] = buildOutline([
            node('image', { attrs: { src: '/media/2026/fog_pass_02.png?v=17' } }),
            node('video', { attrs: { src: 'https://cdn.example/clip.mp4' } }),
            node('youtube', { attrs: { videoId: 'dQw4w9WgXcQ' } }),
        ]);

        expect(image.text).toBe('fog_pass_02.png');
        expect(video.text).toBe('clip.mp4');
        expect(embed.text).toBe('dQw4w9WgXcQ');
    });

    it('counts what is described by how much it holds, and nothing else', () => {
        const [list, gallery, table, paragraph] = buildOutline([
            node('bulletList', { childCount: 3, text: 'one two three' }),
            node('carousel', { attrs: { images: ['a.png', 'b.png', 'c.png', 'd.png'] } }),
            node('table', { childCount: 4 }),
            node('paragraph', { text: 'plain' }),
        ]);

        expect(list.count).toBe(3);
        expect(gallery.count).toBe(4);
        expect(table.count).toBe(4);
        expect(paragraph.count).toBeUndefined();
    });

    it('flattens and clips a paragraph so one block cannot carry a page into the column', () => {
        const [entry] = buildOutline([node('paragraph', { text: '  fog\n  and   light  ' })]);
        expect(entry.text).toBe('fog and light');

        const [long] = buildOutline([node('paragraph', { text: 'x'.repeat(400) })]);
        expect(long.text.endsWith('…')).toBe(true);
        expect(long.text.length).toBeLessThanOrEqual(121);
    });

    it('leaves a block with no words of its own unlabelled, for the panel to name by kind', () => {
        const [entry] = buildOutline([node('paragraph', { text: '' })]);
        expect(entry.text).toBe('');
        expect(entry.kind).toBe('paragraph');
    });

    it('falls back to a generic block for a node type it does not know', () => {
        const [entry] = buildOutline([node('someFutureNode', { text: 'whatever' })]);
        expect(entry.kind).toBe('block');
        expect(entry.icon).toBe('file');
    });

    it('gives an empty document an empty outline', () => {
        expect(buildOutline([])).toEqual([]);
    });
});

describe('topLevelStart', () => {
    it('sums the sizes of every block before the one asked for', () => {
        const sizes = [12, 4, 30];
        expect(topLevelStart(sizes, 0)).toBe(0);
        expect(topLevelStart(sizes, 1)).toBe(12);
        expect(topLevelStart(sizes, 2)).toBe(16);
    });

    // The list is debounced, so a row on screen can outlive the block it describes. Answering null
    // is what keeps a click in that window from resolving a position the document no longer has.
    it('refuses an index the document no longer reaches', () => {
        expect(topLevelStart([12, 4], 2)).toBeNull();
        expect(topLevelStart([12, 4], -1)).toBeNull();
        expect(topLevelStart([], 0)).toBeNull();
    });
});
