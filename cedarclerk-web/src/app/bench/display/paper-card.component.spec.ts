import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PaperCardComponent } from './paper-card.component';

// The component's own stylesheet, read back out of the document. The prompt.md rules for this
// component are rules about CSS ("never glassy, never blur it, never a large radius", "hover lifts
// it by 2-3px with --ease-swing"), so the only way to hold the port to them is to read what it
// actually shipped. The length assertion is the control: without it a renamed class would make
// every rule below pass over an empty string.
function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n');
}

@Component({
    standalone: true,
    imports: [PaperCardComponent],
    template: `<app-paper-card [deckled]="deckled" [seed]="seed" [rotate]="rotate"
                               [bright]="bright" [interactive]="interactive"
                               (activated)="hits = hits + 1">Devlog</app-paper-card>`,
})
class Host {
    deckled = false;
    seed = 7;
    rotate = -0.5;
    bright = false;
    interactive = false;
    hits = 0;
}

describe('PaperCardComponent', () => {
    let fixture: ComponentFixture<Host>;
    let host: Host;
    const card = () => fixture.nativeElement.querySelector('app-paper-card') as HTMLElement;
    const sheet = () => fixture.nativeElement.querySelector('.pc-sheet') as HTMLElement;

    // The host is a plain component holding plain fields, so a field written between renders
    // leaves its view clean and detectChanges alone would report the write as a change that
    // arrived after the check.
    const render = () => { fixture.changeDetectorRef.markForCheck(); fixture.detectChanges(); };

    beforeEach(async () => {
        await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
        fixture = TestBed.createComponent(Host);
        host = fixture.componentInstance;
        render();
    });

    it('is a paper surface', () => {
        expect(card().getAttribute('data-surface')).toBe('paper');
    });

    it('rotates from the input and never from a style attribute on the host', () => {
        expect(sheet().style.transform).toBe('rotate(-0.5deg)');
        expect(card().getAttribute('style')).toBeNull();

        host.rotate = 1.4;
        render();
        expect(sheet().style.transform).toBe('rotate(1.4deg)');
    });

    it('holds rotation inside -2..2', () => {
        host.rotate = 9;
        render();
        expect(sheet().style.transform).toBe('rotate(2deg)');

        host.rotate = -40;
        render();
        expect(sheet().style.transform).toBe('rotate(-2deg)');
    });

    it('cuts the edge square with a border by default', () => {
        expect(sheet().classList).toContain('pc-cut');
        expect(sheet().style.clipPath).toBe('');
    });

    it('tears the edge when deckled, and the same seed tears it the same way', () => {
        host.deckled = true;
        host.seed = 11;
        render();
        const first = sheet().style.clipPath;
        expect(first).toMatch(/^polygon\(0% 0%, 100% 0%, /);
        expect(sheet().classList).not.toContain('pc-cut');

        host.seed = 12;
        render();
        const other = sheet().style.clipPath;
        expect(other).not.toBe(first);

        host.seed = 11;
        render();
        expect(sheet().style.clipPath).toBe(first);
    });

    it('switches to bright stock', () => {
        expect(sheet().classList).not.toContain('pc-bright');
        host.bright = true;
        render();
        expect(sheet().classList).toContain('pc-bright');
    });

    it('is inert until it is interactive', () => {
        expect(card().getAttribute('role')).toBeNull();
        expect(card().getAttribute('tabindex')).toBeNull();
        card().click();
        expect(host.hits).toBe(0);
    });

    it('answers click, Enter and Space when interactive', () => {
        host.interactive = true;
        render();
        expect(card().getAttribute('role')).toBe('button');
        expect(card().getAttribute('tabindex')).toBe('0');

        card().click();
        card().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
        card().dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }));
        card().dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }));
        expect(host.hits).toBe(3);
    });

    describe('the prompt.md rules, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.pc-sheet'); });

        it('casts a real drop shadow as a filter, which is the only shadow a torn edge can have', () => {
            expect(css).toMatch(/filter:\s*var\(--shadow-card-drop\)/);
        });

        it('declares paper ink for content and nested controls', () => {
            expect(css).toMatch(/\.pc-sheet[^{]*\{[^}]*color:\s*var\(--text\)/s);
            expect(css).toMatch(/--surface-ink:\s*var\(--text\)/);
            expect(css).toMatch(/--surface-ink-soft:\s*var\(--t2\)/);
        });

        it('is never glassy and never blurred', () => {
            expect(css).not.toMatch(/backdrop-filter/);
            expect(css).not.toMatch(/blur\(/);
        });

        it('is never given a large radius', () => {
            const radii = Array.from(css.matchAll(/border-radius:\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(radii.length).toBeGreaterThan(0);
            for (const r of radii) expect(r).toBe('var(--radius-paper)');
        });

        it('lifts 2-3px on --ease-swing, and that is the whole hover language', () => {
            expect(css).toMatch(/--pc-lift:\s*3px/);
            expect(css).toMatch(/:hover[^{]*\{[^}]*translateY\(calc\(var\(--pc-lift\)/);
            expect(css).toMatch(/transition:\s*transform[^;]*var\(--ease-swing\)/);
            expect(css).not.toMatch(/:hover[^{]*\{[^}]*scale/);
        });

        it('paints no literal colour and no loose pixel but the named lift', () => {
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
            expect(css.replace(/--pc-lift:\s*3px/, '')).not.toMatch(/\d+px/);
        });
    });
});
