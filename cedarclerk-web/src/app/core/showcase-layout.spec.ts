import {
    defaultShowcaseLayout, normalizeShowcaseLayout, parseShowcaseLayout, serializeShowcaseLayout,
} from './showcase-layout';

describe('Showcase layout', () => {
    it('keeps the compatible default and makes hero the first block', () => {
        const layout = defaultShowcaseLayout();

        expect(layout.blocks[0].kind).toBe('hero');
        expect(layout.blocks.some(block => block.kind === 'devlog')).toBe(true);
        expect(layout.blocks.some(block => block.kind === 'about')).toBe(false);
    });

    it('drops unknown and duplicate kinds and restores hero', () => {
        const layout = normalizeShowcaseLayout({
            blocks: [
                { kind: 'about', visible: true, title: ' About ', body: 'Copy' },
                { kind: 'about', visible: false },
                { kind: 'script', visible: true },
            ],
        });

        expect(layout.blocks.map(block => block.kind)).toEqual(['hero', 'about']);
        expect(layout.blocks[1].title).toBe('About');
    });

    it('round-trips a safe, bounded document', () => {
        const json = serializeShowcaseLayout([
            { id: 'hero', kind: 'hero', visible: false, title: null, body: 'Intro' },
            { id: 'about', kind: 'about', visible: false, title: 'Story', body: 'Body' },
        ]);
        const layout = parseShowcaseLayout(json);

        expect(layout.blocks[0].visible).toBe(true);
        expect(layout.blocks[1]).toMatchObject({ kind: 'about', visible: false, title: 'Story', body: 'Body' });
    });
});
