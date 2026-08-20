import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
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
    const menuItems = () => [...el().querySelectorAll('app-rail-header .menu-item')] as HTMLElement[];
    // The theme entry carries its sun/moon glyph in the same element as its label.
    const menuText = (i: Element) => (i.textContent ?? '').replace(/\s+/g, ' ').trim();
    const menuItem = (label: string) => menuItems().find(i => menuText(i).includes(label))!;

    async function go(url: string) {
        await router.navigateByUrl(url);
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

    it('hangs no project tool on the wall while the module is off', () => {
        const ids = hooks().map(a => a.textContent?.trim());
        expect(ids).toEqual(['Text', 'Assets', 'Metrics', 'Settings']);
        expect(el().querySelector('app-rail-header .tile')).toBeFalsy();
    });

    it('keeps the wall inside its budget with the module on', () => {
        TestBed.inject(AuthService).indieDev.set(true);
        fixture.detectChanges();
        const labels = hooks().map(a => a.textContent?.trim());
        expect(labels).toEqual(['Hub', 'Text', 'Board', 'Assets', 'Metrics', 'Settings']);
        expect(labels.length).toBeLessThanOrEqual(7);
        // Settings is the tail, and it is anchored inside the one list rather than a second one.
        expect(hooks().at(-1)!.closest('li')!.classList).toContain('is-tail');
    });

    it('lights the hook the route belongs to, including the two paths Text covers', async () => {
        const lit = () => hooks().find(a => a.getAttribute('aria-current') === 'page')?.textContent?.trim();
        TestBed.inject(AuthService).indieDev.set(true);

        await go('/drafts');
        expect(lit()).toBe('Text');
        await go('/editor');
        expect(lit()).toBe('Text');
        await go('/library');
        expect(lit()).toBe('Assets');
        await go('/posts');
        expect(lit()).toBe('Metrics');
        await go('/settings');
        expect(lit()).toBe('Settings');
        // The board is a child of the hub, so the longer pattern has to be tried first.
        await go('/projects');
        expect(lit()).toBe('Hub');
        await go('/projects/p1/tasks');
        expect(lit()).toBe('Board');
        // Everything behind the dots menu leaves the wall unlit rather than guessing a hook.
        await go('/glossary');
        expect(lit()).toBeUndefined();
    });

    // DraftMeta carries no projectId (ADR-139), so outside /projects/:id the board has no owner to
    // open. The hook lands on the hub, which is the one screen that can name which board was meant.
    it('sends the board to the hub when no route names a project', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/drafts');
        const board = hooks().find(a => a.textContent?.trim() === 'Board')!;
        expect(board.getAttribute('href')).toBe('/projects');

        await go('/projects/p1/planner');
        const onProject = hooks().find(a => a.textContent?.trim() === 'Board')!;
        expect(onProject.getAttribute('href')).toBe('/projects/p1/tasks');
    });

    // Same gap, seen from the switcher: a tile naming a project on /drafts would be a confident
    // wrong answer, so it says what it can actually vouch for — the account.
    it('scopes the switcher to the account where the project is unknown', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/drafts');
        expect(el().querySelector('app-rail-header .tile-name')?.textContent?.trim()).toBe('All projects');
    });

    it('names what is open inside the project, not the project twice', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/drafts');
        expect(crumbs()).toEqual(['Drafts']);
        await go('/projects/p1/builds');
        expect(crumbs()).toEqual(['Builds']);
        await go('/projects/p1');
        expect(crumbs()).toEqual([]);
        await go('/dev/icons');
        expect(crumbs()).toEqual(['Icons']);
    });

    // ADR-151 — the two controls with no bench surface of their own live behind the dots button.
    it('keeps the theme toggle and Appearance behind the dots button', () => {
        const labels = menuItems().map(menuText);
        expect(labels.some(l => l.includes('Toggle theme'))).toBe(true);
        expect(labels).toContain('Appearance');
        expect(el().querySelector('app-rail-header .dots')).toBeTruthy();
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

    // Carried over from the root component's own spec: the drawer stays shut, and the lip has to
    // say enough to justify leaving it that way.
    it('shuts the drawer by default, and says so on the lip', () => {
        const pull = el().querySelector('app-bench-drawer .pull');
        expect(pull?.getAttribute('aria-expanded')).toBe('false');
        expect(el().querySelector('app-bench-drawer .summary')?.textContent?.trim()).toBeTruthy();
    });
});
