import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectComponent } from './project.component';
import { ProjectDetail, ProjectSummary, ProjectsService } from '../core/projects.service';
import { Build, BuildsService } from '../core/builds.service';
import { Channel, ChannelsService } from '../core/channels.service';
import { en } from '../core/i18n/en';
import { formatInZone } from '../core/display-time';
import { AssetsService } from '../core/assets.service';

const SUMMARY: ProjectSummary = {
    id: 'p1', name: 'Cedar Quest', description: '', projectType: 'fullgame', coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null,
    documentCount: 3, openTaskCount: 8, assetCount: 2481, lastActivityAt: '2026-08-19T11:00:00',
};

const OTHER: ProjectSummary = { ...SUMMARY, id: 'p2', name: 'Night Lanterns', assetCount: 0 };

const DETAIL: ProjectDetail = {
    id: 'p1', name: 'Cedar Quest', description: 'A game about a bench.', projectType: 'fullgame',
    coverUrl: null, teamId: null, createdAt: '2026-08-01T09:00:00', archivedAt: null,
    showcaseSlug: null, showcaseLinks: '', showcaseGallery: '', showcaseTrailerUrl: null, customDomain: null,
    pressContactEmail: null, pressPrice: null, pressEngine: null, pressGenre: null, pressFactsheetRows: null,
    documents: [
        { id: 'd-old', title: 'Design bible', documentType: 'design', updatedAt: '2026-08-10T09:00:00', isArchived: false, isBlogPublished: false },
        { id: 'd-new', title: 'Devlog #12', documentType: 'post', updatedAt: '2026-08-19T11:00:00', isArchived: false, isBlogPublished: false },
        { id: 'd-mid', title: 'Cave script', documentType: 'script', updatedAt: '2026-08-15T09:00:00', isArchived: false, isBlogPublished: true },
        { id: 'd-gone', title: 'Old plan', documentType: 'note', updatedAt: '2026-08-01T09:00:00', isArchived: true, isBlogPublished: false },
    ],
    upNext: [
        {
            id: 't1', projectId: 'p1', title: 'Fix saves on quit', status: 'in_progress', priority: 1,
            description: '', assignee: '', sprintId: 's1', buildId: null, dueAt: '2026-01-01T00:00:00',
            isPublicRoadmap: false, createdAt: '', updatedAt: '', completedAt: null, archivedAt: null, links: [],
        },
        {
            id: 't2', projectId: 'p1', title: 'Second in line', status: 'planned', priority: 3,
            description: '', assignee: '', sprintId: null, buildId: null, dueAt: null,
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
    { id: 'b2', projectId: 'p1', version: '0.4.0', notes: '', releasedAt: null, createdAt: '', released: false, taskCount: 0, doneCount: 0, documents: [], isPublic: false, downloadUrl: null },
    { id: 'b1', projectId: 'p1', version: '0.3.1', notes: '', releasedAt: '2026-08-17T09:00:00', createdAt: '', released: true, taskCount: 3, doneCount: 3, documents: [], isPublic: false, downloadUrl: null },
];

const CHANNELS: Channel[] = [
    { id: 'c1', title: 'Dev Dairy', telegramChatId: 1, username: 'devdairy', avatarUrl: null },
];

class FakeProjects {
    detail: ProjectDetail | null = DETAIL;
    list_: ProjectSummary[] | null = [SUMMARY, OTHER];
    updates: unknown[] = [];
    async get() { if (!this.detail) throw new Error('nope'); return structuredClone(this.detail); }
    async list() { if (!this.list_) throw new Error('nope'); return structuredClone(this.list_); }
    async update(id: string, name: string, description: string, coverUrl: string | null) {
        this.updates.push({ id, name, description, coverUrl });
        return { ...SUMMARY, id, name, description, coverUrl };
    }
    async setShowcase(_id: string, _enabled: boolean, slug: string | null, _links: string) {
        return { showcaseSlug: slug };
    }
}

class FakeBuilds {
    builds: Build[] | null = BUILDS;
    async list() { if (!this.builds) throw new Error('nope'); return structuredClone(this.builds); }
}

class FakeChannels {
    channels: Channel[] | null = CHANNELS;
    async list() { if (!this.channels) throw new Error('nope'); return structuredClone(this.channels); }
}

class FakeAssets {
    uploaded: File[] = [];
    async upload(file: File) {
        this.uploaded.push(file);
        return { id: 'cover-asset', url: '/media/project-cover.png' };
    }
}

describe('project hub', () => {
    let fixture: ComponentFixture<ProjectComponent>;
    let projects: FakeProjects;
    let builds: FakeBuilds;
    let channels: FakeChannels;
    const t = en.projects;

    const el = () => fixture.nativeElement as HTMLElement;
    const meta = () => [...el().querySelectorAll('app-page-header .page-meta > span:not(.sep)')]
        .map(x => x.textContent?.trim());
    const docRows = () => [...el().querySelectorAll('.doc-list a.doc-row')] as HTMLAnchorElement[];
    const docTitles = () => docRows().map(r => r.querySelector('.doc-title')?.textContent?.trim());
    const sideCards = () => [...el().querySelectorAll('.hub-side .side-card')] as HTMLElement[];
    const kvValue = (label: string) => {
        const cells = [...el().querySelectorAll('.kv > *')];
        const i = cells.findIndex(c => c.tagName === 'B' && c.textContent?.trim() === label);
        return i >= 0 ? cells[i + 1] : null;
    };

    async function create() {
        projects = new FakeProjects();
        builds = new FakeBuilds();
        channels = new FakeChannels();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: ProjectsService, useValue: projects },
                { provide: BuildsService, useValue: builds },
                { provide: ChannelsService, useValue: channels },
                { provide: AssetsService, useValue: new FakeAssets() },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1' })) } },
            ],
        });
        fixture = TestBed.createComponent(ProjectComponent);
        fixture.detectChanges();
        // load(), loadProjects() and loadChannels() are a few awaits deep before the build list lands.
        for (let i = 0; i < 6; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    // ADR-239 clause 6 — the state, the kind, the count and the last edit are the header's meta line.
    it('says what the project is in the header: state tag, kind, documents, last edit', () => {
        expect(meta()).toEqual([
            t.stateActive, t.projectTypes.fullgame.name, t.documentCount(4), expect.stringContaining(t.hub.lastEdit),
        ]);
        expect(el().querySelector('app-page-header .page-meta .tag')?.classList.contains('ok')).toBe(true);
    });

    it('offers Settings and New document as the header\'s two actions', () => {
        const actions = [...el().querySelectorAll('app-page-header .page-actions app-button')].map(b => b.textContent?.trim());
        expect(actions).toEqual([t.settings, t.newDocument]);
    });

    // ADR-169. Continue is a door to a document, so it carries the address a middle click can take
    // to a new tab — and the assertion is the href rather than a spy on the router.
    it('opens the newest document from Continue writing, as a link', () => {
        expect(el().querySelector('.resume-t')?.textContent?.trim()).toBe('Devlog #12');
        const open = el().querySelector('.resume app-button a') as HTMLAnchorElement;
        expect(open.getAttribute('href')).toBe('/editor?draft=d-new');
        expect(el().querySelector('.resume app-button button')).toBeNull();
    });

    it('lists every document, newest first, each on its own address', () => {
        expect(docTitles()).toEqual(['Devlog #12', 'Cave script', 'Design bible', 'Old plan']);
        expect(docRows().map(r => r.getAttribute('href')))
            .toEqual(['/editor?draft=d-new', '/editor?draft=d-mid', '/editor?draft=d-old', '/editor?draft=d-gone']);
    });

    // The strip switches what the card shows: live is published and not archived, drafts is
    // neither, archived is archived whatever else it is.
    it('filters the documents by the strip and the search', () => {
        fixture.componentInstance.docFilter.set('live');
        fixture.detectChanges();
        expect(docTitles()).toEqual(['Cave script']);

        fixture.componentInstance.docFilter.set('drafts');
        fixture.detectChanges();
        expect(docTitles()).toEqual(['Devlog #12', 'Design bible']);

        fixture.componentInstance.docFilter.set('archived');
        fixture.detectChanges();
        expect(docTitles()).toEqual(['Old plan']);

        fixture.componentInstance.docFilter.set('all');
        fixture.componentInstance.docSearch.set('cave');
        fixture.detectChanges();
        expect(docTitles()).toEqual(['Cave script']);
    });

    it('names the next action when the filter finds nothing, and when there is no document at all', () => {
        fixture.componentInstance.docSearch.set('zzz');
        fixture.detectChanges();
        expect(el().querySelector('.doc-list app-empty-state')?.textContent).toContain(t.hub.noDocumentsMatch);

        fixture.componentInstance.project.set({ ...DETAIL, documents: [] });
        fixture.detectChanges();
        const empty = el().querySelector('.doc-list app-empty-state')!;
        expect(empty.textContent).toContain(t.hub.noDocuments);
        expect(empty.querySelector('app-button')?.textContent?.trim()).toBe(t.newDocument);
    });

    it('draws the sprint covering today with its progress, and one task up next', () => {
        const [sprint, next] = sideCards();
        expect(sprint.textContent).toContain(t.hub.sprintLabel(4));
        expect(sprint.textContent).toContain('Autumn build');
        expect(sprint.querySelector('.bar')).toBeTruthy();
        expect(sprint.textContent).toContain(t.hub.sprintUntil(11, 19, formatInZone(DETAIL.currentSprint!.endsAt, 'd MMM')));

        const task = next.querySelector('a.next-task') as HTMLAnchorElement;
        expect(task.getAttribute('href')).toBe('/projects/p1/tasks?task=t1');
        expect(task.querySelector('.tag')?.textContent?.trim()).toBe('P1');
        expect(next.querySelectorAll('a.next-task').length).toBe(1);
    });

    it('says no sprint covers today instead of drawing an empty one', () => {
        fixture.componentInstance.project.set({ ...DETAIL, currentSprint: null });
        fixture.detectChanges();
        const sprint = sideCards()[0];
        expect(sprint.textContent).toContain(t.planner.noCurrentSprint);
        expect(sprint.querySelector('.bar')).toBeNull();
    });

    // ADR-239 clause 12 — a project has no channel table, so the Telegram row lists the account's
    // channels; the showcase is the one address a project owns (ADR-134).
    it('lists where the work goes from what the page already holds', () => {
        expect(kvValue(t.hub.telegram)?.textContent).toContain('@devdairy');
        expect(kvValue(t.hub.publicPage)?.textContent).toContain(t.hub.notPublished);
        expect(kvValue(t.assets.title)?.textContent?.trim()).toBe(t.hub.filesCount(2481));
        expect(kvValue(t.builds.title)?.textContent?.trim()).toBe(t.hub.versionsCount(2));
    });

    // ADR-160 rule 4. 0 would say "no versions yet", which is a different sentence.
    it('shows a dash, never a zero, when the build list or the project list cannot be asked', () => {
        fixture.componentInstance.builds.set(null);
        fixture.componentInstance.projects.set([]);
        fixture.detectChanges();
        expect(kvValue(t.builds.title)?.textContent?.trim()).toBe('—');
        expect(kvValue(t.assets.title)?.textContent?.trim()).toBe('—');
        // …and the header goes quiet about the last edit rather than inventing a date.
        expect(meta()).toEqual([t.stateActive, t.projectTypes.fullgame.name, t.documentCount(4)]);
    });

    it('says no channel is connected when the account has none', () => {
        fixture.componentInstance.channels.set([]);
        fixture.detectChanges();
        expect(kvValue(t.hub.telegram)?.textContent?.trim()).toBe(t.hub.noChannel);
    });

    // T-353 — the logo comes out of the one asset window now, so the picked asset IS the answer
    // and there is no deferred upload left on save.
    it('saves the project logo picked in the asset window', async () => {
        const component = fixture.componentInstance;
        component.startEdit();
        component.pickedCover({ id: 'a1', localPath: 'project-cover.png' } as never);
        fixture.detectChanges();

        expect(component.coverPickerOpen()).toBe(false);
        expect(el().querySelector('.cover-field')?.textContent).toContain(t.edit.logoLabel);
        await component.saveEdit();

        expect(projects.updates).toEqual([expect.objectContaining({ coverUrl: '/media/project-cover.png' })]);
        expect(component.project()?.coverUrl).toBe('/media/project-cover.png');
        expect(component.summary()?.coverUrl).toBe('/media/project-cover.png');
    });
});
