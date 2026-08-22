import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LogLevel, LogLineComponent, LogSurface } from './log-line.component';

// The component's own stylesheet, read back out of the document. This component's kit rule is a
// rule about CSS — one line, the anchor in `at` rather than in the sentence — and its ADR-157 rule
// is two declared type sizes, so the port can only be held to either by reading what shipped. The
// length assertion is the control: without it a renamed class would make every rule pass over an
// empty string.
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

@Component({
    imports: [LogLineComponent],
    template: `
        <app-log-line [level]="level()" [word]="word()" [surface]="surface()"
                      time="17.08 23:48" at="блок 5">
            Изображение без описания — уйдёт без alt
        </app-log-line>
    `,
})
class JournalHost {
    readonly level = signal<LogLevel>('info');
    readonly word = signal('');
    readonly surface = signal<LogSurface>('paper');
}

describe('LogLineComponent', () => {
    function host() {
        const fixture = TestBed.createComponent(JournalHost);
        fixture.detectChanges();
        return fixture;
    }

    function create() {
        const fixture = TestBed.createComponent(LogLineComponent);
        fixture.detectChanges();
        return fixture;
    }

    // A component's stylesheet reaches the document when it first renders, and TestBed takes the
    // document apart between tests — so a rule is read off a line that exists right now.
    function renderedCss(): string {
        create();
        return sheetFor('.ll-msg');
    }

    it('lies on paper until a consumer says the container is chrome', () => {
        const fixture = create();
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('paper');

        fixture.componentRef.setInput('surface', 'chrome');
        fixture.detectChanges();
        expect(fixture.nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('writes the time, the stamp, the sentence and the anchor in that order', () => {
        const line = host().nativeElement.querySelector('app-log-line') as HTMLElement;
        const parts = [...line.children].map(c => c.tagName.toLowerCase());
        expect(parts).toEqual(['span', 'app-stamp-badge', 'span', 'span']);
        expect(line.querySelector('.ll-time')!.textContent!.trim()).toBe('17.08 23:48');
        expect(line.querySelector('.ll-msg')!.textContent!.trim()).toBe('Изображение без описания — уйдёт без alt');
        expect(line.querySelector('.ll-at')!.textContent!.trim()).toBe('блок 5');
    });

    it('drops the time and the anchor when a line has neither', () => {
        const el = create().nativeElement as HTMLElement;
        expect(el.querySelector('.ll-time')).toBeNull();
        expect(el.querySelector('.ll-at')).toBeNull();
        expect(el.querySelector('app-stamp-badge')).not.toBeNull();
    });

    it('stamps each level with its own tone', () => {
        const fixture = create();
        const tones: Record<LogLevel, string> = { ok: 'pine', warn: 'rust', info: 'ink', build: 'brass' };
        for (const [level, tone] of Object.entries(tones)) {
            fixture.componentRef.setInput('level', level);
            fixture.detectChanges();
            expect(fixture.nativeElement.querySelector('app-stamp-badge')!.getAttribute('data-tone')).toBe(tone);
        }
    });

    it('lets a line whose severity and colour disagree override the tone', () => {
        const fixture = create();
        fixture.componentRef.setInput('level', 'build');
        fixture.componentRef.setInput('tone', 'rust');
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('app-stamp-badge')!.getAttribute('data-tone')).toBe('rust');
    });

    it('never stamps a wordless plate: the level id stands in until a consumer translates one', () => {
        const fixture = host();
        const stamp = () => fixture.nativeElement.querySelector('app-stamp-badge')!.textContent!.trim();
        expect(stamp()).toBe('INFO');

        fixture.componentInstance.word.set('ВНИМ.');
        fixture.componentInstance.level.set('warn');
        fixture.detectChanges();
        expect(stamp()).toBe('ВНИМ.');
    });

    // The kit's rule: the severity is a rubber stamp, not a coloured dot — no tinted row, no bar
    // down the left edge, no level-keyed class the journal could grow a vocabulary on. Stripping
    // the stamp out and comparing what is left says it about the rendered row rather than about
    // the four rules someone remembered not to write.
    it('carries the severity in the stamp and nowhere else', () => {
        const fixture = host();
        const withoutStamp = () => {
            const clone = (fixture.nativeElement.querySelector('app-log-line') as HTMLElement).cloneNode(true) as HTMLElement;
            clone.querySelector('app-stamp-badge')!.remove();
            return clone.outerHTML;
        };

        fixture.componentInstance.level.set('info');
        fixture.detectChanges();
        const plain = withoutStamp();

        for (const level of ['ok', 'warn', 'build'] as const) {
            fixture.componentInstance.level.set(level);
            fixture.detectChanges();
            expect(withoutStamp(), `${level} styled the row itself`).toBe(plain);
        }
    });

    it('holds the sentence to one line, which is what puts the anchor in `at`', () => {
        const css = renderedCss();
        expect(css).toMatch(/\.ll-msg[^{]*\{[^}]*white-space:\s*nowrap/);
        expect(css).toMatch(/\.ll-msg[^{]*\{[^}]*text-overflow:\s*ellipsis/);
    });

    it('declares a type size on each surface, so the density lint scores both (ADR-157)', () => {
        const css = renderedCss();
        // The quotes come off in the shipped sheet — what has to be there is the attribute.
        expect(css).toMatch(/\[data-surface="?paper"?\][^{]*\{[^}]*font-size:\s*var\(--fs-ui\)/);
        expect(css).toMatch(/\[data-surface="?chrome"?\][^{]*\{[^}]*font-size:\s*var\(--text-chrome-sm\)/);
    });

    it('declares no touch floor on either surface — a line is not a control', () => {
        const css = renderedCss();
        expect(css).not.toMatch(/min-height:/);
        const widths = [...css.matchAll(/min-width:\s*([^;}]+)/g)].map(m => m[1].trim());
        for (const width of widths) expect(width).toBe('0');
    });

    it('keeps one ink pair across both surfaces: the split governs size, never contrast', () => {
        const css = renderedCss();
        const inks = [...css.matchAll(/([^{}]*)\{[^}]*[\s;{]color:\s*([^;}]+)/g)]
            .map(m => ({ selector: m[1].trim(), ink: m[2].trim() }));
        expect(inks.length).toBeGreaterThan(0);
        for (const { selector, ink } of inks) {
            expect(['var(--text)', 'var(--t2)']).toContain(ink);
            expect(selector).not.toMatch(/data-surface/);
        }
    });

    it('paints no literal colour and no loose pixel beyond the stamp hooks', () => {
        const css = renderedCss();
        expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
        // The --stamp-* hooks carry the kit's compact stamp geometry (LogLine.jsx:20) into the badge.
        expect(css.replace(/--stamp-[\w-]+:\s*[^;}]+/g, '')).not.toMatch(/\d+px/);
    });
});
