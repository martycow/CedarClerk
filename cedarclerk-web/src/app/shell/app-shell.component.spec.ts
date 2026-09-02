import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { CommentsService } from '../core/comments.service';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/theme.service';
import { AppShellComponent } from './app-shell.component';

@Component({ template: '' })
class Blank {}

describe('app shell', () => {
    let fixture: ComponentFixture<AppShellComponent>;
    let router: Router;
    const el = () => fixture.nativeElement as HTMLElement;
    const items = () => [...el().querySelectorAll('app-sidebar .side-item')] as HTMLAnchorElement[];
    const labels = () => items().map(a => a.querySelector('.side-text')?.textContent?.trim());
    const groupLabels = () => [...el().querySelectorAll('app-sidebar .side-label')].map(g => g.textContent?.trim());
    const lit = () => items().find(a => a.getAttribute('aria-current') === 'page')?.querySelector('.side-text')?.textContent?.trim();
    const menuText = (i: Element) => (i.textContent ?? '').replace(/\s+/g, ' ').trim();
    const openAccountMenu = () => {
        (el().querySelector('app-account-menu .account-trigger') as HTMLButtonElement).click();
        fixture.detectChanges();
        return [...el().querySelectorAll('app-account-menu .account-item')] as HTMLElement[];
    };
    const menuItem = (label: string) => openAccountMenu().find(i => menuText(i).includes(label))!;

    async function go(url: string) {
        await router.navigateByUrl(url);
        fixture.detectChanges();
        for (const probe of TestBed.inject(HttpTestingController)
                 .match(r => /^\/api\/projects\/[^/]+\/access$/.test(r.url))) {
            probe.flush({ role: 'owner', canWrite: true, archived: false });
        }
        await fixture.whenStable();
        fixture.detectChanges();
    }

    async function flushProjects(list: object[]) {
        TestBed.inject(HttpTestingController)
            .expectOne(r => r.url.startsWith('/api/projects') && !r.url.includes('/access'))
            .flush(list);
        await fixture.whenStable();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [AppShellComponent],
            providers: [
                provideRouter([{ path: '**', component: Blank }]),
                provideHttpClient(),
                provideHttpClientTesting(),
            ],
        }).compileComponents();
        TestBed.inject(LocaleService).uiLang.set('en');
        router = TestBed.inject(Router);
        localStorage.removeItem('cedar-project');
        localStorage.removeItem('cedar-sidebar-mode');
        fixture = TestBed.createComponent(AppShellComponent);
        fixture.detectChanges();
    });

    it('draws the sidebar beside the ground and nothing above or below them', () => {
        expect(el().querySelector('.shell > app-sidebar')).toBeTruthy();
        expect(el().querySelector('.shell > main.body router-outlet')).toBeTruthy();
        expect(el().querySelectorAll('.shell > *').length).toBe(2);
        expect(el().querySelector('app-debug-console')).toBeTruthy();
    });

    it('stands every screen on paper', () => {
        const main = el().querySelector('main.body') as HTMLElement;
        expect(main.getAttribute('data-surface')).toBe('paper');
    });

    it('keeps one sidebar mode across routes and persists an explicit change', async () => {
        await go('/drafts');
        expect(fixture.componentInstance.mode()).toBe('full');
        expect(el().querySelector('app-sidebar')!.classList).not.toContain('is-rail');
        await go('/editor?draft=1');
        expect(fixture.componentInstance.mode()).toBe('full');
        const toggle = el().querySelector('app-sidebar .side-mode') as HTMLButtonElement;
        toggle.click();
        fixture.detectChanges();
        expect(fixture.componentInstance.mode()).toBe('rail');
        expect(el().querySelector('app-sidebar')!.classList).toContain('is-rail');
        expect(el().querySelector('app-sidebar .side-label')).toBeNull();
        expect(localStorage.getItem('cedar-sidebar-mode')).toBe('rail');
        await go('/drafts');
        expect(fixture.componentInstance.mode()).toBe('rail');
    });

    it('counts unread feedback on Metrics, and draws nothing at zero', () => {
        const feedback = TestBed.inject(CommentsService);
        expect(el().querySelectorAll('app-sidebar .side-count').length).toBe(0);

        feedback.newComments.set(2);
        feedback.newReactions.set(1);
        fixture.detectChanges();
        const count = el().querySelector('app-sidebar .side-count')!;
        expect(count.textContent!.trim()).toBe('3');
        expect(count.closest('a.side-item')!.querySelector('.side-text')!.textContent!.trim()).toBe('Metrics');

        feedback.newComments.set(0);
        feedback.newReactions.set(0);
        fixture.detectChanges();
        expect(el().querySelectorAll('app-sidebar .side-count').length).toBe(0);
    });

    it('asks for the count itself, so the tally is right on whatever screen opened', () => {
        expect(TestBed.inject(HttpTestingController).match('/api/comments/new-count').length).toBe(1);
    });

    it('lists the account-wide screens while the module is off, in their groups', () => {
        expect(labels()).toEqual(['Documents', 'Assets', 'Calendar', 'Posts', 'Metrics', 'Glossary', 'Presets', 'Settings']);
        expect(groupLabels()).toEqual(['Write', 'Plan', 'Ship', 'Library']);
        expect(el().querySelector('app-project-switcher')).toBeNull();
    });

    it('draws the account-wide screens on the hub until a project has been opened, and the hub as the card', () => {
        TestBed.inject(AuthService).indieDev.set(true);
        fixture.detectChanges();
        expect(labels()).toEqual(['Documents', 'Assets', 'Calendar', 'Posts', 'Metrics', 'Glossary', 'Presets', 'Teams', 'All projects', 'Settings']);
        expect(groupLabels()).toEqual(['Write', 'Plan', 'Ship', 'Library']);
        const card = el().querySelector('app-project-switcher .side-project') as HTMLAnchorElement;
        expect(card.tagName).toBe('A');
        expect(card.getAttribute('href')).toBe('/projects');
        expect(card.querySelector('.side-project-name')!.textContent!.trim()).toBe('All projects');
        expect(card.querySelector('.side-project-tile.is-hub app-icon')).toBeTruthy();
    });

    it('lights every screen and the account-wide routes it belongs to', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1');
        await go('/drafts');
        expect(lit()).toBe('Documents');
        await go('/editor');
        expect(lit()).toBe('Documents');
        await go('/library');
        expect(lit()).toBe('Assets');
        await go('/calendar');
        expect(lit()).toBe('Calendar');
        await go('/posts');
        expect(lit()).toBe('Posts');
        await go('/posts?tab=stats');
        expect(lit()).toBe('Metrics');
        await go('/settings');
        expect(lit()).toBe('Settings');
        await go('/projects');
        expect(lit()).toBe('All projects');
        await go('/projects/p1/tasks');
        expect(lit()).toBe('Tasks');
        await go('/projects/p1/planner');
        expect(lit()).toBe('Planner');
        await go('/projects/p1/builds');
        expect(lit()).toBe('Builds');
        await go('/projects/p1/assets');
        expect(lit()).toBe('Assets');
        await go('/projects/p1/canvas');
        expect(lit()).toBe('Canvas');
        await go('/projects/p1/canvas/b1');
        expect(lit()).toBe('Canvas');
        await go('/glossary');
        expect(lit()).toBe('Glossary');
        await go('/presets');
        expect(lit()).toBe('Presets');
        await go('/teams');
        expect(lit()).toBe('Teams');
    });

    it('adds the complete project set after a project has been opened', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/drafts');
        expect(labels()).not.toContain('Tasks');

        await go('/projects/p1/planner');
        expect(labels()).toEqual([
            'Documents', 'Assets', 'Canvas', 'Dialogues', 'Site',
            'Tasks', 'Planner', 'Calendar',
            'Builds', 'Posts', 'Metrics',
            'Glossary', 'Presets', 'Teams',
            'All projects', 'Settings',
        ]);
        expect(items().find(a => a.textContent?.includes('Tasks'))!.getAttribute('href')).toBe('/projects/p1/tasks');
        expect(items().find(a => a.textContent?.includes('Metrics'))!.getAttribute('href')).toBe('/posts?tab=stats');
    });

    it('draws a member the canvas and the account-wide screens only', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await router.navigateByUrl('/projects/p1/canvas');
        fixture.detectChanges();
        TestBed.inject(HttpTestingController)
            .expectOne(r => /^\/api\/projects\/p1\/access$/.test(r.url))
            .flush({ role: 'editor', canWrite: true, archived: false });
        await fixture.whenStable();
        fixture.detectChanges();
        expect(labels()).toEqual(['Canvas', 'Calendar', 'Posts', 'Metrics', 'Glossary', 'Presets', 'Teams', 'All projects', 'Settings']);
    });

    it('draws the counts the project list already holds, and never a zero', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest', projectType: 'blog', documentCount: 23, openTaskCount: 0, assetCount: 77 }]);
        const count = (label: string) => items().find(a => a.textContent?.includes(label))!.querySelector('.side-count')?.textContent?.trim();
        expect(count('Documents')).toBe('23');
        expect(count('Assets')).toBe('77');
        expect(count('Tasks')).toBeUndefined();
        expect(el().querySelector('app-project-switcher .side-project-kind')!.textContent!.trim()).toBe('Blog · 1 project');
    });

    // ADR-186 — which project is open is session state, so it survives leaving the project's own
    // routes, and the card keeps naming it.
    it('keeps the project screens and the card after leaving project routes', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1/planner');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);

        await go('/drafts');
        expect(items().find(a => a.textContent?.includes('Tasks'))!.getAttribute('href')).toBe('/projects/p1/tasks');
        expect(el().querySelector('app-project-switcher .side-project-name')!.textContent!.trim()).toBe('Cedar Quest');
    });

    it('turns the card into a switcher once there are projects to switch to, hub last', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1');
        // No name yet and nothing to switch to: a link to the hub that borrows no other project's word.
        const link = el().querySelector('app-project-switcher .side-project') as HTMLAnchorElement;
        expect(link.tagName).toBe('A');
        expect(link.querySelector('.side-project-name')!.textContent!.trim()).toBe('…');

        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);
        const card = el().querySelector('app-project-switcher .side-project') as HTMLButtonElement;
        expect(card.tagName).toBe('BUTTON');
        expect(card.getAttribute('aria-haspopup')).toBe('true');

        const entries = [...el().querySelectorAll('app-project-switcher .side-project-item')] as HTMLAnchorElement[];
        expect(entries.map(a => a.textContent?.trim())).toEqual(['Cedar Quest', 'Second', 'All projects']);
        expect(entries.map(a => a.getAttribute('href'))).toEqual(['/projects/p1', '/projects/p2', '/projects']);
    });

    it('carries the open project screen across the switcher', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1/assets');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);
        const entries = () => [...el().querySelectorAll('app-project-switcher .side-project-item')] as HTMLAnchorElement[];
        expect(entries().map(a => a.getAttribute('href')))
            .toEqual(['/projects/p1/assets', '/projects/p2/assets', '/projects']);

        await go('/projects/p1/canvas/b1');
        expect(entries().map(a => a.getAttribute('href')))
            .toEqual(['/projects/p1/canvas', '/projects/p2/canvas', '/projects']);
    });

    it('hangs the brand as a door to the hub', () => {
        const home = el().querySelector('app-sidebar a.side-brand') as HTMLAnchorElement;
        expect(home.getAttribute('href')).toBe('/projects');
        expect(home.textContent).toContain('Cedar Clerk');
    });

    it('puts Admin in the account menu and nowhere in the navigation', () => {
        TestBed.inject(AuthService).isAdmin.set(true);
        fixture.detectChanges();
        expect(labels()).not.toContain('Admin');
        const menu = openAccountMenu().map(menuText);
        expect(menu).toContain('Admin');
        expect(menu).toContain('Style guide');
        expect(menu).toContain('Icons');
    });

    // ADR-240 — the menu holds what belongs to the person; the library screens are the sidebar's.
    it('holds the personal entries in the account menu, in order, and no screen', () => {
        const entries = openAccountMenu();
        const menu = entries.map(menuText);
        expect(menu.slice(0, 2)).toEqual(['Profile', 'Account']);
        expect(menu).not.toContain('Glossary');
        expect(menu).not.toContain('Presets');
        expect(labels()).toContain('Glossary');
        expect(menu).toContain('Appearance');
        expect(menu.some(m => m.includes('Toggle theme'))).toBe(true);
        expect(menu.some(m => m.includes('Fullscreen'))).toBe(true);
        expect(menu.some(m => m.includes('Debug console'))).toBe(true);
        expect(menu.at(-1)).toBe('Log out');
        expect(menu).not.toContain('Documents');
        const about = entries.find(i => menuText(i).includes('About')) as HTMLAnchorElement;
        expect(about.getAttribute('href')).toBe('/welcome');
    });

    it('flips the theme from the menu', () => {
        const theme = TestBed.inject(ThemeService);
        const before = theme.theme();
        menuItem('Toggle theme').click();
        expect(theme.theme()).not.toBe(before);
    });

    it('opens the Appearance panel the shell parents', () => {
        expect(el().querySelector('app-appearance-panel app-modal')).toBeFalsy();
        menuItem('Appearance').click();
        fixture.detectChanges();
        expect(el().querySelector('app-appearance-panel app-modal')).toBeTruthy();
    });

    // ADR-239 clause 11 — the console is an overlay: shut by default, opened from the menu or the
    // shortcut, and reserving no height on the shell while shut.
    it('keeps the console shut, and toggles it from the menu and from Ctrl+`', () => {
        const log = TestBed.inject(DebugLogService);
        expect(log.open()).toBe(false);
        expect(el().querySelector('.console-overlay')).toBeNull();

        menuItem('Debug console').click();
        fixture.detectChanges();
        expect(log.open()).toBe(true);
        expect(el().querySelector('.console-overlay[role="dialog"]')).toBeTruthy();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: '`', ctrlKey: true, bubbles: true }));
        fixture.detectChanges();
        expect(log.open()).toBe(false);
        expect(el().querySelector('.console-overlay')).toBeNull();
    });
});
