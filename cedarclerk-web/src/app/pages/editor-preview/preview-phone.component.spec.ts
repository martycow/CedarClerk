import { TestBed } from '@angular/core/testing';
import { LocaleService } from '../../core/i18n/locale.service';
import { PreviewTheme, TelegramPreview } from '../../core/preview.service';
import { PreviewPhoneComponent } from './preview-phone.component';

// Two messages the way the splitter hands them over: a heading cut, a photo with a caption, and
// the CTA row on the last one only.
const PREVIEW: TelegramPreview = {
    language: 'ru', messageCount: 2, characters: 60, maxCharactersPerMessage: 3000, maxMediaPerMessage: 10,
    messages: [
        {
            index: 0, characters: 40, mediaCount: 1, cutReason: 'heading', startsWith: 'Coyote vs ACME',
            blocks: [
                { kind: 'heading', text: 'Coyote vs ACME', urls: [], caption: null },
                { kind: 'paragraph', text: 'Сегодня сходили на фильм.', urls: [], caption: null },
                { kind: 'photo', text: '', urls: ['/media/2026/08/coyote.jpg'], caption: 'Все кадры с койотом' },
            ],
        },
        {
            index: 1, characters: 20, mediaCount: 0, cutReason: 'end', startsWith: 'Мой совет',
            blocks: [{ kind: 'paragraph', text: 'Мой совет — идти в кино.', urls: [], caption: null }],
        },
    ],
    buttons: [{ text: 'Read on the blog', url: 'https://example.test/coyote' }],
};

