import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { CommentsService } from '../../core/comments.service';
import { ThemeService } from '../../core/theme.service';
import { BenchShellComponent } from './bench-shell.component';

@Component({ template: '' })
class Blank {}

describe('bench shell', () => {
    let fixture: ComponentFixture<BenchShellComponent>;
    let router: Router;
    const el = () => fixture.nativeElement as HTMLElement;
    const hooks = () => [...el().querySelectorAll('app-hook-rail .hook')] as HTMLAnchorElement[];
    const crumbs = () => [...el().querySelectorAll('app-rail-header .crumbs li')].map(li => li.textContent?.trim());
    const menuItems = () => [...el().querySelectorAll('app-hook-rail .menu-item')] as HTMLElement[];
    // The theme entry carries its sun/moon glyph in the same element as its label.
    const menuText = (i: Element) => (i.textContent ?? '').replace(/\s+/g, ' ').trim();
    const menuItem = (label: string) => menuItems().find(i => menuText(i).includes(label))!;

    async function go(url: string) {
        await router.navigateByUrl(url);
        fixture.detectChanges();
    }

    /** Answers the one project list the shell asks for per session (ADR-186). */
    async function flushProjects(list: { id: string; name: string }[]) {
        TestBed.inject(HttpTestingController).expectOne(r => r.url.startsWith('/api/projects')).flush(list);
        // The service hands the list back through a promise; without settling it the names signal
        // is still empty when the view is checked.
        await fixture.whenStable();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [BenchShellComponent],
            providers: [
                provideRouter([{ path: '**', component: Blank }]),
                provideHttpClient(),
                provideHttpClientTesting(),
            ],
        }).compileComponents();
        router = TestBed.inject(Router);
        // The open project outlives a reload by design, which means it outlives a test too.
        localStorage.removeItem('cedar-project');
        fixture = TestBed.createComponent(BenchShellComponent);
        fixture.detectChanges();
    });

    it('draws the wall, the board, the ground and the bottom strip', () => {
        expect(el().querySelector('app-hook-rail')).toBeTruthy();
        expect(el().querySelector('app-rail-header')).toBeTruthy();
        expect(el().querySelector('main.body router-outlet')).toBeTruthy();
        expect(el().querySelector('.bench-bottom app-debug-console')).toBeTruthy();
        expect(el().querySelector('.bench-bottom app-ruler-bar')).toBeTruthy();
    });

    // ADR-138 item 5's carve-out keys on this attribute rather than on a class, so the page's own
    // controls keep the 44px touch floor while the chrome nested inside them stays at 30px. Without
    // it the coarse-pointer rule reaches nothing at all, which is the same thing as deleting it.
    it('stands every screen on paper', () => {
        const main = el().querySelector('main.body') as HTMLElement;
        expect(main.getAttribute('data-surface')).toBe('paper');
        expect(main.querySelector('router-outlet')).toBeTruthy();
    });

    // Retiring the page header took the only global unread-feedback badge with it; the wall is
    // where it lives now, and the shell is what asks for the number (ADR-155).
    it('hangs the unread-feedback tally on the metrics tool, and nothing at zero', () => {
        const feedback = TestBed.inject(CommentsService);
        expect(el().querySelectorAll('app-hook-rail .tally').length).toBe(0);

        feedback.newComments.set(2);
        feedback.newReactions.set(1);
        fixture.detectChanges();
        const tally = el().querySelector('app-hook-rail .tally')!;
        expect(tally.textContent!.trim()).toBe('3');
        expect(tally.closest('a.hook')!.getAttribute('href')).toBe('/posts');

        feedback.newComments.set(0);
        feedback.newReactions.set(0);
        fixture.detectChanges();
        expect(el().querySelectorAll('app-hook-rail .tally').length).toBe(0);
    });

    it('asks for the count itself, so the tally is right on whatever screen opened', () => {
        const seen = TestBed.inject(HttpTestingController).match('/api/comments/new-count');
        expect(seen.length).toBe(1);
    });

    it('hangs no project tool on the wall while the module is off', () => {
        const ids = hooks().map(a => a.textContent?.trim());
        expect(ids).toEqual(['Docs', 'Calendar', 'Assets', 'Metrics', 'Settings']);
        expect(el().querySelector('app-rail-header .tile')).toBeFalsy();
    });

    it('keeps project tools off the wall until a project has been selected', () => {
        TestBed.inject(AuthService).indieDev.set(true);
        fixture.detectChanges();
        const labels = hooks().map(a => a.textContent?.trim());
        expect(labels).toEqual(['Hub', 'Settings']);
        // Settings is the tail, and it is anchored inside the one list rather than a second one.
        expect(hooks().at(-1)!.closest('li')!.classList).toContain('is-tail');
    });

    it('lights every project tool and the account-wide routes it belongs to', async () => {
        const lit = () => hooks().find(a => a.getAttribute('aria-current') === 'page')?.textContent?.trim();
        TestBed.inject(AuthService).indieDev.set(true);

        await go('/projects/p1');
        await go('/drafts');
        expect(lit()).toBe('Docs');
        await go('/editor');
        expect(lit()).toBe('Docs');
        await go('/library');
        expect(lit()).toBe('Assets');
        await go('/calendar');
        expect(lit()).toBe('Calendar');
        await go('/posts');
        expect(lit()).toBe('Metrics');
        await go('/settings');
        expect(lit()).toBe('Settings');
        // The board is a child of the hub, so the longer pattern has to be tried first.
        await go('/projects');
        expect(lit()).toBe('Hub');
        await go('/projects/p1/tasks');
        expect(lit()).toBe('Board');
        await go('/projects/p1/planner');
        expect(lit()).toBe('Planner');
        await go('/projects/p1/builds');
        expect(lit()).toBe('Builds');
        await go('/projects/p1/assets');
        expect(lit()).toBe('Assets');
        // The board is a child of the canvas the same way the canvas is a child of the project,
        // so both lengths have to be tried before the project's own prefix.
        await go('/projects/p1/canvas');
        expect(lit()).toBe('Canvas');
        await go('/projects/p1/canvas/b1');
        expect(lit()).toBe('Canvas');
        // Everything behind the dots menu leaves the wall unlit rather than guessing a hook.
        await go('/glossary');
        expect(lit()).toBeUndefined();
    });

    it('adds the complete project tool set after a project has been opened', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/drafts');
        expect(hooks().find(a => a.textContent?.trim() === 'Board')).toBeUndefined();

        await go('/projects/p1/planner');
        expect(hooks().map(a => a.textContent?.trim()))
            .toEqual(['Hub', 'Docs', 'Board', 'Planner', 'Builds', 'Assets', 'Canvas', 'Site', 'Calendar', 'Metrics', 'Settings']);
        expect(hooks().find(a => a.textContent?.trim() === 'Board')!.getAttribute('href'))
            .toBe('/projects/p1/tasks');
    });

    // ADR-186 — the whole point: which project is open is session state, so it survives leaving
    // the project's own routes. Before this the hook opened a board on two screens out of eleven.
    it('keeps project tools but removes the switcher tile after leaving project routes', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1/planner');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);

        await go('/drafts');
        expect(hooks().find(a => a.textContent?.trim() === 'Board')!.getAttribute('href'))
            .toBe('/projects/p1/tasks');
        expect(el().querySelector('app-rail-header .tile')).toBeFalsy();
    });

    // With nothing to switch between the tile stays the plain link it has always been; hand it a
    // list and it becomes the switcher the rail's own description calls it.
    it('turns the tile into a switcher once there are projects to switch to', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1');
        expect((el().querySelector('app-rail-header .tile') as HTMLElement).tagName).toBe('A');

        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);
        const tile = el().querySelector('app-rail-header .tile') as HTMLButtonElement;
        expect(tile.tagName).toBe('BUTTON');
        expect(tile.getAttribute('aria-haspopup')).toBe('true');

        const entries = [...el().querySelectorAll('app-rail-header .switcher-item')] as HTMLAnchorElement[];
        expect(entries.map(a => a.textContent?.trim())).toEqual(['Cedar Quest', 'Second', 'All projects']);
        // Anchors, so a middle click still opens a project in a tab — the reason the tile itself
        // used to be one, kept where it actually matters.
        expect(entries.map(a => a.getAttribute('href')))
            .toEqual(['/projects/p1', '/projects/p2', '/projects']);
    });

    // The switch keeps the open screen: every page under /projects/:id refetches off paramMap, so
    // the same child segment under another project id is a live screen, not a stale one.
    it('carries the open project screen across the switcher', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1/assets');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);
        const entries = () => [...el().querySelectorAll('app-rail-header .switcher-item')] as HTMLAnchorElement[];
        expect(entries().map(a => a.getAttribute('href')))
            .toEqual(['/projects/p1/assets', '/projects/p2/assets', '/projects']);

        // One board folds to the canvas list — another project has no board with that id.
        await go('/projects/p1/canvas/b1');
        expect(entries().map(a => a.getAttribute('href')))
            .toEqual(['/projects/p1/canvas', '/projects/p2/canvas', '/projects']);
    });

    it('hangs the brand as a door to the hub, and the landing link beside the version', () => {
        const home = el().querySelector('app-rail-header a.home') as HTMLAnchorElement;
        expect(home.getAttribute('href')).toBe('/projects');
        expect(home.textContent).toContain('Cedar Clerk');
        // A plain href on purpose — the landing is served outside the SPA.
        const about = el().querySelector('app-rail-header a.about') as HTMLAnchorElement;
        expect(about.getAttribute('href')).toBe('/welcome');
    });

    it('puts Admin on the wall and nowhere in either account menu', () => {
        TestBed.inject(AuthService).isAdmin.set(true);
        fixture.detectChanges();
        expect(hooks().map(a => a.textContent?.trim())).toContain('Admin');
        expect(menuItems().map(menuText)).not.toContain('Admin');
        (el().querySelector('app-account-menu .account-trigger') as HTMLButtonElement).click();
        fixture.detectChanges();
        expect(el().querySelector('app-account-menu .account-popover')?.textContent).not.toContain('Admin');
    });

    it('names what is open inside the project, not the project twice', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/drafts');
        expect(crumbs()).toEqual(['Documents']);
        await go('/projects/p1/builds');
        expect(crumbs()).toEqual(['Builds']);
        await go('/projects/p1');
        expect(crumbs()).toEqual([]);
        await go('/dev/icons');
        expect(crumbs()).toEqual(['Icons']);
    });

    // ADR-151, moved to the wall by ADR-183 — the two controls with no bench surface of their own
    // live in the tray at the foot of the hook rail, beside the screens that got no hook.
    it('keeps the theme toggle and Appearance in the wall tray', () => {
        const labels = menuItems().map(menuText);
        expect(labels.some(l => l.includes('Toggle theme'))).toBe(true);
        expect(labels).toContain('Appearance');
        expect(el().querySelector('app-hook-rail .dots')).toBeTruthy();
        expect(el().querySelector('app-rail-header .dots')).toBeNull();
        // Navigation is the wall's; the menu carries only what the wall has no hook for.
        expect(labels).toContain('Glossary');
        expect(labels).not.toContain('Drafts');
    });

    it('flips the theme from the menu', () => {
        const theme = TestBed.inject(ThemeService);
        const before = theme.theme();
        menuItem('Toggle theme').click();
        expect(theme.theme()).not.toBe(before);
    });

    it('opens the Appearance panel the shell now parents', () => {
        expect(el().querySelector('app-appearance-panel app-modal')).toBeFalsy();
        menuItem('Appearance').click();
        fixture.detectChanges();
        expect(el().querySelector('app-appearance-panel app-modal')).toBeTruthy();
    });

    // Theme and Appearance act on the screen behind the panel, so the panel stays; the entries
    // that are destinations take it with them rather than leaving it hanging over the new page.
    it('leaves the panel standing for the two in-place entries and shuts it on a destination', async () => {
        const dots = () => el().querySelector('app-hook-rail .dots') as HTMLButtonElement;
        const panel = () => el().querySelector('app-hook-rail .tray-panel') as HTMLElement;

        dots().click();
        fixture.detectChanges();
        menuItem('Toggle theme').click();
        fixture.detectChanges();
        expect(panel().hasAttribute('hidden')).toBe(false);

        menuItem('Glossary').click();
        fixture.detectChanges();
        await fixture.whenStable();
        expect(panel().hasAttribute('hidden')).toBe(true);
        expect(dots().getAttribute('aria-expanded')).toBe('false');
    });

    // Carried over from the root component's own spec: the drawer stays shut, and the lip has to
    // say enough to justify leaving it that way.
    it('shuts the drawer by default, and says so on the lip', () => {
        const pull = el().querySelector('app-bench-drawer .pull');
        expect(pull?.getAttribute('aria-expanded')).toBe('false');
        expect(el().querySelector('app-bench-drawer .summary')?.textContent?.trim()).toBeTruthy();
    });
});
