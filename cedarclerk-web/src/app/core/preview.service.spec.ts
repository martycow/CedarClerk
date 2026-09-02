import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PreviewService, TelegramPreview } from './preview.service';

const PREVIEW: TelegramPreview = {
    language: 'ru', messageCount: 1, characters: 12, maxCharactersPerMessage: 3000, maxMediaPerMessage: 10,
    messages: [{
        index: 0, characters: 12, mediaCount: 0, cutReason: 'end', startsWith: 'Hello there',
        blocks: [{ kind: 'paragraph', text: 'Hello there.', urls: [], caption: null }],
    }],
    buttons: [],
};

describe('PreviewService', () => {
    let api: PreviewService;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        api = TestBed.inject(PreviewService);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    it('asks the telegram projection for one language', async () => {
        const answer = api.telegram('d1', 'en');
        const req = http.expectOne(r => r.url === '/api/drafts/d1/preview/telegram');
        expect(req.request.method).toBe('GET');
        expect(req.request.params.get('lang')).toBe('en');
        req.flush(PREVIEW);
        expect(await answer).toEqual(PREVIEW);
    });

    it('builds the blog page address with and without a theme', () => {
        expect(api.blogUrl('d1', 'ru', null)).toBe('/api/drafts/d1/preview/blog?lang=ru');
        expect(api.blogUrl('d1', 'ru', 'dark')).toBe('/api/drafts/d1/preview/blog?lang=ru&theme=dark');
    });

    // The frame is fully sandboxed, so the page is fetched here — where the cookie travels — and
    // handed over as text rather than navigated to.
    it('fetches the blog page as text', async () => {
        const answer = api.blogHtml('d1', 'ru', 'light');
        const req = http.expectOne('/api/drafts/d1/preview/blog?lang=ru&theme=light');
        expect(req.request.responseType).toBe('text');
        req.flush('<html><body>page</body></html>');
        expect(await answer).toContain('page');
    });

    it('reads the share link without rotating it, and answers null when there is none', async () => {
        const found = api.previewLink('d1');
        http.expectOne({ method: 'GET', url: '/api/drafts/d1/preview-link' }).flush({ url: 'https://x/preview/t' });
        expect(await found).toEqual({ url: 'https://x/preview/t' });

        const missing = api.previewLink('d2');
        http.expectOne({ method: 'GET', url: '/api/drafts/d2/preview-link' })
            .flush({}, { status: 404, statusText: 'Not Found' });
        expect(await missing).toBeNull();
    });

    it('lets every other failure through', async () => {
        const broken = api.previewLink('d3');
        http.expectOne('/api/drafts/d3/preview-link').flush({}, { status: 500, statusText: 'Server Error' });
        await expect(broken).rejects.toBeTruthy();
    });
});
