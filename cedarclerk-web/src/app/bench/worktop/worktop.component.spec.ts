import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PaperCardComponent } from '../display/paper-card.component';
import { WorktopComponent } from './worktop.component';

// The component's own stylesheet, read back out of the document. Half of what this port owes is a
// CSS rule — which layer the pencil grid is on, which ink the edge strip takes — and the only way
// to hold it to them is to read what shipped. The length assertion is the control: without it a
// renamed class would make every rule below pass over an empty string.
// Emulated encapsulation rewrites what was authored — :host becomes an [_nghost-…] attribute and
// every other selector gains an [_ngcontent-…] one — so the shim is undone before the rules are
// read, and attribute values are unquoted the way the shim leaves them.
function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n')
        .replace(/\[_ngcontent-[^\]]+\]/g, '')
        .replace(/\[_nghost-[^\]]+\]/g, ':host')
        .replace(/(\[[a-z-]+)="([^"]*)"\]/g, '$1=$2]');
}

@Component({
    imports: [WorktopComponent, PaperCardComponent],
    template: `
        <app-worktop label="лист 640 px" meta="пост · RU">
            <app-paper-card bright>Свет и туман</app-paper-card>
        </app-worktop>
    `,
})
class SheetOnTopHost {}

describe('WorktopComponent', () => {
    let fixture: ComponentFixture<WorktopComponent>;
    const top = () => fixture.nativeElement as HTMLElement;
    const edge = () => top().querySelector('.wt-edge') as HTMLElement | null;
    const body = () => top().querySelector('.wt-body') as HTMLElement;

    beforeEach(() => {
        fixture = TestBed.createComponent(WorktopComponent);
        fixture.detectChanges();
    });

    afterEach(() => fixture.destroy());

    it('is chrome — it is the frame, and the frame lives in the shell', () => {
        expect(top().getAttribute('data-surface')).toBe('chrome');
    });

    // The trap: a chrome box holding paper. Chrome's 30px/11-13px numbers ride down from the
    // attribute, so without the body restating the axis the sheet lying on the top would inherit
    // them, and every control on that sheet would come out at chrome's density. What this runner
    // can hold the worktop to is the chain of declarations; the px the chain resolves to needs an
    // engine with a coarse pointer, and is measured in e2e/17-density.spec.ts.
    it('does not leak chrome onto what lies on it', () => {
        expect(body().getAttribute('data-surface')).toBe('paper');

        fixture.destroy();
        const projected = TestBed.createComponent(SheetOnTopHost);
        projected.detectChanges();
        const card = projected.nativeElement.querySelector('app-paper-card') as HTMLElement;
        // The floor rides down as an inherited custom property, so what decides a control's floor
        // is the last surface named on the way to it. The chain from the top down to the card is
        // read whole: the frame opens it, and nothing under the body may put chrome back.
        const chain: string[] = [];
        for (let node: HTMLElement | null = card; node; node = node.parentElement) {
            if (node.hasAttribute('data-surface')) chain.unshift(node.getAttribute('data-surface')!);
        }
        expect(chain.length, 'the card carries no surface chain at all').toBeGreaterThan(1);
        expect(chain[0], 'the top itself is the frame').toBe('chrome');
        expect(chain.indexOf('chrome', 1), `chrome comes back under the body: ${chain.join(' > ')}`).toBe(-1);
        projected.destroy();
    });


    it('lays lamp over rules over stock, and lets a consumer drop either layer', () => {
        expect(top().style.getPropertyValue('--wt-lamp')).toBe('var(--lamp)');
        expect(top().style.getPropertyValue('--wt-grid')).toBe('var(--grid-worktop)');

        fixture.componentRef.setInput('lamp', false);
        fixture.componentRef.setInput('grid', false);
        fixture.detectChanges();
        expect(top().style.getPropertyValue('--wt-lamp')).toBe('');
        expect(top().style.getPropertyValue('--wt-grid')).toBe('');
    });

    it('draws the strip only for what it was given', () => {
        expect(edge()).toBeNull();

        fixture.componentRef.setInput('meta', 'пост · RU');
        fixture.detectChanges();
        expect(edge()!.querySelector('.wt-label')).toBeNull();
        expect(edge()!.querySelector('.wt-meta')!.textContent!.trim()).toBe('пост · RU');

        fixture.componentRef.setInput('label', 'лист 640 px');
        fixture.detectChanges();
        expect(edge()!.querySelector('.wt-label')!.textContent!.trim()).toBe('лист 640 px');
    });

    it('clips what it holds until it is told to scroll', () => {
        expect(top().classList.contains('scrolls')).toBe(false);
        fixture.componentRef.setInput('scroll', true);
        fixture.detectChanges();
        expect(top().classList.contains('scrolls')).toBe(true);
    });

    // Worktop.jsx: the wall tone swaps the ground and keeps the lamp and the rules.
    it('keeps the pencil rules on either tone, and drops them only when asked', () => {
        fixture.componentRef.setInput('tone', 'wall');
        fixture.detectChanges();
        expect(top().getAttribute('data-tone')).toBe('wall');
        expect(top().style.getPropertyValue('--wt-grid')).toBe('var(--grid-worktop)');

        fixture.componentRef.setInput('grid', false);
        fixture.detectChanges();
        expect(top().style.getPropertyValue('--wt-grid')).toBe('');
    });

    it('draws the edge as a strip unless the screen asks for chips', () => {
        expect(top().getAttribute('data-edge')).toBe('strip');
        fixture.componentRef.setInput('edge', 'chips');
        fixture.detectChanges();
        expect(top().getAttribute('data-edge')).toBe('chips');
    });

    // "Only one Worktop per screen — a bench has one top."
    it('says so when a second top appears, and gives the slot back when one goes', () => {
        fixture.destroy();
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        try {
            const first = TestBed.createComponent(WorktopComponent);
            first.detectChanges();
            expect(warn).not.toHaveBeenCalled();

            const second = TestBed.createComponent(WorktopComponent);
            second.detectChanges();
            expect(warn).toHaveBeenCalledOnce();

            second.destroy();
            first.destroy();
            warn.mockClear();
            const replacement = TestBed.createComponent(WorktopComponent);
            replacement.detectChanges();
            expect(warn).not.toHaveBeenCalled();
            replacement.destroy();
        } finally {
            warn.mockRestore();
        }
    });

    describe('the prompt.md rules and the token contract, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.wt-edge'); });

        it('names the grid token and never draws a rule of its own', () => {
            expect(css).toMatch(/background-image:\s*var\(--wt-lamp,\s*none\),\s*var\(--wt-grid,\s*none\),\s*var\(--tex-paper\)/);
            expect(css).not.toMatch(/repeating-linear-gradient/);
            expect(css).not.toMatch(/--grid-worktop\s*:/);
        });

        it('keeps the grid under content — no overlay, no stacking order to climb', () => {
            expect(css).not.toMatch(/::(before|after)/);
            expect(css).not.toMatch(/z-index/);
            expect(css).not.toMatch(/\.wt-body[^{]*\{[^}]*background/);
        });

        it('puts the edge readout on measured ink, not on the faintest tier', () => {
            expect(css).toMatch(/\.wt-edge\s*\{[^}]*color:\s*var\(--t2\)/);
            expect(css).toMatch(/\[data-tone=wall\][^{]*\.wt-edge\s*\{[^}]*color:\s*var\(--wood-ink\)/);
            expect(css).not.toMatch(/--t3\b/);
            expect(css).not.toMatch(/--text-faint/);
        });

        it('takes both grounds by their contract names, which are the ones that are measured', () => {
            expect(css).toMatch(/background-color:\s*var\(--surface\)/);
            expect(css).toMatch(/\[data-tone=wall\][^{]*\{[^}]*background-color:\s*var\(--canvas\)/);
            expect(css).not.toMatch(/--paper-2|--wall-lo/);
        });

        it('sizes the strip from the surface, so the density lint can score it', () => {
            expect(css).toMatch(/\[data-surface=chrome\][^{]*\.wt-edge\s*\{[^}]*font-size:\s*var\(--text-chrome-sm\)/);
            expect(css).toMatch(/height:\s*var\(--bench-worktop-edge-h/);
        });

        it('spends one box-shadow, and withholds it while focused so the global halo stands', () => {
            const shadowed = [...css.matchAll(/([^{}]+)\{([^{}]*box-shadow[^{}]*)\}/g)];
            expect(shadowed.length).toBe(1);
            expect(shadowed[0][1]).toContain(':not(:focus-visible)');
            expect(shadowed[0][2]).toMatch(/box-shadow:\s*var\(--shadow-worktop-inset/);
        });

        it('paints no literal colour, and spends pixels only on hairlines and the chalk strip', () => {
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
            expect(css).not.toMatch(/rgba?\(/);
            // A token fallback is the kit's value waiting for its token; the strip's own geometry
            // (Worktop.jsx:21, hub.html:77-78) has no token in either system.
            const bare = css.replace(/var\(--[\w-]+,\s*[^)]*\)/g, '');
            const strip = new Set(['1', '2', '7', '10', '14']);
            for (const [, value] of bare.matchAll(/(\d+(?:\.\d+)?)px/g)) expect(strip.has(value), `${value}px`).toBe(true);
        });
    });
});
