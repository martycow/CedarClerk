import { documentKinds } from './document-kinds';

describe('documentKinds', () => {
    it('counts each kind the document holds, pictures per gallery item and links per marked run', () => {
        const doc = JSON.stringify({ type: 'doc', content: [
            { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'Title' }] },
            { type: 'paragraph', content: [
                { type: 'text', text: 'Read ' },
                { type: 'text', text: 'this', marks: [{ type: 'link', attrs: { href: 'https://a.test' } }] },
            ] },
            { type: 'image', attrs: { src: '/media/a.jpg' } },
            { type: 'carousel', attrs: { images: ['/media/b.jpg', '/media/c.jpg'] } },
            { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'one' }] }] }] },
            { type: 'codeBlock', content: [{ type: 'text', text: 'x' }] },
        ] });
        const counts = documentKinds(doc);
        expect(counts.headings).toBe(1);
        expect(counts.text).toBe(2);
        expect(counts.links).toBe(1);
        expect(counts.images).toBe(3);
        expect(counts.lists).toBe(1);
        expect(counts.code).toBe(1);
        expect(counts.video).toBe(0);
    });

    it('answers zeros for a document that does not parse', () => {
        expect(documentKinds('{oops')).toEqual(expect.objectContaining({ text: 0, images: 0 }));
    });
});