describe('PreviewPhoneComponent', () => {
    function create(preview: TelegramPreview | null = PREVIEW, theme: PreviewTheme = 'light') {
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(PreviewPhoneComponent);
        fixture.componentRef.setInput('preview', preview);
        fixture.componentRef.setInput('theme', theme);
        fixture.componentRef.setInput('channelTitle', 'Dev Dairy Diary');
        fixture.componentRef.setInput('channelHandle', 'devdairydiary');
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    it('draws one bubble per message of the DTO — the count is the server’s, never a split of its own', () => {
        const el = create();
        expect(el.querySelectorAll('.bubble').length).toBe(2);
        expect(el.querySelector('.bubble')!.getAttribute('aria-label')).toBe('Message 1 of 2');
    });

    it('keeps the blocks in order, with media as a placeholder over the relative URL and its caption under it', () => {
        const el = create();
        const first = el.querySelector('.bubble')!;
        const blocks = Array.from(first.children).map(c => c.className.split(' ')[0]);
        expect(blocks.slice(0, 3)).toEqual(['bubble-block', 'bubble-block', 'bubble-media']);
        expect(first.querySelector('.bubble-block.is-heading')!.textContent!.trim()).toBe('Coyote vs ACME');
        expect(first.querySelector('.bubble-tile img')!.getAttribute('src')).toBe('/media/2026/08/coyote.jpg');
        expect(first.querySelector('.bubble-caption')!.textContent!.trim()).toBe('Все кадры с койотом');
    });

    it('hangs the CTA buttons under the last bubble only', () => {
        const el = create();
        const bubbles = el.querySelectorAll('.bubble');
        expect(bubbles[0].querySelector('.bubble-buttons')).toBeNull();
        expect(bubbles[1].querySelector('.bubble-button')!.textContent!.trim()).toBe('Read on the blog');
    });

    it('prints each bubble’s characters against the real per-message limit and names the cut', () => {
        const el = create();
        const metas = Array.from(el.querySelectorAll('.bubble-meta')).map(m => m.textContent!.replace(/\s+/g, ' ').trim());
        expect(metas[0]).toBe('40 / 3,000 · cut at a heading');
        expect(metas[1]).toBe('20 / 3,000 · end of post');
    });

    it('says the document is empty when there are no messages', () => {
        const el = create({ ...PREVIEW, messageCount: 0, characters: 0, messages: [], buttons: [] });
        expect(el.querySelectorAll('.bubble').length).toBe(0);
        expect(el.querySelector('.phone-empty')!.textContent).toContain('empty');
    });

    // ADR-313 / #5 — the toolbar's theme reaches the phone, which has its own Telegram palette.
    it('follows the theme input with its own palette attribute', () => {
        const light = TestBed.createComponent(PreviewPhoneComponent);
        light.detectChanges();
        expect((light.nativeElement as HTMLElement).getAttribute('data-tg-theme')).toBe('light');

        const dark = TestBed.createComponent(PreviewPhoneComponent);
        dark.componentRef.setInput('theme', 'dark');
        dark.detectChanges();
        expect((dark.nativeElement as HTMLElement).getAttribute('data-tg-theme')).toBe('dark');
    });

    // ADR-313 / #7 — bullets, numbers and checkboxes reach the phone as a list, not as bare lines.
    it('draws a bullet, a numbered and a task list with their own markers', () => {
        const el = create({
            ...PREVIEW, messageCount: 1,
            messages: [{
                index: 0, characters: 30, mediaCount: 0, cutReason: 'end', startsWith: '',
                blocks: [
                    { kind: 'list', text: 'a\nb', urls: [], caption: null, items: [
                        { text: 'a', order: null, hasCheckbox: false, isChecked: false },
                        { text: 'b', order: null, hasCheckbox: false, isChecked: false }] },
                    { kind: 'list', text: 'x\ny', urls: [], caption: null, items: [
                        { text: 'x', order: 1, hasCheckbox: false, isChecked: false },
                        { text: 'y', order: 2, hasCheckbox: false, isChecked: false }] },
                    { kind: 'list', text: 'done\nnot', urls: [], caption: null, items: [
                        { text: 'done', order: null, hasCheckbox: true, isChecked: true },
                        { text: 'not', order: null, hasCheckbox: true, isChecked: false }] },
                ],
            }],
            buttons: [],
        });

        expect(el.querySelectorAll('ul.bubble-list').length).toBe(2);
        expect(el.querySelectorAll('ol.bubble-list').length).toBe(1);
        const markers = Array.from(el.querySelectorAll('.bubble-marker')).map(m => m.textContent!.trim());
        expect(markers).toEqual(['•', '•', '1.', '2.']);
        const checks = Array.from(el.querySelectorAll('.bubble-check'));
        expect(checks.map(c => c.classList.contains('is-on'))).toEqual([true, false]);
    });

    it('falls back to plain text for a list the server sent without items', () => {
        const el = create({
            ...PREVIEW, messageCount: 1,
            messages: [{
                index: 0, characters: 3, mediaCount: 0, cutReason: 'end', startsWith: '',
                blocks: [{ kind: 'list', text: 'one\ntwo', urls: [], caption: null }],
            }],
            buttons: [],
        });
        expect(el.querySelector('ul.bubble-list, ol.bubble-list')).toBeNull();
        expect(el.querySelector('p.bubble-block[data-kind="list"]')!.textContent).toContain('one');
    });

    // #7 — every image the post sends is shown; past four, the rest are counted, not dropped.
    it('counts the images past four on the last tile and does not label a lone photo', () => {
        const urls = ['1', '2', '3', '4', '5', '6'].map(n => `/media/${n}.jpg`);
        const el = create({
            ...PREVIEW, messageCount: 1,
            messages: [{
                index: 0, characters: 0, mediaCount: 7, cutReason: 'end', startsWith: '',
                blocks: [
                    { kind: 'photo', text: '', urls: ['/media/p.jpg'], caption: null },
                    { kind: 'slideshow', text: '', urls, caption: null },
                ],
            }],
            buttons: [],
        });

        const [photo, gallery] = Array.from(el.querySelectorAll('.bubble-media'));
        expect(photo.querySelector('.bubble-media-kind')).toBeNull();
        expect(gallery.querySelectorAll('.bubble-tile').length).toBe(4);
        expect(gallery.querySelector('.bubble-more')!.textContent!.trim()).toBe('+2');
        expect(gallery.querySelector('.bubble-media-kind')!.textContent!.trim()).toBe('6 photos');
    });

    // ADR-313 / #3 — one long message gets a fold marker where Telegram's client hides the rest.
    it('marks where the client folds a long single message, and says so in the numbers', () => {
        const el = create({
            ...PREVIEW, messageCount: 1, characters: 4000, maxCharactersPerMessage: 32768, foldAfterCharacters: 3000,
            messages: [{
                index: 0, characters: 4000, mediaCount: 0, cutReason: 'end', startsWith: '',
                blocks: [
                    { kind: 'paragraph', text: 'x'.repeat(3000), urls: [], caption: null },
                    { kind: 'paragraph', text: 'y'.repeat(1000), urls: [], caption: null },
                ],
            }],
            buttons: [],
        });

        const children = Array.from(el.querySelector('.bubble')!.children).map(c => c.className.split(' ')[0]);
        expect(children.slice(0, 3)).toEqual(['bubble-block', 'bubble-fold', 'bubble-block']);
        expect(el.querySelector('.bubble-fold')!.textContent!.trim()).toBe('Show more');
        expect(el.querySelector('.bubble-meta')!.textContent!.replace(/\s+/g, ' ').trim())
            .toBe('4,000 / 32,768 · Telegram folds after 3,000 · end of post');
    });

    it('draws no fold under the limit', () => {
        const el = create({ ...PREVIEW, foldAfterCharacters: 3000 });
        expect(el.querySelector('.bubble-fold')).toBeNull();
        expect(el.querySelector('.bubble-meta')!.textContent).not.toContain('folds');
    });
});
