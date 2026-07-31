import { plainTextOf } from './cedar-text.util';

describe('plainTextOf', () => {
    it('returns one line per top-level block', () => {
        const doc = JSON.stringify({
            type: 'doc',
            content: [
                { type: 'paragraph', content: [{ type: 'text', text: 'first' }] },
                { type: 'paragraph', content: [{ type: 'text', text: 'second' }] },
            ],
        });
        expect(plainTextOf(doc)).toBe('first\nsecond');
    });

    it('joins the marked runs of one paragraph into one line', () => {
        const doc = JSON.stringify({
            type: 'doc',
            content: [{
                type: 'paragraph',
                content: [
                    { type: 'text', text: 'bold ' },
                    { type: 'text', text: 'and plain', marks: [{ type: 'bold' }] },
                ],
            }],
        });
        expect(plainTextOf(doc)).toBe('bold and plain');
    });

    it('skips blocks that carry no words', () => {
        const doc = JSON.stringify({
            type: 'doc',
            content: [
                { type: 'paragraph', content: [{ type: 'text', text: 'kept' }] },
                { type: 'horizontalRule' },
                { type: 'paragraph' },
            ],
        });
        expect(plainTextOf(doc)).toBe('kept');
    });

    it('is empty rather than throwing on content it cannot parse', () => {
        expect(plainTextOf('not json')).toBe('');
        expect(plainTextOf('')).toBe('');
    });
});
