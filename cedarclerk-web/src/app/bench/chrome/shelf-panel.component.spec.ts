import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ButtonComponent } from '../forms/button.component';
import { ShelfPanelComponent } from './shelf-panel.component';

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

@Component({
    imports: [ShelfPanelComponent, ButtonComponent],
    template: `
        <app-shelf-panel title="Inspector" count="image">
            <app-button actions variant="rail" size="sm">Scan</app-button>
            <app-button class="body-control">Publish</app-button>
            <span class="plain">a bare line of body text</span>
        </app-shelf-panel>
    `,
})
class PanelHost {}

@Component({
    imports: [ShelfPanelComponent],
    template: `
        <app-shelf-panel title="Assets">
            <app-shelf-panel title="Inspector" />
        </app-shelf-panel>
    `,
})
class NestedHost {}

describe('ShelfPanelComponent', () => {
    function create(title = 'Inspector') {
        const fixture = TestBed.createComponent(ShelfPanelComponent);
        fixture.componentRef.setInput('title', title);
        fixture.detectChanges();
        return fixture;
    }

    const el = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement;

    // ADR-239 clause 2 — a plain card: paper, named to a screen reader by its caption line.
    it('is a paper card, and names itself to a screen reader by its caption', () => {
        const host = el(create('Assets'));
        expect(host.getAttribute('data-surface')).toBe('paper');
        expect(host.getAttribute('role')).toBe('region');
        expect(host.getAttribute('aria-label')).toBe('Assets');
        const title = host.querySelector('.sp-title') as HTMLElement;
        expect(title.textContent!.trim()).toBe('Assets');
        expect(title.classList).toContain('label');
    });

    it('draws the counter only once one is given, and draws a zero as a zero', () => {
        const fixture = create();
        expect(el(fixture).querySelector('.sp-count')).toBeNull();

        fixture.componentRef.setInput('count', 0);
        fixture.detectChanges();
        expect(el(fixture).querySelector('.sp-count')!.textContent!.trim()).toBe('0');

        fixture.componentRef.setInput('count', '2481');
        fixture.detectChanges();
        expect(el(fixture).querySelector('.sp-count')!.textContent!.trim()).toBe('2481');
    });

    it('carries tone and flush on the sheet, not on the card', () => {
        const fixture = create();
        const sheet = () => el(fixture).querySelector('.sp-sheet') as HTMLElement;
        expect(sheet().classList.contains('is-cork')).toBe(false);
        expect(sheet().classList.contains('is-flush')).toBe(false);

        fixture.componentRef.setInput('tone', 'cork');
        fixture.componentRef.setInput('flush', true);
        fixture.detectChanges();
        expect(sheet().classList.contains('is-cork')).toBe(true);
        expect(sheet().classList.contains('is-flush')).toBe(true);
    });

    it('puts the panel own commands in the caption slot and the rest on the sheet', () => {
        const fixture = TestBed.createComponent(PanelHost);
        fixture.detectChanges();
        const panel = fixture.nativeElement.querySelector('app-shelf-panel') as HTMLElement;
        expect(panel.querySelector('.sp-actions app-button')!.textContent).toContain('Scan');
        expect(panel.querySelector('.sp-head .body-control')).toBeNull();
        expect(panel.querySelector('.sp-sheet .body-control')).not.toBeNull();
    });

    it('refuses to be nested inside another panel', () => {
        expect(() => {
            const fixture = TestBed.createComponent(NestedHost);
            fixture.detectChanges();
        }).toThrowError(/never nest a panel/i);
    });

    // The wood is gone: no frame, no sign tile, no rail ink anywhere in the sheet.
    it('paints paper and nothing of the bench', () => {
        create();
        const css = sheetFor('.sp-head');
        expect(css).not.toMatch(/--rail-ink|--sign-tile|--shelf-frame|--wood-edge|--tex-wood|--grad-sign-tile/);
        expect(css).toMatch(/background:\s*var\(--sheet\)/);
        expect(css).toMatch(/\.sp-count[^{]*\{[^}]*color:\s*var\(--t3\)/);
    });

    describe('the surface does not leak across the slot', () => {
        it('declares the sheet as paper, so undeclared content lands on paper density', () => {
            const fixture = TestBed.createComponent(PanelHost);
            fixture.detectChanges();
            const panel = fixture.nativeElement.querySelector('app-shelf-panel') as HTMLElement;
            const sheet = panel.querySelector('.sp-sheet') as HTMLElement;
            expect(sheet.getAttribute('data-surface')).toBe('paper');

            const plain = panel.querySelector('.plain') as HTMLElement;
            const between: string[] = [];
            for (let node = plain.parentElement; node && node !== sheet; node = node.parentElement) {
                if (node.hasAttribute('data-surface')) between.push(node.getAttribute('data-surface')!);
            }
            expect(between, 'a surface declared under the sheet takes the floor away from it').toEqual([]);
        });

        it('leaves a projected paper control its own surface', () => {
            const fixture = TestBed.createComponent(PanelHost);
            fixture.detectChanges();
            const control = fixture.nativeElement.querySelector('.body-control') as HTMLElement;
            expect(control.getAttribute('data-surface')).toBe('paper');
        });

        it('never stamps projected content with the panel own scope', () => {
            const fixture = TestBed.createComponent(PanelHost);
            fixture.detectChanges();
            const panel = fixture.nativeElement.querySelector('app-shelf-panel') as HTMLElement;
            const head = panel.querySelector('.sp-head') as HTMLElement;
            const scope = head.getAttributeNames().find(n => n.startsWith('_ngcontent-'));
            expect(scope, 'the panel template carries no encapsulation scope attribute').toBeTruthy();

            for (const selector of ['.plain', '.body-control']) {
                const projected = panel.querySelector(selector) as HTMLElement;
                expect(projected.hasAttribute(scope!), `${selector} was stamped with the panel scope`).toBe(false);
            }
        });

        // The one leak encapsulation cannot stop is inheritance: a font-size on the sheet would be
        // read by every projected line. The panel therefore sizes type only on its caption.
        it('sizes type only inside the caption, never on the sheet', () => {
            const fixture = create();
            const head = el(fixture).querySelector('.sp-head') as HTMLElement;
            const scope = head.getAttributeNames().find(n => n.startsWith('_ngcontent-'))!.replace('_ngcontent-', '');
            const sized = [...sheetFor('.sp-sheet').matchAll(/([^{}]+)\{([^{}]*)\}/g)]
                .filter(m => m[1].includes(scope) && /font-size\s*:/.test(m[2]))
                .map(m => m[1].trim());
            expect(sized.length, 'no font-size rule found — the marker or the class names moved').toBeGreaterThan(0);
            for (const selector of sized) {
                expect(/\.sp-(title|count)\b/.test(selector), `${selector} sizes type outside the caption`).toBe(true);
            }
        });
    });
});
