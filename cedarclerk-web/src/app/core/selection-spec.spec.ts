import { NodeLike, describeSelection, fileNameOf } from './selection-spec';

function node(typeName: string, attrs: Record<string, unknown> = {}, childCount?: number): NodeLike {
    return { typeName, attrs, childCount };
}

describe('describeSelection', () => {
    it('answers nothing when nothing is selected, which is what puts the panel on the document', () => {
        expect(describeSelection(null)).toBeNull();
    });

    it('names an unmapped node rather than hiding it', () => {
        const spec = describeSelection(node('blockquote'))!;
        expect(spec.kind).toBe('block');
        expect(spec.typeName).toBe('blockquote');
    });

    it('reads an image from its own attributes and from nowhere else', () => {
        const spec = describeSelection(node('image', {
            src: '/media/2026/fog_pass_02.png?v=17', alt: 'Fog', caption: 'Second pass',
        }))!;
        expect(spec.kind).toBe('image');
        expect(spec.source).toBe('fog_pass_02.png');
        expect(spec.alt).toBe('Fog');
        expect(spec.caption).toBe('Second pass');
        expect(spec.altMissing).toBe(false);
    });

    // The kit's image panel shows a resolution, a byte size and the asset folder. None of the three
    // is on the node, so none of them may appear — not even as an empty row, which would assert the
    // property exists and is unfilled (ADR-159 clause 6).
    it('invents no resolution, size or asset path', () => {
        const spec = describeSelection(node('image', { src: 'a.png', alt: 'x' }))!;
        expect(Object.keys(spec).sort()).toEqual(['alt', 'altMissing', 'kind', 'source', 'typeName']);
    });

    it('flags a missing alt, because that is the one claim the document can be wrong about', () => {
        expect(describeSelection(node('image', { src: 'a.png' }))!.altMissing).toBe(true);
        expect(describeSelection(node('image', { src: 'a.png', alt: '' }))!.altMissing).toBe(true);
        expect(describeSelection(node('video', { src: 'a.mp4' }))!.altMissing).toBe(true);
    });

    it('does not flag an alt on a node that has no alt to give', () => {
        for (const kind of ['audio', 'youtube', 'carousel', 'poll', 'table', 'blockquote']) {
            expect(describeSelection(node(kind, { src: 'a', images: [], options: [] }))!.altMissing)
                .toBeUndefined();
        }
    });

    it('counts a gallery by its frames and a poll by the options that were filled in', () => {
        expect(describeSelection(node('carousel', { images: ['a', 'b', 'c'] }))!.count).toBe(3);
        expect(describeSelection(node('collage', { images: [] }))!.count).toBe(0);
        expect(describeSelection(node('poll', { question: 'Which?', options: ['a', '', 'b'] }))!.count).toBe(2);
        expect(describeSelection(node('poll', { question: 'Which?', options: ['a', '', 'b'] }))!.text).toBe('Which?');
    });

    it('counts a table by its rows, which is what the node holds', () => {
        expect(describeSelection(node('table', {}, 4))!.count).toBe(4);
    });

    it('takes a YouTube video by its id, since it has no file behind it', () => {
        const spec = describeSelection(node('youtube', { videoId: 'dQw4w9WgXcQ' }))!;
        expect(spec.source).toBe('dQw4w9WgXcQ');
    });

    it('carries the one line each text-bearing node has', () => {
        expect(describeSelection(node('toggle', { summary: 'Details' }))!.text).toBe('Details');
        expect(describeSelection(node('footnote', { text: 'See page 4' }))!.text).toBe('See page 4');
        expect(describeSelection(node('wikilink', { label: 'Fog pass' }))!.text).toBe('Fog pass');
        expect(describeSelection(node('audio', { src: 'a.mp3', title: 'Theme' }))!.text).toBe('Theme');
    });

    it('leaves an empty attribute out instead of reporting an empty string', () => {
        const spec = describeSelection(node('toggle', { summary: '' }))!;
        expect(spec.text).toBeUndefined();
    });
});

describe('fileNameOf', () => {
    it('keeps the file and drops the path, the query and the fragment', () => {
        expect(fileNameOf('/media/2026/08/fog.png')).toBe('fog.png');
        expect(fileNameOf('https://cdn.example/a/b/fog.png?v=3#x')).toBe('fog.png');
        expect(fileNameOf('fog.png')).toBe('fog.png');
    });

    it('names a data URI by its type rather than printing the payload', () => {
        expect(fileNameOf('data:image/png;base64,AAAA')).toBe('image/png');
    });
});
