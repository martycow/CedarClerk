import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { HookRailComponent, HookRailItem } from './hook-rail.component';

function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n');
}

const SIX: HookRailItem[] = [
    { id: 'hub', icon: 'squares-four', label: 'Hub', link: '/projects' },
    { id: 'text', icon: 'pencil-simple', label: 'Text', link: '/drafts' },
    { id: 'board', icon: 'kanban', label: 'Board', link: '/projects/1/tasks' },
    { id: 'assets', icon: 'images', label: 'Assets', link: '/library' },
    { id: 'stats', icon: 'chart-bar', label: 'Metrics', link: '/posts' },
    { id: 'settings', icon: 'gear', label: 'Settings', link: '/settings', end: true },
];

@Component({
    imports: [HookRailComponent],
    template: `<app-hook-rail [items]="items" [value]="value" [hooks]="hooks" [label]="label"
                              (picked)="picks.push($event)" />`,
})
class Host {
    items: HookRailItem[] = SIX;
    value = 'text';
    hooks = true;
    label = 'Screens';
    picks: string[] = [];
}

describe('HookRailComponent', () => {
    let fixture: ComponentFixture<Host>;
    let host: Host;
    const el = () => fixture.nativeElement as HTMLElement;
    const railEl = () => el().querySelector('app-hook-rail') as HTMLElement;
    const hooks = () => Array.from(el().querySelectorAll('a.hook')) as HTMLAnchorElement[];
    const current = () => el().querySelector('a.hook.is-current') as HTMLAnchorElement | null;

    const render = () => { fixture.changeDetectorRef.markForCheck(); fixture.detectChanges(); };

    beforeEach(async () => {
        await TestBed.configureTestingModule({ imports: [Host], providers: [provideRouter([])] }).compileComponents();
        // The hooks are real links, so a click in a test starts a real navigation that outlives
        // the fixture. What is under test here is the rail, not the router.
        vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        fixture = TestBed.createComponent(Host);
        host = fixture.componentInstance;
        render();
    });

    it('is chrome, and a named navigation landmark', () => {
        expect(railEl().getAttribute('data-surface')).toBe('chrome');
        expect(railEl().getAttribute('role')).toBe('navigation');
        expect(railEl().getAttribute('aria-label')).toBe('Screens');
    });

    // A hook moves between screens, so it is a link and not a button: middle click and
    // open-in-new-tab are navigation affordances the browser gives away for free.
    it('hangs one real link per tool', () => {
        expect(hooks().length).toBe(6);
        expect(hooks().map(a => a.getAttribute('href')))
            .toEqual(['/projects', '/drafts', '/projects/1/tasks', '/library', '/posts', '/settings']);
    });

    it('marks the current screen three ways, only one of which is a colour', () => {
        expect(current()!.getAttribute('href')).toBe('/drafts');
        expect(hooks().filter(a => a.hasAttribute('aria-current')).length).toBe(1);
        expect(current()!.getAttribute('aria-current')).toBe('page');
        expect(current()!.querySelector('.here')).not.toBeNull();

        host.value = 'assets';
        render();
        expect(current()!.getAttribute('href')).toBe('/library');
    });

    it('takes its accessible name from the caption, and from the title only when there is none', () => {
        expect(hooks()[0].textContent).toContain('Hub');
        expect(hooks()[0].hasAttribute('aria-label')).toBe(false);
        expect(hooks()[0].getAttribute('title')).toBe('Hub');

        host.items = [{ id: 'hub', icon: 'squares-four', title: 'Hub', link: '/projects' }];
        render();
        expect(hooks()[0].getAttribute('aria-label')).toBe('Hub');
        expect(hooks()[0].textContent!.trim()).toBe('');
    });

    it('anchors a tail tool to the bottom of one list', () => {
        expect(el().querySelectorAll('ul').length).toBe(1);
        const rows = Array.from(el().querySelectorAll('li'));
        expect(rows.filter(r => r.classList.contains('is-tail')).length).toBe(1);
        expect(rows[rows.length - 1].classList).toContain('is-tail');
    });

    it('hangs a brass hook off every tool, and drops them all on request', () => {
        expect(el().querySelectorAll('app-brass-hook').length).toBe(6);
        host.hooks = false;
        render();
        expect(el().querySelectorAll('app-brass-hook').length).toBe(0);
    });

    it('reports which tool was taken off the wall', () => {
        hooks()[3].click();
        expect(host.picks).toEqual(['assets']);
    });

    // "Keep it to 5-7 tools; a wall with everything on it is a wall you stop reading."
    it('says so when the wall is overloaded', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        host.items = [...SIX, { id: 'x', icon: 'flag', label: 'X', link: '/x' },
                              { id: 'y', icon: 'flask', label: 'Y', link: '/y' }];
        render();
        expect(warn).toHaveBeenCalledTimes(1);
        expect(warn.mock.calls[0][0]).toContain('8 hooks');
        warn.mockRestore();
    });

    it('stays quiet inside the budget', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        host.value = 'hub';
        render();
        expect(warn).not.toHaveBeenCalled();
        warn.mockRestore();
    });

    describe('the prompt.md rules, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.hook'); });

        it('is 52px of wall, and every tool clears the 30px chrome floor', () => {
            expect(css).toMatch(/width:\s*var\(--bench-tool-w\)/);
            expect(css).toMatch(/min-height:\s*var\(--hit-chrome\)/);
        });

        it('holds the chrome band, which is what costs the kit its 8.5px caption', () => {
            const sizes = Array.from(css.matchAll(/font-size:\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(sizes.length).toBeGreaterThan(0);
            for (const s of sizes) expect(['var(--text-chrome)', 'var(--text-chrome-sm)']).toContain(s);
        });

        it('is a pegboard, and paints ink on the rail with the cream', () => {
            expect(css).toMatch(/background:\s*var\(--pegboard\)/);
            const rules = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g));
            const inked = rules.filter(m => /(^|[^-])color:/.test(m[2]));
            expect(inked.length).toBeGreaterThan(0);
            for (const m of inked) {
                const value = m[2].match(/(^|[^-])color:\s*([^;}]+)/)![2].trim();
                expect(value).toBe('var(--rail-ink)');
            }
        });

        it('marks the current tool with a shape as well as a fill', () => {
            expect(css).toMatch(/is-current[^{]*\.here[^{]*\{[^}]*background:\s*var\(--grad-brass\)/);
        });

        it('withholds the current tool shadow under focus, so the ADR-140 halo is not out-specified', () => {
            const shadowed = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g)).filter(m => m[2].includes('box-shadow'));
            const controls = shadowed.filter(m => m[1].includes('.hook'));
            expect(controls.length).toBeGreaterThan(0);
            for (const m of controls) expect(m[1]).toContain(':not(:focus-visible)');
        });

        it('paints no literal colour', () => {
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
        });
    });
});
