import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { AppearanceService } from '../core/appearance.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';
import { ModalComponent } from '../shared/modal.component';
import { PopoverComponent } from '../shared/popover.component';
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

    it('takes the persistent sidebar mode from Appearance and forces a phone-safe rail', async () => {
        await go('/drafts');
        expect(fixture.componentInstance.mode()).toBe('full');
        expect(el().querySelector('app-sidebar')!.classList).not.toContain('is-rail');

        TestBed.inject(AppearanceService).preview({ sidebarMode: 'rail' });
        fixture.detectChanges();
        expect(fixture.componentInstance.mode()).toBe('rail');
        expect(el().querySelector('app-sidebar')!.classList).toContain('is-rail');
        expect(el().querySelector('app-sidebar .side-label')).toBeNull();

        TestBed.inject(AppearanceService).preview({ sidebarMode: 'full' });
        fixture.detectChanges();
        expect(fixture.componentInstance.mode()).toBe('full');
        expect(el().querySelector('app-sidebar .side-mode')).toBeNull();

        const originalWidth = window.innerWidth;
        Object.defineProperty(window, 'innerWidth', { configurable: true, value: 390 });
        window.dispatchEvent(new Event('resize'));
        fixture.detectChanges();
        expect(fixture.componentInstance.mode()).toBe('rail');
        expect(TestBed.inject(AppearanceService).prefs().sidebarMode).toBe('full');

        Object.defineProperty(window, 'innerWidth', { configurable: true, value: originalWidth });
        window.dispatchEvent(new Event('resize'));
        fixture.detectChanges();
        expect(fixture.componentInstance.mode()).toBe('full');
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
        expect(labels()).toEqual(['Documents', 'Assets', 'Calendar', 'Posts', 'Metrics', 'Glossary', 'Presets']);
        expect(groupLabels()).toEqual(['Write', 'Plan', 'Ship', 'Library']);
        expect(el().querySelector('app-project-switcher')).toBeNull();
    });

    it('draws the account-wide screens on the hub until a project has been opened, and the hub as the card', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        fixture.detectChanges();
        await go('/projects');
        expect(labels()).toEqual(['Documents', 'Assets', 'Calendar', 'Posts', 'Metrics', 'Glossary', 'Presets']);
        expect(groupLabels()).toEqual(['Write', 'Plan', 'Ship', 'Library']);
        const card = el().querySelector('app-project-switcher .side-project') as HTMLButtonElement;
        expect(card.tagName).toBe('BUTTON');
        expect(card.querySelector('.side-project-name')!.textContent!.trim()).toBe('All projects');
        expect(card.querySelector('.side-project-tile.is-hub app-icon')).toBeTruthy();
        card.click();
        fixture.detectChanges();
        expect([...el().querySelectorAll('.side-project-item')].map(menuText))
            .toEqual(['All projects', 'Manage teams']);
        expect(el().querySelector('.side-project-item.is-on')?.textContent).toContain('All projects');
    });

    it('keeps both workspace doors after the first project arrives', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        fixture.detectChanges();
        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }]);

        const entries = [...el().querySelectorAll('app-project-switcher .side-project-item')] as HTMLAnchorElement[];
        expect(entries.map(menuText)).toEqual(['Cedar Quest', 'All projects', 'Manage teams']);
        expect(entries.map(entry => entry.getAttribute('href'))).toEqual(['/projects/p1', '/projects', '/teams']);
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
        expect(lit()).toBeUndefined();
        await go('/projects');
        expect(lit()).toBeUndefined();
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
        expect(lit()).toBeUndefined();
        expect(el().querySelector('.side-project-item.is-on')?.textContent).toContain('Manage teams');
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
            'Glossary', 'Presets',
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
        expect(labels()).toEqual(['Canvas', 'Calendar', 'Posts', 'Metrics', 'Glossary', 'Presets']);
    });

    it('draws the counts the project list already holds, and never a zero', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest', createdFromPreset: 'blog', modules: {}, documentCount: 23, openTaskCount: 0, assetCount: 77 }]);
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

    it('keeps workspace doors in the switcher before and after projects load', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1');
        const card = el().querySelector('app-project-switcher .side-project') as HTMLButtonElement;
        expect(card.tagName).toBe('BUTTON');
        expect(card.querySelector('.side-project-name')!.textContent!.trim()).toBe('…');
        expect([...el().querySelectorAll('app-project-switcher .side-project-item')].map(menuText))
            .toEqual(['All projects', 'Manage teams']);

        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);
        expect(card.tagName).toBe('BUTTON');
        expect(card.getAttribute('aria-haspopup')).toBe('true');

        const entries = [...el().querySelectorAll('app-project-switcher .side-project-item')] as HTMLAnchorElement[];
        expect(entries.map(menuText)).toEqual(['Cedar Quest', 'Second', 'All projects', 'Manage teams']);
        expect(entries.map(a => a.getAttribute('href'))).toEqual(['/projects/p1', '/projects/p2', '/projects', '/teams']);
    });

    it('refreshes the switcher when a project is created after the shell list loaded', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        fixture.detectChanges();
        await flushProjects([]);

        await router.navigateByUrl('/projects/p-new');
        fixture.detectChanges();
        for (const probe of TestBed.inject(HttpTestingController)
                 .match(r => /^\/api\/projects\/[^/]+\/access$/.test(r.url))) {
            probe.flush({ role: 'owner', canWrite: true, archived: false });
        }
        TestBed.inject(HttpTestingController)
            .expectOne(r => r.url.startsWith('/api/projects') && !r.url.includes('/access'))
            .flush([{ id: 'p-new', name: 'New project', createdFromPreset: 'empty', modules: {} }]);
        await fixture.whenStable();
        fixture.detectChanges();

        expect(el().querySelector('app-project-switcher .side-project-name')!.textContent!.trim())
            .toBe('New project');
    });

    it('dismisses the open project switcher before the global search modal opens', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);
        const card = el().querySelector('app-project-switcher .side-project') as HTMLButtonElement;
        card.click();
        fixture.detectChanges();
        const entry = el().querySelector('app-project-switcher .side-project-item') as HTMLAnchorElement;
        entry.focus();
        expect(card.getAttribute('aria-expanded')).toBe('true');

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true }));
        fixture.detectChanges();
        await Promise.resolve();

        expect(card.getAttribute('aria-expanded')).toBe('false');
        expect(el().querySelector('.so-panel[aria-modal="true"]')).toBeTruthy();
    });

    it('carries the open project screen across the switcher', async () => {
        TestBed.inject(AuthService).indieDev.set(true);
        await go('/projects/p1/assets');
        await flushProjects([{ id: 'p1', name: 'Cedar Quest' }, { id: 'p2', name: 'Second' }]);
        const entries = () => [...el().querySelectorAll('app-project-switcher .side-project-item')] as HTMLAnchorElement[];
        expect(entries().map(a => a.getAttribute('href')))
            .toEqual(['/projects/p1/assets', '/projects/p2/assets', '/projects', '/teams']);

        await go('/projects/p1/canvas/b1');
        expect(entries().map(a => a.getAttribute('href')))
            .toEqual(['/projects/p1/canvas', '/projects/p2/canvas', '/projects', '/teams']);
    });

    it('keeps the brand as identity so the switcher is the only projects door', () => {
        const brand = el().querySelector('app-sidebar .side-brand') as HTMLElement;
        expect(brand.tagName).toBe('DIV');
        expect(brand.textContent).toContain('Cedar Clerk');
        expect(el().querySelector('app-sidebar a.side-brand')).toBeNull();
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

    it('keeps one Settings door in the compact account menu', () => {
        const entries = openAccountMenu();
        const menu = entries.map(menuText);
        expect(menu[0]).toBe('Settings');
        expect(menu.filter(item => item === 'Settings')).toHaveLength(1);
        expect(menu).not.toContain('Glossary');
        expect(menu).not.toContain('Presets');
        expect(labels()).toContain('Glossary');
        expect(menu).not.toContain('Appearance');
        expect(menu.some(m => m.includes('Toggle theme'))).toBe(false);
        expect(menu).toContain('Fullscreen');
        expect(menu.some(m => m.includes('Debug console'))).toBe(false);
        expect(menu.at(-1)).toBe('Log out');
        expect(menu).not.toContain('Documents');
        const about = entries.find(i => menuText(i).includes('About')) as HTMLAnchorElement;
        expect(about.getAttribute('href')).toBe('/welcome');
    });

    it('gives the account menu side placement in full and rail modes, with semantics and focus return', async () => {
        const trigger = el().querySelector('app-account-menu .account-trigger') as HTMLButtonElement;
        const popoverTrigger = el().querySelector('app-account-menu .popover-trigger') as HTMLElement;
        const sidebar = el().querySelector('app-sidebar') as HTMLElement;
        const accountDebug = fixture.debugElement.query(By.css('app-account-menu'));
        const popover = accountDebug.query(By.directive(PopoverComponent)).componentInstance as PopoverComponent;
        const rect = (x: number, y: number, width: number, height: number) => ({
            x, y, width, height, top: y, right: x + width, bottom: y + height, left: x,
            toJSON: () => ({}),
        } as DOMRect);
        vi.spyOn(sidebar, 'getBoundingClientRect').mockReturnValue(rect(0, 0, 232, 768));
        vi.spyOn(popoverTrigger, 'getBoundingClientRect').mockReturnValue(rect(12, 700, 150, 38));

        expect(popover.placement()).toBe('right-start');
        expect(trigger.getAttribute('aria-haspopup')).toBe('dialog');
        expect(trigger.getAttribute('aria-expanded')).toBe('false');
        expect(trigger.getAttribute('aria-controls')).toBe('account-menu-panel');

        trigger.click();
        fixture.detectChanges();
        const settings = el().querySelector('app-account-menu .account-item') as HTMLAnchorElement;
        expect(trigger.getAttribute('aria-expanded')).toBe('true');
        const panel = el().querySelector('#account-menu-panel') as HTMLElement;
        expect(panel.getAttribute('role')).toBe('dialog');
        expect(panel.querySelector('[role="menuitem"]')).toBeNull();
        expect(panel.closest<HTMLElement>('.popover-panel')?.style.left).toBe('240px');
        expect(panel.closest<HTMLElement>('.popover-panel')?.style.right).toBe('');
        panel.dispatchEvent(new Event('scroll'));
        fixture.detectChanges();
        expect(trigger.getAttribute('aria-expanded')).toBe('true');
        await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
        expect(document.activeElement).toBe(settings);
        settings.focus();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        fixture.detectChanges();
        expect(trigger.getAttribute('aria-expanded')).toBe('false');
        expect(document.activeElement).toBe(trigger);

        const outsideButton = document.createElement('button');
        document.body.append(outsideButton);
        trigger.click();
        fixture.detectChanges();
        outsideButton.focus();
        outsideButton.click();
        fixture.detectChanges();
        expect(trigger.getAttribute('aria-expanded')).toBe('false');
        expect(document.activeElement).toBe(outsideButton);
        outsideButton.remove();

        trigger.click();
        fixture.detectChanges();
        (el().querySelector('app-account-menu .account-item') as HTMLElement).focus();
        document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        fixture.detectChanges();
        expect(trigger.getAttribute('aria-expanded')).toBe('false');
        expect(document.activeElement).toBe(trigger);

        const inert = document.createElement('div');
        const inertButton = document.createElement('button');
        inert.setAttribute('inert', '');
        inert.append(inertButton);
        document.body.append(inert);
        trigger.click();
        fixture.detectChanges();
        (el().querySelector('app-account-menu .account-item') as HTMLElement).focus();
        inertButton.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        fixture.detectChanges();
        expect(trigger.getAttribute('aria-expanded')).toBe('false');
        expect(document.activeElement).toBe(trigger);
        inert.remove();

        TestBed.inject(AppearanceService).preview({ sidebarMode: 'rail' });
        fixture.detectChanges();
        vi.mocked(sidebar.getBoundingClientRect).mockReturnValue(rect(0, 0, 92, 768));
        vi.mocked(popoverTrigger.getBoundingClientRect).mockReturnValue(rect(10, 700, 44, 38));

        trigger.click();
        fixture.detectChanges();
        expect(el().querySelector('app-sidebar')!.classList).toContain('is-rail');
        expect(trigger.getAttribute('aria-expanded')).toBe('true');
        expect(trigger.getAttribute('aria-label')).toContain('Account');
        const railPanel = el().querySelector('#account-menu-panel') as HTMLElement;
        expect(railPanel.closest<HTMLElement>('.popover-panel')?.style.left).toBe('100px');
        popover.close();
    });

    it('offers Fullscreen as a transient account action and closes the popover before requesting it', async () => {
        const original = Object.getOwnPropertyDescriptor(document.documentElement, 'requestFullscreen');
        const requestFullscreen = vi.fn().mockResolvedValue(undefined);
        Object.defineProperty(document.documentElement, 'requestFullscreen', {
            configurable: true,
            value: requestFullscreen,
        });

        try {
            menuItem('Fullscreen').click();
            fixture.detectChanges();
            await Promise.resolve();

            expect(requestFullscreen).toHaveBeenCalledOnce();
            expect(el().querySelector('app-account-menu .popover-panel')).toBeNull();
        } finally {
            if (original) Object.defineProperty(document.documentElement, 'requestFullscreen', original);
            else Reflect.deleteProperty(document.documentElement, 'requestFullscreen');
        }
    });

    // ADR-239 clause 11 — the console is an overlay: shut by default, opened from the menu or the
    // shortcut, and reserving no height on the shell while shut.
    it('keeps the console shut, and toggles it from the menu and from Ctrl+`', () => {
        const overlays = TestBed.inject(OverlayCoordinatorService);
        TestBed.inject(AuthService).isAdmin.set(true);
        fixture.detectChanges();
        expect(overlays.active()).toBeNull();
        expect(el().querySelector('.console-overlay')).toBeNull();

        menuItem('Debug console').click();
        fixture.detectChanges();
        expect(overlays.active()).toBe('debug');
        expect(el().querySelector('.console-overlay[role="dialog"]')).toBeTruthy();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: '`', ctrlKey: true, bubbles: true }));
        fixture.detectChanges();
        expect(overlays.active()).toBeNull();
        expect(el().querySelector('.console-overlay')).toBeNull();
    });

    it('keeps exactly one shell overlay active across keyboard entry points', () => {
        const overlays = TestBed.inject(OverlayCoordinatorService);
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true }));
        fixture.detectChanges();
        expect(overlays.active()).toBe('search');
        expect(el().querySelector('.so-panel[aria-modal="true"]')).toBeTruthy();
        expect(el().querySelectorAll('[aria-modal="true"]').length).toBe(1);

        document.dispatchEvent(new KeyboardEvent('keydown', { key: '`', ctrlKey: true, bubbles: true }));
        fixture.detectChanges();
        expect(overlays.active()).toBe('debug');
        expect(el().querySelector('.so-panel')).toBeNull();
        expect(el().querySelectorAll('[aria-modal="true"]').length).toBe(1);
    });

    it('does not open a shell overlay while a page modal owns the top layer', async () => {
        const overlays = TestBed.inject(OverlayCoordinatorService);
        const pageModal = TestBed.createComponent(ModalComponent);
        pageModal.detectChanges();
        await Promise.resolve();
        expect(overlays.modalOpen()).toBe(true);

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true }));
        document.dispatchEvent(new KeyboardEvent('keydown', { key: '`', ctrlKey: true, bubbles: true }));
        fixture.detectChanges();

        expect(overlays.active()).toBeNull();
        expect(el().querySelector('.so-panel')).toBeNull();
        expect(el().querySelector('.console-overlay')).toBeNull();
        pageModal.destroy();
    });

    it('dismisses the account popover synchronously, keeps one modal, and restores the trigger after a peer chain', async () => {
        const entries = openAccountMenu();
        const trigger = el().querySelector('app-account-menu .account-trigger') as HTMLButtonElement;
        const focusedEntry = entries.find(entry => menuText(entry) === 'Settings') as HTMLAnchorElement;
        const accountDebug = fixture.debugElement.query(By.css('app-account-menu'));
        const popover = accountDebug.query(By.directive(PopoverComponent)).componentInstance as PopoverComponent;
        const close = vi.spyOn(popover, 'close');
        focusedEntry.focus();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true }));
        expect(popover.isOpen()).toBe(false);
        expect(close).toHaveBeenCalledOnce();
        fixture.detectChanges();
        await Promise.resolve();
        expect(document.activeElement).toBe(el().querySelector('.so-input'));
        expect(el().querySelectorAll('[aria-modal="true"]').length).toBe(1);

        document.dispatchEvent(new KeyboardEvent('keydown', { key: '`', code: 'Backquote', ctrlKey: true, bubbles: true }));
        fixture.detectChanges();
        await Promise.resolve();
        expect(el().querySelector('.so-panel')).toBeNull();
        expect(el().querySelectorAll('[aria-modal="true"]').length).toBe(1);

        (document.activeElement as HTMLElement).dispatchEvent(
            new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        fixture.detectChanges();
        await Promise.resolve();
        expect(close).toHaveBeenCalledOnce();
        expect(el().querySelectorAll('[aria-modal="true"]').length).toBe(0);
        expect(document.activeElement).toBe(trigger);
    });

    it('closes the account popover before opening Feedback', () => {
        menuItem('Send feedback').click();
        fixture.detectChanges();

        expect(el().querySelector('app-account-menu .popover-panel')).toBeNull();
        expect(el().querySelector('app-feedback-panel [aria-modal="true"]')).toBeTruthy();
        expect(el().querySelectorAll('[aria-modal="true"]').length).toBe(1);
    });
});
