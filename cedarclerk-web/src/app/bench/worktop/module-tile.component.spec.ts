import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ModuleTileComponent } from './module-tile.component';

// The component's own stylesheet, read back out of the document. Two of this component's rules are
// rules about CSS — the one number is the only readout in mono at --text-readout, and the paper
// floor holds every size on the plate — so the only way to hold the port to them is to read what
// actually shipped. The length assertion is the control: without it a renamed class would make
// every rule below pass over an empty string.
function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    // Comments go: an inline <style> keeps them and a constructed sheet's cssText does not, so a
    // rule read off the two forms would otherwise be asking two different questions.
    return hits.join('\n').replace(/\/\*[\s\S]*?\*\//g, ' ');
}

// A tile that a consumer tries to hang a second readout on. The kit's rule is that a tile carries
// ONE number and the second belongs on the screen it opens, so the attempt must reach nothing.
@Component({
    imports: [ModuleTileComponent],
    template: `
        <app-module-tile icon="cube" name="Билды" count="14" sub="последний 0.3.1">
            <span class="smuggled">2 провалено</span>
        </app-module-tile>
    `,
})
class SmugglerHost {}

describe('ModuleTileComponent', () => {
    let fixture: ComponentFixture<ModuleTileComponent>;
    const tile = () => fixture.nativeElement as HTMLElement;

    beforeEach(() => {
        fixture = TestBed.createComponent(ModuleTileComponent);
        fixture.componentRef.setInput('icon', 'list-checks');
        fixture.componentRef.setInput('name', 'Задачи');
        fixture.componentRef.setInput('count', 19);
        fixture.detectChanges();
    });

    it('is paper, and says so where the density lint and the touch carve-out can read it', () => {
        expect(tile().getAttribute('data-surface')).toBe('paper');
    });

    it('draws the icon, the name and the one number', () => {
        fixture.componentRef.setInput('sub', '11 закрыто · 1 просрочена');
        fixture.detectChanges();
        expect(tile().querySelector('app-icon')).not.toBeNull();
        expect(tile().querySelector('.mt-name')!.textContent!.trim()).toBe('Задачи');
        expect(tile().querySelector('.mt-count')!.textContent!.trim()).toBe('19');
        expect(tile().querySelector('.mt-sub')!.textContent!.trim()).toBe('11 закрыто · 1 просрочена');
    });

    it('takes a count that is not a number — a sprint is S4 and a module with nothing yet is a dash', () => {
        fixture.componentRef.setInput('count', 'S4');
        fixture.detectChanges();
        expect(tile().querySelector('.mt-count')!.textContent!.trim()).toBe('S4');
    });

    it('leaves the subline out entirely when there is no context to give', () => {
        expect(tile().querySelector('.mt-sub')).toBeNull();
    });

    it('carries ONE readout: a second number cannot be projected in beside it', () => {
        const host = TestBed.createComponent(SmugglerHost);
        host.detectChanges();
        const projected = host.nativeElement.querySelector('app-module-tile') as HTMLElement;
        expect(projected.querySelector('.smuggled')).toBeNull();
        expect(projected.textContent).not.toContain('2 провалено');
        expect(projected.querySelectorAll('.mt-count').length).toBe(1);
    });

    it('holds the tilt to the ±0.6° the kit allows a wall of plates', () => {
        expect(tile().style.transform).toBe('rotate(0deg)');

        fixture.componentRef.setInput('rotate', -0.35);
        fixture.detectChanges();
        expect(tile().style.transform).toBe('rotate(-0.35deg)');

        fixture.componentRef.setInput('rotate', 12);
        fixture.detectChanges();
        expect(tile().style.transform).toBe('rotate(0.6deg)');
    });

    it('opens what it names, by click and by keyboard, and is reachable to get there', () => {
        let hits = 0;
        fixture.componentInstance.activated.subscribe(() => hits++);

        expect(tile().getAttribute('role')).toBe('button');
        expect(tile().tabIndex).toBe(0);

        tile().click();
        expect(hits).toBe(1);

        for (const key of ['Enter', ' ']) {
            const event = new KeyboardEvent('keydown', { key, cancelable: true });
            tile().dispatchEvent(event);
            expect(event.defaultPrevented).toBe(true);
        }
        expect(hits).toBe(3);

        const ignored = new KeyboardEvent('keydown', { key: 'a', cancelable: true });
        tile().dispatchEvent(ignored);
        expect(ignored.defaultPrevented).toBe(false);
        expect(hits).toBe(3);
    });

    it('hangs a tooltip only when the consumer writes one, and never from a static title', () => {
        expect(tile().getAttribute('title')).toBeNull();

        fixture.componentRef.setInput('hint', 'Открыть задачи проекта');
        fixture.detectChanges();
        expect(tile().getAttribute('title')).toBe('Открыть задачи проекта');
    });

    it('puts the one number in mono at the readout size, and nothing else there', () => {
        const css = sheetFor('.mt-count');
        expect(css).toMatch(/\.mt-count[^{]*\{[^}]*font-family:\s*var\(--font-mono\)/);
        const readouts = [...css.matchAll(/([^{}]*)\{[^}]*font-size:\s*var\(--text-readout\)/g)].map(m => m[1].trim());
        expect(readouts.length).toBeGreaterThan(0);
        for (const selector of readouts) expect(selector).toContain('.mt-count');
    });

    it('stands on the paper floor: every size on the plate is a token at 14px or above', () => {
        const css = sheetFor('.mt-count');
        const sizes = [...css.matchAll(/font-size:\s*([^;}]+)/g)].map(m => m[1].trim());
        expect(sizes.length).toBeGreaterThan(0);
        // --fs-ui is 14px, --fs-body 15px and --text-readout 21px. A raw px here, or a step below
        // the floor, is what check-density rule 5 fails on — this says the same thing by name so a
        // wrong step is caught in the unit run rather than in the lint phase.
        for (const size of sizes) expect(['var(--fs-ui)', 'var(--fs-body)', 'var(--text-readout)']).toContain(size);
    });

    it('keeps its own shadow off the focus ring (ADR-140)', () => {
        const css = sheetFor('.mt-count');
        const shadows = [...css.matchAll(/([^{}]*)\{[^}]*box-shadow:/g)].map(m => m[1]);
        expect(shadows.length).toBeGreaterThan(0);
        for (const selector of shadows) expect(selector).toContain(':not(:focus-visible)');
    });
});
