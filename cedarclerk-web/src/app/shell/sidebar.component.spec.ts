import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { NavGroup, NavItem, SidebarComponent, SidebarProject } from './sidebar.component';

const GROUPS: NavGroup[] = [
    { id: 'write', label: 'Write', items: [
        { id: 'documents', label: 'Documents', icon: 'file-text', link: '/drafts', count: 23, countTitle: 'documents' },
        { id: 'assets', label: 'Assets', icon: 'images', link: '/library' },
    ] },
    { id: 'plan', label: 'Plan', items: [] },
    { id: 'ship', label: 'Ship', items: [
        { id: 'metrics', label: 'Metrics', icon: 'chart-bar', link: '/posts', queryParams: { tab: 'stats' }, count: 0 },
    ] },
];
const FOOT: NavItem[] = [{ id: 'settings', label: 'Settings', icon: 'gear', link: '/settings' }];

@Component({
    imports: [SidebarComponent],
    template: `
        <app-sidebar [mode]="mode()" [groups]="groups" [foot]="foot" activeId="documents"
                     [project]="project()" [projects]="projects()" projectHint="Switch project"
                     navLabel="Screens" brand="Cedar Clerk" brandLabel="Cedar Clerk — home"
                     allProjectsLabel="All projects" alertsTitle="New comments and reactions" [alerts]="alerts()"
                     collapseLabel="Collapse sidebar" expandLabel="Expand sidebar"
                     (modeChange)="mode.set($event)" />
    `,
})
class Host {
    mode = signal<'full' | 'rail'>('full');
    groups = GROUPS;
    foot = FOOT;
    project = signal<SidebarProject | null>({ id: 'p1', name: 'Cedar Quest', kind: 'Game · 2 projects', link: ['/projects', 'p1'] });
    projects = signal<SidebarProject[]>([]);
    alerts = signal(0);
}

describe('SidebarComponent', () => {
    function mount() {
        TestBed.configureTestingModule({
            providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
        });
        const fixture = TestBed.createComponent(Host);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        return {
            fixture,
            host: fixture.componentInstance,
            el,
            items: () => [...el.querySelectorAll('.side-item')] as HTMLAnchorElement[],
            texts: () => [...el.querySelectorAll('.side-item .side-text')].map(s => s.textContent?.trim()),
        };
    }

    it('is paper, one nav, and every item an anchor', () => {
        const h = mount();
        expect(h.el.querySelector('app-sidebar')!.getAttribute('data-surface')).toBe('paper');
        expect(h.el.querySelectorAll('nav').length).toBe(1);
        expect(h.el.querySelector('nav')!.getAttribute('aria-label')).toBe('Screens');
        expect(h.items().every(a => a.tagName === 'A')).toBe(true);
        expect(h.texts()).toEqual(['Documents', 'Assets', 'Metrics', 'Settings']);
    });

    it('skips a group with nothing in it and labels the rest', () => {
        const h = mount();
        expect([...h.el.querySelectorAll('.side-label')].map(l => l.textContent?.trim())).toEqual(['Write', 'Ship']);
    });

    it('marks the active item once, with aria-current', () => {
        const h = mount();
        const current = h.items().filter(a => a.getAttribute('aria-current') === 'page');
        expect(current.length).toBe(1);
        expect(current[0].querySelector('.side-text')!.textContent!.trim()).toBe('Documents');
        expect(current[0].classList).toContain('is-on');
    });

    it('draws a count only above zero, and names what it counts without dropping the number', () => {
        const h = mount();
        const counts = [...h.el.querySelectorAll('.side-count')] as HTMLElement[];
        expect(counts.length).toBe(1);
        expect(counts[0].textContent!.trim()).toBe('23');
        expect(counts[0].getAttribute('aria-label')).toBe('23 documents');
    });

    it('carries a query on the item that needs one', () => {
        const h = mount();
        expect(h.items().find(a => a.textContent?.includes('Metrics'))!.getAttribute('href')).toBe('/posts?tab=stats');
    });

    it('draws the card as a link to the hub until there is something to switch to', () => {
        const h = mount();
        const card = h.el.querySelector('app-project-switcher .side-project') as HTMLAnchorElement;
        expect(card.tagName).toBe('A');
        expect(card.getAttribute('href')).toBe('/projects');
        expect(card.querySelector('.side-project-name')!.textContent!.trim()).toBe('Cedar Quest');
        expect(card.querySelector('.side-project-kind')!.textContent!.trim()).toBe('Game · 2 projects');
        expect(card.querySelector('.side-project-tile')!.textContent!.trim()).toBe('CQ');
    });

    it('opens the switcher panel, closes it on Escape and hands focus back', () => {
        const h = mount();
        h.host.projects.set([
            { id: 'p1', name: 'Cedar Quest', kind: '', link: ['/projects', 'p1'] },
            { id: '', name: 'All projects', kind: '', link: '/projects' },
        ]);
        h.fixture.detectChanges();
        const button = h.el.querySelector('app-project-switcher .side-project') as HTMLButtonElement;
        const panel = () => h.el.querySelector('.side-project-panel') as HTMLElement;
        expect(button.getAttribute('aria-expanded')).toBe('false');
        expect(panel().hasAttribute('hidden')).toBe(true);

        button.focus();
        button.click();
        h.fixture.detectChanges();
        expect(button.getAttribute('aria-expanded')).toBe('true');
        expect(panel().hasAttribute('hidden')).toBe(false);
        expect(panel().querySelector('.side-project-item.is-on')!.textContent!.trim()).toBe('Cedar Quest');

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        h.fixture.detectChanges();
        expect(panel().hasAttribute('hidden')).toBe(true);
        expect(document.activeElement).toBe(button);
    });

    it('shows the bell dot only while something is unread', () => {
        const h = mount();
        expect(h.el.querySelector('.side-dot')).toBeNull();
        expect((h.el.querySelector('.side-bell') as HTMLAnchorElement).getAttribute('aria-label')).toBe('New comments and reactions');
        h.host.alerts.set(3);
        h.fixture.detectChanges();
        expect(h.el.querySelector('.side-dot')).toBeTruthy();
    });

    it('draws the rail with compact project and footer context', () => {
        const h = mount();
        h.host.mode.set('rail');
        h.fixture.detectChanges();
        expect(h.el.querySelector('app-sidebar')!.classList).toContain('is-rail');
        expect(h.el.querySelector('app-project-switcher.is-compact')).toBeTruthy();
        expect((h.el.querySelector('app-project-switcher .side-project') as HTMLElement).getAttribute('aria-label')).toBe('Cedar Quest');
        expect(h.el.querySelector('.side-label')).toBeNull();
        expect(h.el.querySelector('.side-count')).toBeNull();
        expect(h.el.querySelector('.side-wordmark')).toBeNull();
        expect(h.texts()).toEqual(['Documents', 'Assets', 'Metrics', 'Settings']);
        expect(h.el.querySelector('app-account-menu .account-trigger')).toBeTruthy();
        expect(h.el.querySelector('.side-bell')).toBeTruthy();
        const toggle = h.el.querySelector('.side-mode') as HTMLButtonElement;
        expect(toggle.getAttribute('aria-label')).toBe('Expand sidebar');
        toggle.click();
        h.fixture.detectChanges();
        expect(h.host.mode()).toBe('full');
        expect(toggle.getAttribute('aria-label')).toBe('Collapse sidebar');
    });
});
