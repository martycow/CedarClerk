import { ComponentFixture, TestBed } from '@angular/core/testing';
import { IndexTabItem, IndexTabsComponent, indexTabBadgeLabel } from './index-tabs.component';

const ITEMS: IndexTabItem[] = [
    { id: 'checks', label: 'Checks', badge: 2, badgeTitle: 'unresolved checks' },
    { id: 'preview', label: 'Preview', panelId: 'preview-body' },
    { id: 'translate', label: 'Translate', hint: 'machine translation' },
];

// The component's own stylesheet, read back out of the document, because the rules under test are
// CSS rules. The length assertion is the control: without it a renamed class would make every
// check below pass over an empty string.
function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n');
}

describe('IndexTabsComponent', () => {
    let fixture: ComponentFixture<IndexTabsComponent>;
    const tiles = () => Array.from(fixture.nativeElement.querySelectorAll('.it-tile')) as HTMLButtonElement[];

    function create(items: IndexTabItem[] = ITEMS, value = 'checks') {
        fixture = TestBed.createComponent(IndexTabsComponent);
        fixture.componentRef.setInput('items', items);
        fixture.componentRef.setInput('value', value);
        fixture.detectChanges();
        return fixture;
    }

    it('is chrome, and announces itself as a tab strip', () => {
        create();
        const host = fixture.nativeElement as HTMLElement;
        expect(host.getAttribute('data-surface')).toBe('chrome');
        expect(host.getAttribute('role')).toBe('tablist');
        expect(tiles().map(t => t.getAttribute('role'))).toEqual(['tab', 'tab', 'tab']);
    });

    it('lights exactly the tile the value names', () => {
        create();
        expect(tiles().map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);

        fixture.componentRef.setInput('value', 'translate');
        fixture.detectChanges();
        expect(tiles().map(t => t.classList.contains('is-on'))).toEqual([false, false, true]);
    });

    it('emits the id of the body to show, and stays quiet when that body is already shown', () => {
        create();
        const seen: string[] = [];
        fixture.componentInstance.selected.subscribe(id => seen.push(id));

        tiles()[1].click();
        tiles()[0].click();
        expect(seen).toEqual(['preview']);
    });

    // Tabs switch what a panel shows; the sidebar navigates between screens. The component cannot
    // do the sidebar's job because it draws no link at all.
    it('draws buttons, never links', () => {
        create();
        expect(fixture.nativeElement.querySelectorAll('a').length).toBe(0);
        expect(tiles().map(t => t.type)).toEqual(['button', 'button', 'button']);
    });

    it('keeps one tab stop and moves it with the arrow keys', () => {
        create();
        expect(tiles().map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);

        const seen: string[] = [];
        fixture.componentInstance.selected.subscribe(id => seen.push(id));
        tiles()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
        fixture.detectChanges();
        expect(seen).toEqual(['translate']);

        fixture.componentRef.setInput('value', 'translate');
        fixture.detectChanges();
        expect(tiles().map(t => t.getAttribute('tabindex'))).toEqual(['-1', '-1', '0']);
    });

    it('parks the tab stop on the first tile when nothing is selected', () => {
        create(ITEMS, '');
        expect(tiles().map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);
        expect(tiles().some(t => t.classList.contains('is-on'))).toBe(false);
    });

    describe('the badge counts the way the app already counted', () => {
        it('draws nothing at zero or below', () => {
            create([{ id: 'a', label: 'A', badge: 0 }, { id: 'b', label: 'B', badge: -3 }], 'a');
            expect(fixture.nativeElement.querySelectorAll('.it-badge').length).toBe(0);
        });

        it('caps a runaway count at 99+ so the tile cannot be stretched', () => {
            create([{ id: 'a', label: 'A', badge: 99 }, { id: 'b', label: 'B', badge: 4210 }], 'a');
            const badges = Array.from(fixture.nativeElement.querySelectorAll('.it-badge')) as HTMLElement[];
            expect(badges.map(b => b.textContent!.trim())).toEqual(['99', '99+']);
        });

        it('draws a string badge as written — that is how it says something other than a quantity', () => {
            create([{ id: 'a', label: 'A', badge: 'new' }, { id: 'b', label: 'B', badge: '  ' }], 'a');
            const badges = Array.from(fixture.nativeElement.querySelectorAll('.it-badge')) as HTMLElement[];
            expect(badges.map(b => b.textContent!.trim())).toEqual(['new']);
        });

        it('names what it counts without dropping the count', () => {
            create();
            const badge = fixture.nativeElement.querySelector('.it-badge') as HTMLElement;
            expect(badge.getAttribute('title')).toBe('unresolved checks');
            expect(badge.getAttribute('aria-label')).toBe('2 unresolved checks');

            create([{ id: 'a', label: 'A', badge: 4 }], 'a');
            expect(fixture.nativeElement.querySelector('.it-badge').hasAttribute('aria-label')).toBe(false);
        });

        it('states the rules once, where the sidebar can reuse them', () => {
            expect(indexTabBadgeLabel(undefined)).toBe('');
            expect(indexTabBadgeLabel(0)).toBe('');
            expect(indexTabBadgeLabel(1)).toBe('1');
            expect(indexTabBadgeLabel(99)).toBe('99');
            expect(indexTabBadgeLabel(100)).toBe('99+');
            expect(indexTabBadgeLabel('draft')).toBe('draft');
        });
    });

    it('names the body a tile opens, when the caller gave it one', () => {
        create();
        expect(tiles().map(t => t.getAttribute('aria-controls'))).toEqual([null, 'preview-body', null]);
    });

    // ADR-239 — the strip is the artboards' segmented control: paper inks in a trough, the lit tile
    // a raised sheet, still cut at trim so a 24px control fits a caption line.
    describe('the segmented control, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { create(); css = sheetFor('.it-tile'); });

        it('writes with paper ink and lifts the lit tile onto a sheet', () => {
            const inked = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g))
                .filter(m => /(^|[^-])color:/.test(m[2]));
            expect(inked.length).toBeGreaterThan(0);
            for (const m of inked) {
                const value = m[2].match(/(^|[^-])color:\s*([^;}]+)/)![2].trim();
                expect(['var(--t2)', 'var(--text)', 'var(--accent)'], m[1].trim()).toContain(value);
            }
            expect(css).toMatch(/is-on[^{]*\{[^}]*background:\s*var\(--sheet\)/);
            expect(css).not.toMatch(/--rail-ink|--sign-tile|--tex-wood|--brass/);
        });

        it('holds trim type on a trim box', () => {
            const sizes = Array.from(css.matchAll(/font-size:\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(sizes.length).toBeGreaterThan(0);
            for (const size of sizes) expect(['var(--fs-11)', 'var(--fs-12)']).toContain(size);
            expect(css).toMatch(/min-height:\s*var\(--hit-surface, var\(--hit-trim\)\)/);
        });
    });

    it('carries a tile tooltip only where one was given', () => {
        create();
        expect(tiles().map(t => t.getAttribute('title'))).toEqual([null, null, 'machine translation']);
    });
});
