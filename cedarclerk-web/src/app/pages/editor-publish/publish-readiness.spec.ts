import { DOCUMENT_KINDS, DocumentKindCounts } from '../../core/document-kinds';
import { PublishCapabilities } from '../../core/publish.service';
import { MatrixWords } from '../../shared/publish-matrix.component';
import { matrixWarningCount } from './publish-readiness';

const WORDS: MatrixWords = {
    asText: 'as plain text',
    teaser: n => `teaser, ${n}`,
    teaserOrThread: n => `teaser or thread, ${n}`,
    blogLinkOnly: 'the blog link only',
    linkCard: 'the link card carries one',
    upTo: n => `up to ${n}`,
    upToFirst: n => `up to ${n}, first post`,
};

function caps(network: string, over: Partial<PublishCapabilities> = {}): PublishCapabilities {
    return {
        network, maxCharacters: null, maxMediaItems: 10, maxImageBytes: null,
        supportsVideo: true, supportsAudio: true, supportsRichText: true, supportsHeadings: true, supportsLists: true,
        supportsTables: true, supportsCodeBlocks: true, supportsMath: true, supportsLinkPreview: true, supportsAltText: true,
        supportsThreads: false, postsHavePublicUrls: true, ...over,
    };
}

function present(over: Partial<DocumentKindCounts>): DocumentKindCounts {
    return { ...Object.fromEntries(DOCUMENT_KINDS.map(k => [k, 0])) as DocumentKindCounts, ...over };
}

describe('matrixWarningCount', () => {
    const x = caps('x', { maxCharacters: 280, maxMediaItems: 4, supportsHeadings: false, supportsRichText: false, supportsThreads: true, supportsTables: false, supportsCodeBlocks: false, supportsMath: false });
    const telegram = caps('telegram', { maxCharacters: 4096, maxMediaItems: 1000, supportsTables: false, supportsMath: false, supportsHeadings: false });

    it('counts only the kinds the document holds, on the destinations that are ticked', () => {
        const doc = present({ text: 3, images: 2, headings: 1 });
        // X: text with limits, pictures up to 4, headings as text — three. Telegram: text with a
        // limit, headings as text — two. Pictures go as is on Telegram.
        expect(matrixWarningCount(doc, [x, telegram], ['x', 'telegram'], WORDS)).toBe(5);
        expect(matrixWarningCount(doc, [x, telegram], ['telegram'], WORDS)).toBe(2);
        expect(matrixWarningCount(doc, [x, telegram], [], WORDS)).toBe(0);
    });

    it('is zero for a document whose kinds every ticked destination takes as they are', () => {
        expect(matrixWarningCount(present({ images: 1, lists: 2 }), [telegram], ['telegram'], WORDS)).toBe(0);
    });

    it('ignores a ticked destination with no capability record', () => {
        expect(matrixWarningCount(present({ text: 1 }), [x], ['blog', 'bluesky'], WORDS)).toBe(0);
    });
});
