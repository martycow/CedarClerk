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

    // Tabs switch what a panel shows; the rail navigates between screens. The component cannot do
    // the rail's job because it draws no link at all.
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

    // The two behaviours the app's count-badge carried, which T-231 retires into this component.
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

        // The count is the whole point of the badge, and an aria-label replaces the text it is
        // written on rather than adding to it — so the label has to carry both or the tile is
        // announced as "Checks unresolved checks" with the number gone (ADR-155 clause 5).
        it('names what it counts without dropping the count', () => {
            create();
            const badge = fixture.nativeElement.querySelector('.it-badge') as HTMLElement;
            expect(badge.getAttribute('title')).toBe('unresolved checks');
            expect(badge.getAttribute('aria-label')).toBe('2 unresolved checks');

            create([{ id: 'a', label: 'A', badge: 4 }], 'a');
            expect(fixture.nativeElement.querySelector('.it-badge').hasAttribute('aria-label')).toBe(false);
        });

        it('states the rules once, where the retirement can reuse them', () => {
            expect(indexTabBadgeLabel(undefined)).toBe('');
            expect(indexTabBadgeLabel(0)).toBe('');
            expect(indexTabBadgeLabel(1)).toBe('1');
            expect(indexTabBadgeLabel(99)).toBe('99');
            expect(indexTabBadgeLabel(100)).toBe('99+');
            expect(indexTabBadgeLabel('draft')).toBe('draft');
        });
    });

    // role="tab" without aria-controls is half the pattern: the strip announces tabs and never
    // says what any of them opens.
    it('names the body a tile opens, when the caller gave it one', () => {
        create();
        expect(tiles().map(t => t.getAttribute('aria-controls'))).toEqual([null, 'preview-body', null]);
    });

    describe('the chrome band, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { create(); css = sheetFor('.it-tile'); });

        // A tile label is read, so it gets the cream at full strength: the soft cream measures
        // 3.2:1 on an unlit tile, which the recede filter darkens further. What sets a tile back is
        // that filter, the raise and the brass underline — never the ink.
        it('never spends the soft cream on a label', () => {
            const inked = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g))
                .filter(m => /(^|[^-])color:/.test(m[2]));
            expect(inked.length).toBeGreaterThan(0);
            for (const m of inked) {
                const value = m[2].match(/(^|[^-])color:\s*([^;}]+)/)![2].trim();
                expect(['var(--rail-ink)', 'var(--rail-edge)'], m[1].trim()).toContain(value);
            }
        });

        it('holds 11px type on a 30px box', () => {
            const sizes = Array.from(css.matchAll(/font-size:\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(sizes.length).toBeGreaterThan(0);
            for (const size of sizes) expect(['var(--text-chrome)', 'var(--text-chrome-sm)']).toContain(size);
            expect(css).toMatch(/min-height:\s*var\(--hit-chrome\)/);
        });
    });

    it('carries a tile tooltip only where one was given', () => {
        create();
        expect(tiles().map(t => t.getAttribute('title'))).toEqual([null, null, 'machine translation']);
    });
});
