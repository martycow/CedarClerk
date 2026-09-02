import { TestBed } from '@angular/core/testing';
import { LocaleService } from '../../core/i18n/locale.service';
import { MicroPreview } from '../../core/preview.service';
import { MicroMode, PreviewPostComponent } from './preview-post.component';

// The projection the way the server hands it over: one announcement with the blog link, and a
// three-part thread whose first part carries the picture and whose last carries the link.
const URL = 'https://blog.test/coyote';
const PREVIEW: MicroPreview = {
    network: 'bluesky', language: 'en', maxLength: 300, hasAuthorText: false, supportsThreads: true, blogUrl: URL,
    single: { index: 0, text: `Coyote vs ACME opens tonight.\n${URL}`, length: 52, imageUrls: ['/media/coyote.jpg'], linkUrl: URL },
    thread: [
        { index: 0, text: 'Coyote vs ACME opens tonight.\n\n1/3', length: 33, imageUrls: ['/media/coyote.jpg'], linkUrl: null },
        { index: 1, text: 'Bring popcorn.\n\n2/3', length: 18, imageUrls: [], linkUrl: null },
        { index: 2, text: `3/3\n\n${URL}`, length: 30, imageUrls: [], linkUrl: URL },
    ],
};

describe('PreviewPostComponent', () => {
    function create(preview: MicroPreview | null = PREVIEW, mode: MicroMode = 'single') {
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(PreviewPostComponent);
        fixture.componentRef.setInput('preview', preview);
        fixture.componentRef.setInput('mode', mode);
        fixture.componentRef.setInput('accountName', 'martycow.bsky.social');
        fixture.componentRef.setInput('linkTitle', 'Coyote vs ACME');
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    it('draws the announcement as one post with the link pulled out of the text and the picture under it', () => {
        const el = create();
        expect(el.querySelectorAll('.post').length).toBe(1);
        expect(el.querySelector('.post-text')!.textContent).toBe('Coyote vs ACME opens tonight.\nblog.test/coyote');
        expect(el.querySelector('.post-link')!.textContent).toBe('blog.test/coyote');
        expect(el.querySelector('.post-image img')!.getAttribute('src')).toBe('/media/coyote.jpg');
        expect(el.querySelector('.post-meta')!.textContent!.replace(/\s+/g, ' ').trim()).toBe('52 / 300');
    });

    it('draws the thread as the server’s parts, pictures on the first and the link on the last', () => {
        const el = create(PREVIEW, 'thread');
        const posts = el.querySelectorAll('.post');
        expect(posts.length).toBe(3);
        expect(posts[0].getAttribute('aria-label')).toBe('Post 1 of 3');
        expect(posts[0].querySelector('.post-image')).not.toBeNull();
        expect(posts[1].querySelector('.post-image')).toBeNull();
        expect(posts[2].querySelector('.post-link')!.textContent).toBe('blog.test/coyote');
        expect(posts[0].querySelector('.post-line')).not.toBeNull();
        expect(posts[2].querySelector('.post-line')).toBeNull();
    });

    it('unfurls the link into a card on X but never on Bluesky, and never over a picture', () => {
        expect(create().querySelector('.post-card')).toBeNull();
        const x = create({ ...PREVIEW, network: 'x', single: { ...PREVIEW.single, imageUrls: [] } });
        expect(x.querySelector('.post-card-host')!.textContent).toBe('blog.test');
        expect(x.querySelector('.post-card-title')!.textContent).toBe('Coyote vs ACME');
    });

    it('marks a post over the network’s limit', () => {
        const el = create({ ...PREVIEW, single: { ...PREVIEW.single, length: 301 } });
        expect(el.querySelector('.post-meta .is-over')).not.toBeNull();
    });

    it('says there is nothing to post when the announcement is blank', () => {
        const el = create({ ...PREVIEW, single: { ...PREVIEW.single, text: '  ', linkUrl: null } });
        expect(el.querySelectorAll('.post').length).toBe(0);
        expect(el.querySelector('.post-empty')!.textContent).toContain('Nothing to post');
    });
});
