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
                              [hasTray]="hasTray" (picked)="picks.push($event)"
                              (trayOpenChange)="opens.push($event)">
                   <div tray>
                       <button class="theme">theme</button>
                       <button class="appearance">appearance</button>
                       <a class="elsewhere" href="/glossary" (click)="$event.preventDefault()">glossary</a>
                   </div>
               </app-hook-rail>`,
})
class Host {
    items: HookRailItem[] = SIX;
    value = 'text';
    hooks = true;
    label = 'Screens';
    hasTray = true;
    picks: string[] = [];
    opens: boolean[] = [];
}

describe('HookRailComponent', () => {
    let fixture: ComponentFixture<Host>;
    let host: Host;
    const el = () => fixture.nativeElement as HTMLElement;
    const railEl = () => el().querySelector('app-hook-rail') as HTMLElement;
    const hooks = () => Array.from(el().querySelectorAll('a.hook')) as HTMLAnchorElement[];
    const current = () => el().querySelector('a.hook.is-current') as HTMLAnchorElement | null;
    const dots = () => el().querySelector('.dots') as HTMLButtonElement | null;
    const tray = () => el().querySelector('.tray-panel') as HTMLElement;

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

    // The wall is the only place the app says feedback has arrived, so the tally is a real
    // regression surface and not decoration (ADR-155).
    it('hangs a tally on the tool with work waiting, and nothing at all at zero', () => {
        expect(el().querySelectorAll('.tally').length).toBe(0);

        host.items = SIX.map(i => i.id === 'stats' ? { ...i, badge: 3, badgeTitle: 'New comments and reactions' } : i);
        render();
        const tallies = Array.from(el().querySelectorAll('.tally'));
        expect(tallies.length).toBe(1);
        expect(tallies[0].textContent!.trim()).toBe('3');
        expect(tallies[0].closest('a.hook')!.getAttribute('href')).toBe('/posts');

        host.items = SIX.map(i => i.id === 'stats' ? { ...i, badge: 0, badgeTitle: 'New comments and reactions' } : i);
        render();
        expect(el().querySelectorAll('.tally').length).toBe(0);
    });

    it('caps a runaway tally so it cannot outgrow the wall', () => {
        host.items = SIX.map(i => i.id === 'stats' ? { ...i, badge: 150 } : i);
        render();
        expect(el().querySelector('.tally')!.textContent!.trim()).toBe('99+');

        host.items = SIX.map(i => i.id === 'stats' ? { ...i, badge: 99 } : i);
        render();
        expect(el().querySelector('.tally')!.textContent!.trim()).toBe('99');
    });

    // An aria-label on the tally REPLACES its text, so naming what it counts without repeating the
    // number would drop the count out of the link's name — the one thing the tally is there to say.
    it('keeps the count in the hook name and says what it counts', () => {
        host.items = SIX.map(i => i.id === 'stats' ? { ...i, badge: 3, badgeTitle: 'New comments and reactions' } : i);
        render();
        const tally = el().querySelector('.tally')!;
        expect(tally.getAttribute('aria-label')).toBe('3 New comments and reactions');
        expect(tally.getAttribute('title')).toBe('New comments and reactions');

        host.items = SIX.map(i => i.id === 'stats' ? { ...i, badge: 3 } : i);
        render();
        expect(el().querySelector('.tally')!.hasAttribute('aria-label')).toBe(false);
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

    // ADR-183 — everything that is not one of the seven screens hangs here, at the foot of the
    // wall: the rare screens, the theme, Appearance. It is outside the <ul>, so the landmark still
    // announces hooks and only hooks, and it costs the seven-hook budget nothing.
    describe('the tray at the foot of the wall', () => {
        it('is not a hook, and does not join the navigation list', () => {
            expect(dots()).not.toBeNull();
            expect(railEl().querySelector('ul .dots')).toBeNull();
            expect(hooks().length).toBe(6);
        });

        it('starts closed, and says so', () => {
            expect(dots()!.getAttribute('aria-expanded')).toBe('false');
            expect(dots()!.getAttribute('aria-haspopup')).toBe('true');
            expect(tray().hasAttribute('hidden')).toBe(true);
        });

        it('opens and closes on the button, reporting each move once', () => {
            dots()!.click();
            render();
            expect(tray().hasAttribute('hidden')).toBe(false);
            expect(dots()!.getAttribute('aria-expanded')).toBe('true');

            dots()!.click();
            render();
            expect(tray().hasAttribute('hidden')).toBe(true);
            expect(host.opens).toEqual([true, false]);
        });

        // Only the button that opened it and the panel itself hold it open. A hook clicked beside
        // it is a navigation, and a panel that survived one would hang over what it opened.
        it('closes on a click anywhere but its own button and panel', () => {
            dots()!.click();
            render();
            railEl().dispatchEvent(new MouseEvent('click', { bubbles: true }));
            render();
            expect(tray().hasAttribute('hidden')).toBe(true);

            dots()!.click();
            render();
            document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            render();
            expect(tray().hasAttribute('hidden')).toBe(true);
        });

        // What separates the two entries is whether the click leaves the page. Theme and
        // Appearance act on the screen in front of you and the panel stays put.
        it('stays open under an entry that acts in place, and goes with one that navigates', () => {
            dots()!.click();
            render();
            (el().querySelector('.theme') as HTMLButtonElement).click();
            render();
            expect(tray().hasAttribute('hidden')).toBe(false);

            (el().querySelector('.elsewhere') as HTMLAnchorElement).dispatchEvent(
                new MouseEvent('click', { bubbles: true }));
            render();
            expect(tray().hasAttribute('hidden')).toBe(true);
        });

        // The WAI-ARIA menu-button pattern: Escape closes and hands focus back to the button.
        // Without it [hidden] takes the focused entry out of the tree and focus lands on <body>.
        it('closes on Escape and gives focus back to the button that opened it', () => {
            dots()!.click();
            render();
            (el().querySelector('.theme') as HTMLButtonElement).focus();

            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
            render();
            expect(tray().hasAttribute('hidden')).toBe(true);
            expect(document.activeElement).toBe(dots());
        });

        // Escape belongs to whoever holds focus. A press from elsewhere still shuts the panel, but
        // pulling focus into the wall would be taking it from that elsewhere.
        it('shuts on an Escape from outside without reaching for focus', () => {
            dots()!.click();
            render();
            const outside = document.createElement('button');
            document.body.append(outside);
            outside.focus();

            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
            render();
            expect(tray().hasAttribute('hidden')).toBe(true);
            expect(document.activeElement).toBe(outside);
            outside.remove();
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

        it('hasTray=false drops the button', () => {
            host.hasTray = false;
            render();
            expect(dots()).toBeNull();
        });
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

        // Ink follows the ground it lands on. Type straight on the wall takes the cream; a rule that
        // paints its own ground takes the ink that ground was measured against, and a ground with no
        // measured ink fails here rather than shipping unscored.
        it('is a pegboard, and every ink matches the ground under it', () => {
            expect(css).toMatch(/background:\s*var\(--pegboard\)/);
            const INK_ON = new Map([
                ['var(--pegboard)', 'var(--rail-ink)'],
                ['var(--hook-face)', 'var(--rail-ink)'],
                // The index tabs' badge pair, byte for byte — one badge across the chrome (ADR-155).
                ['var(--tab-badge)', 'var(--rail-edge)'],
            ]);
            const rules = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g));
            const inked = rules.filter(m => /(^|[^-])color:/.test(m[2]));
            expect(inked.length).toBeGreaterThan(0);
            let onPaper = 0;
            for (const m of inked) {
                const ink = m[2].match(/(^|[^-])color:\s*([^;}]+)/)![2].trim();
                // The tray panel is paper pinned to the wall, not wall: it declares its own sheet
                // ground, and the wall's ink rule stops at the edge of what hangs off it.
                if (/background-color:\s*var\(--sheet\)/.test(m[2])) {
                    expect(ink).toBe('var(--text)');
                    onPaper++;
                    continue;
                }
                const ground = m[2].match(/(^|[^-])background:\s*([^;}]+)/)?.[2].trim();
                expect(ink, `ink on ${ground ?? 'the wall'} in "${m[1].trim()}"`)
                    .toBe(ground ? INK_ON.get(ground) : 'var(--rail-ink)');
            }
            expect(onPaper).toBe(1);
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
