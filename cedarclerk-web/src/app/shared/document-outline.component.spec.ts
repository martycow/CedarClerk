import { TestBed } from '@angular/core/testing';
import { OutlineEntry, buildOutline } from '../core/document-outline';
import { LocaleService } from '../core/i18n/locale.service';
import { DocumentOutlineComponent } from './document-outline.component';

const DOC = [
    { typeName: 'heading', attrs: { level: 1 }, text: 'Fog and light' },
    { typeName: 'paragraph', attrs: {}, text: 'Two weeks of fixing depth.' },
    { typeName: 'heading', attrs: { level: 2 }, text: 'Why fog' },
    { typeName: 'image', attrs: { src: '/media/fog_pass_02.png' }, selectableAtom: true },
    { typeName: 'bulletList', attrs: {}, childCount: 3, text: 'a b c' },
];

describe('DocumentOutlineComponent', () => {
    // jsdom ships no layout, so it ships no scrollIntoView. Stubbed here rather than guarded in the
    // component: every browser has it, and a `?.` in production code would be documenting the test
    // environment.
    beforeAll(() => {
        Element.prototype.scrollIntoView ??= function () { /* no layout to scroll */ };
    });

    function create(entries: OutlineEntry[] = buildOutline(DOC), active = -1) {
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(DocumentOutlineComponent);
        fixture.componentRef.setInput('entries', entries);
        fixture.componentRef.setInput('active', active);
        fixture.detectChanges();
        return fixture;
    }

    const rows = (fixture: { nativeElement: HTMLElement }) =>
        Array.from(fixture.nativeElement.querySelectorAll<HTMLButtonElement>('.ol-row:not(.ol-root)'));
    const root = (fixture: { nativeElement: HTMLElement }) =>
        fixture.nativeElement.querySelector<HTMLButtonElement>('.ol-root')!;

    it('keeps Document as the selectable root above every block', () => {
        const fixture = create(buildOutline(DOC), -1);
        let picked = false;
        fixture.componentInstance.documentPick.subscribe(() => picked = true);

        expect(root(fixture).querySelector('.ol-label')!.textContent!.trim()).toBe('Document');
        expect(root(fixture).getAttribute('aria-current')).toBe('true');
        root(fixture).click();
        expect(picked).toBe(true);
    });

    it('draws one row per block, labelled by its words or by its kind', () => {
        const fixture = create();
        const labels = rows(fixture).map(r => r.querySelector('.ol-label')!.textContent!.trim());

        expect(labels).toEqual([
            'Fog and light',
            'Two weeks of fixing depth.',
            'Why fog',
            'fog_pass_02.png',
            'List — 3 items',
        ]);
    });

    it('names the type of each row to a screen reader without spending the column on it', () => {
        const fixture = create();
        const kinds = rows(fixture).map(r => r.querySelector('.visually-hidden')!.textContent!.trim());
        expect(kinds).toEqual(['H1', 'Paragraph', 'H2', 'Image', 'List']);
    });

    it('says the document is empty rather than drawing an empty list', () => {
        const fixture = create([]);
        expect(rows(fixture)).toHaveLength(0);
        expect(fixture.nativeElement.querySelector('.ol-empty')!.textContent!.trim()).toBe('The document is empty');
    });

    // ─── caret → highlight ────────────────────────────────────────────────────────────────────
    it('lights the row the caret is in, and moves the light when the caret moves', () => {
        const fixture = create(buildOutline(DOC), 1);
        expect(rows(fixture).map(r => r.getAttribute('aria-current'))).toEqual([null, 'true', null, null, null]);

        fixture.componentRef.setInput('active', 3);
        fixture.detectChanges();
        expect(rows(fixture).map(r => r.getAttribute('aria-current'))).toEqual([null, null, null, 'true', null]);
        expect(rows(fixture)[3].classList.contains('is-current')).toBe(true);
    });

    it('lights the document root when no block is selected', () => {
        const fixture = create(buildOutline(DOC), -1);
        expect(rows(fixture).some(r => r.hasAttribute('aria-current'))).toBe(false);
        expect(root(fixture).getAttribute('aria-current')).toBe('true');
    });

    // ─── click → selection ────────────────────────────────────────────────────────────────────
    it('emits the block that was clicked', () => {
        const fixture = create(buildOutline(DOC), 0);
        const picked: OutlineEntry[] = [];
        fixture.componentInstance.pick.subscribe(entry => picked.push(entry));

        rows(fixture)[3].click();
        fixture.detectChanges();

        expect(picked).toHaveLength(1);
        expect(picked[0].index).toBe(3);
        expect(picked[0].selectableAtom).toBe(true);
    });

    // The loop-prevention property, asserted directly (ADR-162 clause 1): the component owns no
    // selection, so a click cannot light a row. The light moves only when the document says so —
    // which is what makes a feedback loop between the two directions unrepresentable rather than
    // merely guarded against.
    it('does not light the row it was clicked on — only the document decides that', () => {
        const fixture = create(buildOutline(DOC), 0);
        rows(fixture)[3].click();
        fixture.detectChanges();

        expect(rows(fixture)[3].hasAttribute('aria-current')).toBe(false);
        expect(rows(fixture)[0].getAttribute('aria-current')).toBe('true');

        fixture.componentRef.setInput('active', 3);
        fixture.detectChanges();
        expect(rows(fixture)[3].getAttribute('aria-current')).toBe('true');
    });

    // ─── keyboard ─────────────────────────────────────────────────────────────────────────────
    it('is one tab stop, and the stop is the block the caret is in', () => {
        const fixture = create(buildOutline(DOC), 2);
        expect(rows(fixture).map(r => r.getAttribute('tabindex'))).toEqual(['-1', '-1', '0', '-1', '-1']);
    });

    it('browses with the arrows without touching the document', () => {
        const fixture = create(buildOutline(DOC), 0);
        const picked: OutlineEntry[] = [];
        fixture.componentInstance.pick.subscribe(entry => picked.push(entry));
        const list = fixture.nativeElement.querySelector('.ol-list') as HTMLElement;

        list.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
        fixture.detectChanges();
        expect(document.activeElement).toBe(rows(fixture)[1]);
        expect(rows(fixture).map(r => r.getAttribute('tabindex'))).toEqual(['-1', '0', '-1', '-1', '-1']);
        expect(rows(fixture)[0].getAttribute('aria-current')).toBe('true');
        expect(picked).toHaveLength(0);

        list.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
        fixture.detectChanges();
        expect(rows(fixture)[4].getAttribute('tabindex')).toBe('0');

        list.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
        fixture.detectChanges();
        expect(root(fixture).getAttribute('tabindex')).toBe('0');
    });

    it('stops at both ends rather than wrapping', () => {
        const fixture = create(buildOutline(DOC), 0);
        const list = fixture.nativeElement.querySelector('.ol-list') as HTMLElement;

        list.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
        fixture.detectChanges();
        expect(root(fixture).getAttribute('tabindex')).toBe('0');

        for (let i = 0; i < 9; i++) list.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
        fixture.detectChanges();
        expect(rows(fixture)[4].getAttribute('tabindex')).toBe('0');
    });

    it('hands the tab stop back to the caret once focus leaves', () => {
        const fixture = create(buildOutline(DOC), 2);
        const list = fixture.nativeElement.querySelector('.ol-list') as HTMLElement;

        list.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
        fixture.detectChanges();
        expect(rows(fixture)[3].getAttribute('tabindex')).toBe('0');

        list.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: document.body }));
        fixture.detectChanges();
        expect(rows(fixture)[2].getAttribute('tabindex')).toBe('0');
    });

    it('tracks the caret inside the shelf without scrolling the surrounding workspace', () => {
        const fixture = create(buildOutline(DOC), 0);
        const workspace = document.createElement('div');
        const shelf = document.createElement('div');
        shelf.className = 'sp-sheet';
        document.body.append(workspace);
        workspace.append(shelf);
        shelf.append(fixture.nativeElement);
        workspace.scrollTop = 40;
        Object.defineProperties(shelf, { scrollHeight: { value: 800 }, clientHeight: { value: 200, configurable: true } });
        vi.spyOn(shelf, 'getBoundingClientRect').mockReturnValue({ top: 100, bottom: 300 } as DOMRect);
        vi.spyOn(rows(fixture)[2], 'getBoundingClientRect').mockReturnValue({ top: 350, bottom: 380 } as DOMRect);
        const ancestorScroll = vi.spyOn(rows(fixture)[2], 'scrollIntoView');
        fixture.componentRef.setInput('active', 2);
        fixture.detectChanges();
        expect(shelf.scrollTop).toBe(80);
        expect(workspace.scrollTop).toBe(40);
        expect(ancestorScroll).not.toHaveBeenCalled();
        Object.defineProperty(shelf, 'clientHeight', { value: 800 });
        fixture.componentRef.setInput('active', 3);
        fixture.detectChanges();
        expect(shelf.scrollTop).toBe(80);
        fixture.destroy();
        workspace.remove();
    });
});
