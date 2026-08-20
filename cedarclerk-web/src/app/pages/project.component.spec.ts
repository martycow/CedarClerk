import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectComponent } from './project.component';
import { ProjectDetail, ProjectSummary, ProjectsService } from '../core/projects.service';
import { Build, BuildsService } from '../core/builds.service';
import { RulerService } from '../core/ruler.service';
import { en } from '../core/i18n/en';

const SUMMARY: ProjectSummary = {
    id: 'p1', name: 'Cedar Quest', description: '', projectType: 'fullgame', coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null,
    documentCount: 3, openTaskCount: 8, assetCount: 2481, lastActivityAt: '2026-08-19T11:00:00',
};

const OTHER: ProjectSummary = { ...SUMMARY, id: 'p2', name: 'Night Lanterns', assetCount: 0 };

const DETAIL: ProjectDetail = {
    id: 'p1', name: 'Cedar Quest', description: 'A game about a bench.', projectType: 'fullgame',
    coverUrl: null, createdAt: '2026-08-01T09:00:00', archivedAt: null,
    showcaseSlug: null, showcaseLinks: '',
    documents: [
        { id: 'd-old', title: 'Design bible', documentType: 'design', updatedAt: '2026-08-10T09:00:00', isArchived: false, isBlogPublished: false },
        { id: 'd-new', title: 'Devlog #12', documentType: 'post', updatedAt: '2026-08-19T11:00:00', isArchived: false, isBlogPublished: false },
        { id: 'd-mid', title: 'Cave script', documentType: 'script', updatedAt: '2026-08-15T09:00:00', isArchived: false, isBlogPublished: true },
    ],
    upNext: [
        {
            id: 't1', projectId: 'p1', title: 'Fix saves on quit', status: 'in_progress', priority: 1,
            description: '', assignee: '', sprintId: 's1', buildId: null, dueAt: '2026-01-01T00:00:00',
            isPublicRoadmap: false, createdAt: '', updatedAt: '', completedAt: null, archivedAt: null, links: [],
        },
    ],
    taskCounts: { in_progress: 2, backlog: 6 },
    currentSprint: {
        id: 's1', projectId: 'p1', number: 4, name: 'Autumn build',
        startsAt: '2026-08-01T00:00:00', endsAt: '2026-08-31T00:00:00',
        state: 'current', taskCount: 19, doneCount: 11, overdueCount: 1,
    },
    openTaskCount: 8,
};

const BUILDS: Build[] = [
    { id: 'b2', projectId: 'p1', version: '0.4.0', notes: '', releasedAt: null, createdAt: '', released: false, taskCount: 0, doneCount: 0, documents: [] },
    { id: 'b1', projectId: 'p1', version: '0.3.1', notes: '', releasedAt: '2026-08-17T09:00:00', createdAt: '', released: true, taskCount: 3, doneCount: 3, documents: [] },
];

class FakeProjects {
    detail: ProjectDetail | null = DETAIL;
    list_: ProjectSummary[] | null = [SUMMARY, OTHER];
    async get() { if (!this.detail) throw new Error('nope'); return structuredClone(this.detail); }
    async list() { if (!this.list_) throw new Error('nope'); return structuredClone(this.list_); }
}

class FakeBuilds {
    builds: Build[] | null = BUILDS;
    async list() { if (!this.builds) throw new Error('nope'); return structuredClone(this.builds); }
}

