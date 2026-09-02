import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PaperCardComponent } from '../display/paper-card.component';
import { WorktopComponent } from './worktop.component';

// The component's own stylesheet, read back out of the document. Emulated encapsulation rewrites
// what was authored — :host becomes an [_nghost-…] attribute and every other selector gains an
// [_ngcontent-…] one — so the shim is undone before the rules are read.
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

    it('is chrome — its caption line is measured as chrome', () => {
        expect(top().getAttribute('data-surface')).toBe('chrome');
    });

    // The trap: a chrome box holding paper. Without the body restating the axis the sheet lying
    // in it would inherit chrome's numbers.
    it('does not leak chrome onto what lies on it', () => {
        expect(body().getAttribute('data-surface')).toBe('paper');

        fixture.destroy();
        const projected = TestBed.createComponent(SheetOnTopHost);
        projected.detectChanges();
        const card = projected.nativeElement.querySelector('app-paper-card') as HTMLElement;
        const chain: string[] = [];
        for (let node: HTMLElement | null = card; node; node = node.parentElement) {
            if (node.hasAttribute('data-surface')) chain.unshift(node.getAttribute('data-surface')!);
        }
        expect(chain.length, 'the card carries no surface chain at all').toBeGreaterThan(1);
        expect(chain[0], 'the top itself is the frame').toBe('chrome');
        expect(chain.indexOf('chrome', 1), `chrome comes back under the body: ${chain.join(' > ')}`).toBe(-1);
        projected.destroy();
    });

    it('draws the caption line only for what it was given', () => {
        expect(edge()).toBeNull();

        fixture.componentRef.setInput('meta', 'пост · RU');
        fixture.detectChanges();
        expect(edge()!.querySelector('.wt-label')).toBeNull();
        expect(edge()!.querySelector('.wt-meta')!.textContent!.trim()).toBe('пост · RU');

        fixture.componentRef.setInput('label', 'лист 640 px');
        fixture.detectChanges();
        const label = edge()!.querySelector('.wt-label') as HTMLElement;
        expect(label.textContent!.trim()).toBe('лист 640 px');
        expect(label.classList).toContain('label');
    });

    it('clips what it holds until it is told to scroll', () => {
        expect(top().classList.contains('scrolls')).toBe(false);
        fixture.componentRef.setInput('scroll', true);
        fixture.detectChanges();
        expect(top().classList.contains('scrolls')).toBe(true);
    });

    // ADR-239 clause 2 — the lamp, the grid and the chalk edge are gone; the inputs stay so the
    // pages that name them still compile, and they paint nothing.
    it('paints neither lamp nor grid whatever it is told', () => {
        expect(top().style.getPropertyValue('--wt-lamp')).toBe('');
        expect(top().style.getPropertyValue('--wt-grid')).toBe('');
        fixture.componentRef.setInput('lamp', true);
        fixture.componentRef.setInput('grid', true);
        fixture.detectChanges();
        expect(top().style.getPropertyValue('--wt-lamp')).toBe('');
        expect(top().style.getPropertyValue('--wt-grid')).toBe('');
    });

    it('keeps its tone and edge attributes for the pages that read them', () => {
        expect(top().getAttribute('data-tone')).toBe('paper');
        expect(top().getAttribute('data-edge')).toBe('strip');
        fixture.componentRef.setInput('tone', 'wall');
        fixture.componentRef.setInput('edge', 'chips');
        fixture.detectChanges();
        expect(top().getAttribute('data-tone')).toBe('wall');
        expect(top().getAttribute('data-edge')).toBe('chips');
    });

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

    describe('the card, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.wt-edge'); });

        it('is a plain card with no wood, no lamp and no rules', () => {
            expect(css).not.toMatch(/--lamp|--tex-wood|--wood-edge|--rule-ink|repeating-linear-gradient/);
            expect(css).toMatch(/background-color:\s*var\(--sheet\)/);
            expect(css).toMatch(/border:\s*1px solid var\(--border\)/);
        });

        it('puts the caption readout on measured ink, not on the faintest tier', () => {
            expect(css).toMatch(/\.wt-edge\s*\{[^}]*color:\s*var\(--t2\)/);
            expect(css).not.toMatch(/--text-faint/);
        });

        it('sizes the caption line from the surface, so the density lint can score it', () => {
            expect(css).toMatch(/\[data-surface=chrome\][^{]*\.wt-meta\s*\{[^}]*font-size:\s*var\(--text-chrome\)/);
            expect(css).toMatch(/min-height:\s*var\(--hit-chrome\)/);
        });

        it('spends one box-shadow, and withholds it while focused so the global halo stands', () => {
            const shadowed = [...css.matchAll(/([^{}]+)\{([^{}]*box-shadow[^{}]*)\}/g)];
            expect(shadowed.length).toBe(1);
            expect(shadowed[0][1]).toContain(':not(:focus-visible)');
            expect(shadowed[0][2]).toMatch(/box-shadow:\s*var\(--shadow\)/);
        });

        it('paints no literal colour', () => {
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
            expect(css).not.toMatch(/rgba?\(/);
        });
    });
});
