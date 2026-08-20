import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RailHeaderComponent } from './rail-header.component';

function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n');
}

@Component({
    imports: [RailHeaderComponent],
    template: `<app-rail-header [version]="version" [project]="project" [projectHint]="hint"
                                [crumbs]="crumbs" [hasMenu]="hasMenu"
                                (projectClicked)="switches = switches + 1"
                                (menuOpenChange)="opens.push($event)">
                   <span class="save">saved 09:51</span>
                   <button menu class="theme">theme</button>
                   <button menu class="appearance">appearance</button>
                   <span account class="avatar">M</span>
               </app-rail-header>`,
})
class Host {
    version = '';
    project = '';
    hint = '';
    crumbs: string[] = [];
    hasMenu = true;
    switches = 0;
    opens: boolean[] = [];
}

describe('RailHeaderComponent', () => {
    let fixture: ComponentFixture<Host>;
    let host: Host;
    const el = () => fixture.nativeElement as HTMLElement;
    const rail = () => el().querySelector('app-rail-header') as HTMLElement;
    const tile = () => el().querySelector('.tile') as HTMLButtonElement | null;
    const dots = () => el().querySelector('.dots') as HTMLButtonElement | null;
    const menu = () => el().querySelector('.menu') as HTMLElement;

    const render = () => { fixture.changeDetectorRef.markForCheck(); fixture.detectChanges(); };

    beforeEach(async () => {
        await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
        fixture = TestBed.createComponent(Host);
        host = fixture.componentInstance;
        render();
    });

    it('is chrome, and the screen banner', () => {
        expect(rail().getAttribute('data-surface')).toBe('chrome');
        expect(rail().getAttribute('role')).toBe('banner');
    });

    it('names the product, and shows a version only when given one', () => {
        expect(el().querySelector('.brand')!.textContent!.trim()).toBe('Cedar Clerk');
        expect(el().querySelector('.version')).toBeNull();

        host.version = 'v0.12.0';
        render();
        expect(el().querySelector('.version')!.textContent!.trim()).toBe('v0.12.0');
    });

    it('renders the switcher only when a project is on show', () => {
        expect(tile()).toBeNull();

        host.project = 'Cedar Quest';
        host.hint = 'Switch project';
        render();
        expect(tile()!.textContent).toContain('Cedar Quest');
        expect(tile()!.getAttribute('title')).toBe('Switch project');

        tile()!.click();
        expect(host.switches).toBe(1);
    });

    // WCAG 2.5.3: the visible project name has to be inside the accessible name, so the purpose
    // rides on aria-haspopup and never on an aria-label that replaces the name.
    it('leaves the project name as the switcher accessible name', () => {
        host.project = 'Cedar Quest';
        host.hint = 'Switch project';
        render();
        expect(tile()!.hasAttribute('aria-label')).toBe(false);
        expect(tile()!.getAttribute('aria-haspopup')).toBe('true');
    });

    it('walks the crumbs and marks the last one as the page', () => {
        expect(el().querySelector('.crumbs')).toBeNull();

        host.crumbs = ['Cedar Quest', 'Devlog #12'];
        render();
        const items = Array.from(el().querySelectorAll('.crumbs li'));
        expect(items.map(i => i.textContent!.trim())).toEqual(['Cedar Quest', 'Devlog #12']);
        expect(items[0].getAttribute('aria-current')).toBeNull();
        expect(items[1].getAttribute('aria-current')).toBe('page');
    });

    it('projects the save state, the account and the menu entries into their own slots', () => {
        expect(el().querySelector('.save')!.textContent).toContain('saved 09:51');
        expect(el().querySelector('.avatar')!.textContent!.trim()).toBe('M');
        expect(menu().querySelectorAll('button').length).toBe(2);
    });

    describe('the dots menu — the slot the prompt file reserves for the rare rest', () => {
        it('starts closed, and says so', () => {
            expect(dots()!.getAttribute('aria-expanded')).toBe('false');
            expect(dots()!.getAttribute('aria-haspopup')).toBe('true');
            expect(menu().hasAttribute('hidden')).toBe(true);
        });

        it('opens and closes on the button, reporting each move once', () => {
            dots()!.click();
            render();
            expect(menu().hasAttribute('hidden')).toBe(false);
            expect(dots()!.getAttribute('aria-expanded')).toBe('true');

            dots()!.click();
            render();
            expect(menu().hasAttribute('hidden')).toBe(true);
            expect(host.opens).toEqual([true, false]);
        });

        it('closes on Escape and on a click outside, but not on one inside', () => {
            dots()!.click();
            render();
            rail().dispatchEvent(new MouseEvent('click', { bubbles: true }));
            render();
            expect(menu().hasAttribute('hidden')).toBe(false);

            document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            render();
            expect(menu().hasAttribute('hidden')).toBe(true);

            dots()!.click();
            render();
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
            render();
            expect(menu().hasAttribute('hidden')).toBe(true);
        });

        // The controls the shell hands to this slot own their own state, so the panel is hidden
        // rather than destroyed and the same elements come back on reopen.
        it('keeps the projected controls alive while closed', () => {
            const before = el().querySelector('.theme');
            dots()!.click();
            render();
            dots()!.click();
            render();
            expect(el().querySelector('.theme')).toBe(before);
        });

        it('hasMenu=false drops the button and keeps the slot', () => {
            host.hasMenu = false;
            render();
            expect(dots()).toBeNull();
            expect(menu().querySelectorAll('button').length).toBe(2);
        });
    });

    describe('the prompt.md rules, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.tile'); });

        // "Text on the rail is always --rail-ink; never put ink-on-paper colors here." The board
        // pinned under the rail is the one thing that is not the rail, and it takes paper ink for
        // the same reason it takes a paper surface. Which rule that is comes from what the rule
        // paints, never from its name, and the count is pinned so a second one cannot appear
        // quietly: a cream label on cream paper is invisible, which is the defect this caught.
        it('paints every colour on the rail with the cream, and spends the soft cream on a separator', () => {
            const rules = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g));
            const inked = rules.filter(m => /(^|[^-])color:/.test(m[2]));
            expect(inked.length).toBeGreaterThan(0);
            let onPaper = 0;
            for (const m of inked) {
                const value = m[2].match(/(^|[^-])color:\s*([^;}]+)/)![2].trim();
                if (/background-color:\s*var\(--sheet\)/.test(m[2])) {
                    expect(value).toBe('var(--text)');
                    onPaper++;
                    continue;
                }
                expect(['var(--rail-ink)', 'var(--rail-ink-soft)']).toContain(value);
                if (value === 'var(--rail-ink-soft)') expect(m[1]).toContain('::before');
            }
            expect(onPaper).toBe(1);
        });

        it('holds the chrome band: 13/11px type and a 30px box', () => {
            const sizes = Array.from(css.matchAll(/font-size:\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(sizes.length).toBeGreaterThan(0);
            for (const s of sizes) expect(['var(--text-chrome)', 'var(--text-chrome-sm)']).toContain(s);
            expect(css).toMatch(/min-height:\s*var\(--hit-chrome\)/);
            expect(css).toMatch(/height:\s*var\(--bench-rail-h\)/);
        });

        it('numbers are mono', () => {
            expect(css).toMatch(/\.version[^{]*\{[^}]*font-family:\s*var\(--font-mono\)/);
        });

        it('is sticky, and never lets the paper card overlap the rail', () => {
            expect(css).toMatch(/position:\s*sticky/);
            expect(css).toMatch(/z-index:\s*10/);
        });

        it('withholds a focusable control shadow under focus, so the ADR-140 halo is not out-specified', () => {
            const shadowed = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g)).filter(m => m[2].includes('box-shadow'));
            const controls = shadowed.filter(m => m[1].includes('.tile') || m[1].includes('.dots'));
            expect(controls.length).toBeGreaterThan(0);
            for (const m of controls) expect(m[1]).toContain(':not(:focus-visible)');
        });

        it('paints no literal colour', () => {
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
        });
    });
});
