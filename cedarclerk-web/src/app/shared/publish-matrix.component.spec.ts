import { TestBed } from '@angular/core/testing';
import { documentKinds } from '../core/document-kinds';
import { LocaleService } from '../core/i18n/locale.service';
import { PublishCapabilities } from '../core/publish.service';
import { PublishMatrixComponent } from './publish-matrix.component';

const X: PublishCapabilities = {
    network: 'x', maxCharacters: 280, maxMediaItems: 4, maxImageBytes: 5242880,
    supportsVideo: false, supportsAudio: false, supportsRichText: false, supportsHeadings: false, supportsLists: false,
    supportsTables: false, supportsCodeBlocks: false, supportsMath: false, supportsLinkPreview: true, supportsAltText: false,
    supportsThreads: true, postsHavePublicUrls: true,
};
const DISCORD: PublishCapabilities = { ...X, network: 'discord', maxCharacters: 2000, maxMediaItems: 0, supportsThreads: false };

describe('PublishMatrixComponent', () => {
    function create(present = documentKinds('{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]},{"type":"image","attrs":{"src":"/media/a.jpg"}}]}')) {
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(PublishMatrixComponent);
        fixture.componentRef.setInput('capabilities', [DISCORD, X]);
        fixture.componentRef.setInput('connected', ['x']);
        fixture.componentRef.setInput('present', present);
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    function row(el: HTMLElement, kind: string) {
        return [...el.querySelectorAll('tbody tr')].find(r => r.querySelector('.pm-kind')!.textContent!.includes(kind))!;
    }

    it('draws the blog first, then the networks in their fixed order, muting the unconnected', () => {
        const heads = [...create().querySelectorAll('thead th')].map(h => h.textContent!.trim());
        expect(heads).toEqual(['Content', 'Blog', 'X', 'Discord']);
        expect(create().querySelectorAll('thead th.is-off').length).toBe(1);
    });

    it('reads every cell off the capabilities: pictures go to X four at a time and not to Discord', () => {
        const el = create();
        const cells = [...row(el, 'Pictures').querySelectorAll('td')];
        expect(cells.map(c => c.getAttribute('data-verdict'))).toEqual(['yes', 'partial', 'no']);
        expect(cells[1].querySelector('.pm-note')!.textContent).toBe('up to 4, first post');
        expect(cells[2].querySelector('.pm-note')!.textContent).toContain('link card');
        expect(row(el, 'Headings').querySelectorAll('td')[1].getAttribute('data-verdict')).toBe('partial');
    });

    it('lights the rows the document holds, with their counts', () => {
        const el = create();
        expect(row(el, 'Pictures').classList).toContain('is-present');
        expect(row(el, 'Pictures').querySelector('.pm-count')!.textContent).toBe('1');
        expect(row(el, 'Video').classList).not.toContain('is-present');
    });
});