describe('project hub', () => {
    let fixture: ComponentFixture<ProjectComponent>;
    let projects: FakeProjects;
    let builds: FakeBuilds;
    const t = en.projects;

    const el = () => fixture.nativeElement as HTMLElement;
    const panels = () => [...el().querySelectorAll('app-shelf-panel')];
    const panel = (title: string) =>
        panels().find(p => p.getAttribute('aria-label') === title) as HTMLElement;
    const tiles = () => [...el().querySelectorAll('app-module-tile')] as HTMLElement[];
    const tileNames = () => tiles().map(x => x.querySelector('.mt-name')?.textContent?.trim());
    const tileCount = (name: string) =>
        tiles().find(x => x.querySelector('.mt-name')?.textContent?.trim() === name)
            ?.querySelector('.mt-count')?.textContent?.trim();

    async function create() {
        projects = new FakeProjects();
        builds = new FakeBuilds();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: ProjectsService, useValue: projects },
                { provide: BuildsService, useValue: builds },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1' })) } },
            ],
        });
        fixture = TestBed.createComponent(ProjectComponent);
        fixture.detectChanges();
        // load() and loadProjects() are two awaits deep before the build list lands.
        for (let i = 0; i < 5; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    // ADR-160 rule 1: a plate is a door. Documents is the panel on this screen and Metrics has no
    // per-project number, so the kit's six become four — and this is what would go red if someone
    // restored one of them with a plausible-looking count.
    it('hangs one plate per module that has a screen behind it, and no others', () => {
        expect(tileNames()).toEqual([t.tasks.title, t.railPendingTitle, t.assets.title, t.builds.title]);
        expect(tileNames()).not.toContain(t.hub.documentsPanel);
        expect(tileNames()).not.toContain(en.shell.metrics);
    });

    // ADR-163 — a plate is a door, and a door has an address. router.navigate has no URL to hand
    // the browser, so middle click, copy link address and the hover preview all died with it.
    it('gives every plate a real href rather than a click handler', () => {
        const hrefs = tiles().map(x => x.querySelector('.mt-plate')?.getAttribute('href'));
        expect(hrefs).toEqual([
            '/projects/p1/tasks', '/projects/p1/planner', '/projects/p1/assets', '/projects/p1/builds',
        ]);
        for (const tile of tiles()) expect(tile.querySelector('button')).toBeNull();
    });

    it('gives the up next tag the same address the board would open at', () => {
        const tag = el().querySelector('app-task-tag .tt-plate')!;
        expect(tag.tagName).toBe('A');
        expect(tag.getAttribute('href')).toBe('/projects/p1/tasks?task=t1');
    });

    it('takes each plate\'s number from the field that actually holds it', () => {
        expect(tileCount(t.tasks.title)).toBe('8');          // detail.openTaskCount
        expect(tileCount(t.railPendingTitle)).toBe('S4');    // the sprint covering today
        expect(tileCount(t.assets.title)).toBe('2481');      // summary.assetCount — not on the detail
        expect(tileCount(t.builds.title)).toBe('2');         // the build list, fetched beside it
    });

    // ADR-160 rule 4. 0 would say "no versions yet", which is a different sentence.
    it('shows a dash, never a zero, when the build list cannot be asked', async () => {
        TestBed.resetTestingModule();
        await create();
        builds.builds = null;
        fixture.componentInstance.builds.set(null);
        fixture.detectChanges();
        expect(tileCount(t.builds.title)).toBe('—');
    });

    it('shows a dash for assets when the project list did not answer', async () => {
        fixture.componentInstance.projects.set([]);
        fixture.detectChanges();
        expect(tileCount(t.assets.title)).toBe('—');
        // …and the hero's chalk strip goes quiet rather than inventing a date.
        expect(el().querySelector('app-worktop .wt-meta')).toBeNull();
    });

    it('stamps the version off the released build and never off a planned one', () => {
        const stamps = [...el().querySelectorAll('.hero-stamps app-stamp-badge')].map(s => s.textContent?.trim());
        expect(stamps).toContain('0.3.1');
        expect(stamps).not.toContain('0.4.0');
        expect(stamps).toContain(t.stateActive);
    });

    // The three regions, by the accessible name each panel carries.
    it('lays the bench out as projects, the top, and today', () => {
        expect(panels().length).toBe(3);
        expect(panel(t.title)).toBeTruthy();
        expect(panel(t.hub.documentsPanel)).toBeTruthy();
        expect(panel(t.hub.today)).toBeTruthy();
        expect(el().querySelectorAll('app-worktop').length).toBe(1);
    });

    it('marks the open project in the switcher and links every row to its own hub', () => {
        const rows = [...panel(t.title).querySelectorAll('a.proj')] as HTMLAnchorElement[];
        expect(rows.length).toBe(2);
        expect(rows[0].getAttribute('aria-current')).toBe('page');
        expect(rows[1].getAttribute('aria-current')).toBeNull();
        expect(rows.map(r => r.getAttribute('href'))).toEqual(['/projects/p1', '/projects/p2']);
    });

    // The old body showed three documents per featured type and the latest of the rest; the panel
    // shows all of them, newest first.
    it('lists every document, newest first', () => {
        const titles = [...panel(t.hub.documentsPanel).querySelectorAll('.doc-title')]
            .map(x => x.textContent?.trim());
        expect(titles).toEqual(['Devlog #12', 'Cave script', 'Design bible']);
    });

    it('opens the newest document from Continue', () => {
        const resume = el().querySelector('.resume-t')?.textContent?.trim();
        expect(resume).toBe('Devlog #12');

        const router = TestBed.inject(Router);
        const nav = vi.spyOn(router, 'navigate').mockResolvedValue(true);
        (el().querySelector('.resume-row app-button button') as HTMLButtonElement).click();
        expect(nav).toHaveBeenCalledWith(['/editor'], { queryParams: { draft: 'd-new' } });
    });

    it('hands a task tag its own urgency rather than reddening the row', () => {
        const tag = el().querySelector('app-task-tag')!;
        expect(tag.querySelector('.tt-prio')?.textContent?.trim()).toBe('P1');
        expect(tag.querySelector('.tt-due')?.classList.contains('tt-overdue')).toBe(true);
        expect(tag.classList.contains('overdue')).toBe(false);
    });

    // ADR-160 rule 6 — the kit's channel group has no link table behind it; the showcase does.
    it('says the public page is not published rather than drawing channels it cannot know', () => {
        const today = panel(t.hub.today);
        expect(today.textContent).toContain(t.hub.notPublished);
        expect(today.querySelector('.spec-value.link')).toBeNull();
    });

    it('publishes the rule while it is open and clears it on the way out', () => {
        const ruler = TestBed.inject(RulerService);
        expect(ruler.label()).toBe('Cedar Quest');
        expect(ruler.left().map(r => r.text)).toEqual([
            t.hub.rulerSprint(4, 11, 19),
            t.hub.rulerAssets(2481),
        ]);
        expect(ruler.right().map(r => r.text)).toEqual([t.hub.rulerDocs(3), t.hub.rulerTasks(8)]);

        fixture.destroy();
        expect(ruler.label()).toBe('');
        expect(ruler.left()).toEqual([]);
    });

    it('says no sprint covers today instead of drawing an empty one', async () => {
        fixture.componentInstance.project.set({ ...DETAIL, currentSprint: null });
        fixture.detectChanges();
        expect(tileCount(t.railPendingTitle)).toBe('—');
        expect(panel(t.hub.today).textContent).toContain(t.planner.noCurrentSprint);
        expect(panel(t.hub.today).querySelector('.bar')).toBeNull();
    });

    it('keeps the project type on this screen — it is the only one that shows it', () => {
        expect(el().querySelector('.hero-sub')?.textContent).toContain(t.projectTypes.fullgame.name);
    });
});
