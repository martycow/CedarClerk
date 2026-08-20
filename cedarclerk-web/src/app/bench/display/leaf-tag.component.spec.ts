import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LeafState, LeafTagComponent } from './leaf-tag.component';

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
    imports: [LeafTagComponent],
    template: `<app-leaf-tag [state]="state" [interactive]="interactive" [swatch]="swatch"
                             [removable]="removable" [removeLabel]="removeLabel"
                             (activated)="picks = picks + 1" (removed)="drops = drops + 1">
                   <span pin>pin</span>Telegram
               </app-leaf-tag>`,
})
class Host {
    state: LeafState = 'idle';
    interactive = false;
    swatch = '';
    removable = false;
    removeLabel = '';
    picks = 0;
    drops = 0;
}

describe('LeafTagComponent', () => {
    let fixture: ComponentFixture<Host>;
    let host: Host;
    const leaf = () => fixture.nativeElement.querySelector('app-leaf-tag') as HTMLElement;
    const pick = () => fixture.nativeElement.querySelector('.lt-pick') as HTMLElement;
    const swatch = () => fixture.nativeElement.querySelector('.lt-swatch') as HTMLElement | null;
    const remove = () => fixture.nativeElement.querySelector('.lt-remove') as HTMLButtonElement | null;

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

    it('is a paper surface and projects the pin', () => {
        expect(leaf().getAttribute('data-surface')).toBe('paper');
        expect(leaf().textContent).toContain('pin');
        expect(leaf().textContent).toContain('Telegram');
    });

    it('carries its state as a class', () => {
        expect(leaf().classList).not.toContain('is-active');
        expect(leaf().classList).not.toContain('is-dried');

        host.state = 'active';
        render();
        expect(leaf().classList).toContain('is-active');

        host.state = 'dried';
        render();
        expect(leaf().classList).toContain('is-dried');
        expect(leaf().classList).not.toContain('is-active');
    });

    it('draws the series swatch only when one is given, and paints it from what it was handed', () => {
        expect(swatch()).toBeNull();
        host.swatch = 'var(--series-1)';
        render();
        expect(swatch()!.style.background).toBe('var(--series-1)');
    });

    it('is inert until it is interactive', () => {
        expect(pick().getAttribute('role')).toBeNull();
        expect(pick().getAttribute('tabindex')).toBeNull();
        pick().click();
        expect(host.picks).toBe(0);
    });

    it('picks on click, Enter and Space, and reports whether it is the chosen filter', () => {
        host.interactive = true;
        host.state = 'active';
        render();
        expect(pick().getAttribute('role')).toBe('button');
        expect(pick().getAttribute('aria-pressed')).toBe('true');

        host.state = 'idle';
        render();
        expect(pick().getAttribute('aria-pressed')).toBe('false');

        pick().click();
        pick().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
        pick().dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }));
        pick().dispatchEvent(new KeyboardEvent('keydown', { key: 'q' }));
        expect(host.picks).toBe(3);
    });

    it('a dried leaf is off: no tab stop, no pick, and it says so', () => {
        host.interactive = true;
        host.state = 'dried';
        render();
        expect(pick().getAttribute('role')).toBeNull();
        expect(pick().getAttribute('tabindex')).toBeNull();
        expect(pick().getAttribute('aria-disabled')).toBe('true');

        pick().click();
        pick().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
        expect(host.picks).toBe(0);
    });

    it('unpins without also picking, and names the control it draws an icon in', () => {
        host.interactive = true;
        host.removable = true;
        host.removeLabel = 'Unpin Telegram';
        render();
        expect(remove()!.getAttribute('aria-label')).toBe('Unpin Telegram');
        expect(remove()!.querySelector('app-icon')).toBeTruthy();

        remove()!.click();
        expect(host.drops).toBe(1);
        expect(host.picks).toBe(0);
    });

    describe('the prompt.md rules, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.lt-pick'); });

        it('never says error: a dried leaf means no data, and rust belongs to a stamp', () => {
            expect(css).not.toMatch(/var\(--danger\)/);
            expect(css).not.toMatch(/var\(--rust\)/);
            expect(css).toMatch(/is-dried[^{]*\{[^}]*color:\s*var\(--t3\)/);
        });

        it('holds the paper floor: 44px targets and 14px type', () => {
            const boxes = Array.from(css.matchAll(/min-(?:height|width):\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(boxes.length).toBeGreaterThan(0);
            for (const b of boxes) expect(b).toBe('var(--hit-target)');

            const sizes = Array.from(css.matchAll(/font-size:\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(sizes.length).toBeGreaterThan(0);
            for (const s of sizes) expect(s).toBe('var(--fs-ui)');
        });

        it('keeps one rounded corner pair and paints no literal colour', () => {
            expect(css).toMatch(/border-radius:\s*var\(--radius-paper\) var\(--radius-lg\) var\(--radius-paper\) var\(--radius-lg\)/);
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
        });
    });
});
