import { TestBed } from '@angular/core/testing';
import { LocaleService } from '../../core/i18n/locale.service';
import { TelegramPreview } from '../../core/preview.service';
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
    function create(preview: TelegramPreview | null = PREVIEW) {
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(PreviewPhoneComponent);
        fixture.componentRef.setInput('preview', preview);
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
});
