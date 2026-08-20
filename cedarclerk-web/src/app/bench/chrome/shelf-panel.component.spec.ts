import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ButtonComponent } from '../forms/button.component';
import { ShelfPanelComponent } from './shelf-panel.component';

// The component's own stylesheet, read back out of the document — the same trick paper-card's spec
// uses, because the rules under test are CSS rules. The length assertion is the control: without it
// a renamed class would make every check below pass over an empty string.
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

    it('is chrome, and names itself to a screen reader by its sign tile', () => {
        const host = el(create('Assets'));
        expect(host.getAttribute('data-surface')).toBe('chrome');
        expect(host.getAttribute('role')).toBe('region');
        expect(host.getAttribute('aria-label')).toBe('Assets');
        expect(host.querySelector('.sp-title')!.textContent!.trim()).toBe('Assets');
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

    it('carries tone and flush on the sheet, not on the board', () => {
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

    // The placement rule V2 is built on: a panel's own commands belong to the panel's header, and
    // the slot is the only door — there is no command input to reach for instead.
    it('puts the panel own commands in the header slot and the rest on the sheet', () => {
        const fixture = TestBed.createComponent(PanelHost);
        fixture.detectChanges();
        const panel = fixture.nativeElement.querySelector('app-shelf-panel') as HTMLElement;
        expect(panel.querySelector('.sp-actions app-button')!.textContent).toContain('Scan');
        expect(panel.querySelector('.sp-head .body-control')).toBeNull();
        expect(panel.querySelector('.sp-sheet .body-control')).not.toBeNull();
    });

    // One board, one sheet. The rule is refused rather than described, so a shell that reaches for
    // a panel-in-a-panel finds out at the first render instead of in a review.
    it('refuses to be nested inside another panel', () => {
        expect(() => {
            const fixture = TestBed.createComponent(NestedHost);
            fixture.detectChanges();
        }).toThrowError(/never nest a panel/i);
    });

    // A counter carved into the tile is read, so it gets the cream at full strength: the soft cream
    // measures 3.6:1 on the lit stop of the sign tile.
    it('never spends the soft cream on the sign tile', () => {
        const fixture = create();
        const head = el(fixture).querySelector('.sp-head') as HTMLElement;
        const scope = head.getAttributeNames().find(n => n.startsWith('_ngcontent-'))!.replace('_ngcontent-', '');
        const inked = [...sheetFor('.sp-head').matchAll(/([^{}]+)\{([^{}]*)\}/g)]
            .filter(m => m[1].includes(scope) && /\.sp-(head|title|count)/.test(m[1]) && /(^|[^-])color:/.test(m[2]));
        expect(inked.length, 'no colour rule found on the header — the class names moved').toBeGreaterThan(0);
        for (const m of inked) {
            const value = m[2].match(/(^|[^-])color:\s*([^;}]+)/)![2].trim();
            expect(value, m[1].trim()).toBe('var(--rail-ink)');
        }
    });

    describe('the surface does not leak across the slot', () => {
        it('declares the sheet as paper, so undeclared content lands on paper density', () => {
            const fixture = TestBed.createComponent(PanelHost);
            fixture.detectChanges();
            const panel = fixture.nativeElement.querySelector('app-shelf-panel') as HTMLElement;
            const sheet = panel.querySelector('.sp-sheet') as HTMLElement;
            expect(sheet.getAttribute('data-surface')).toBe('paper');

            const plain = panel.querySelector('.plain') as HTMLElement;
            expect(plain.closest('[data-surface]')).toBe(sheet);
        });

        it('leaves a projected paper control its own surface', () => {
            const fixture = TestBed.createComponent(PanelHost);
            fixture.detectChanges();
            const control = fixture.nativeElement.querySelector('.body-control') as HTMLElement;
            expect(control.getAttribute('data-surface')).toBe('paper');
        });

        // Encapsulation is the mechanism, so it is what gets asserted: the panel's descendant rules
        // are stamped with a content attribute that projected elements never receive, which is why
        // the header-scoped chrome sizing cannot reach into the body.
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
        // read by every projected line. The panel therefore sizes type only on its header parts.
        it('sizes type only inside the header, never on the sheet', () => {
            const fixture = create();
            const head = el(fixture).querySelector('.sp-head') as HTMLElement;
            // The scope id picks this component's rules out of whatever else the runner put in the
            // document, so the scan is the panel's own CSS and all of it.
            const scope = head.getAttributeNames().find(n => n.startsWith('_ngcontent-'))!.replace('_ngcontent-', '');
            const sized = [...sheetFor('.sp-sheet').matchAll(/([^{}]+)\{([^{}]*)\}/g)]
                .filter(m => m[1].includes(scope) && /font-size\s*:/.test(m[2]))
                .map(m => m[1].trim());
            expect(sized.length, 'no font-size rule found — the marker or the class names moved').toBeGreaterThan(0);
            for (const selector of sized) {
                expect(/\.sp-(title|count)\b/.test(selector), `${selector} sizes type outside the header`).toBe(true);
            }
        });
    });
});
