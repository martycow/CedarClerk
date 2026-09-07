import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectComponent, daysSince, journalLevel, journalLink } from './project.component';
import { ActivityItem, ProjectDetail, ProjectSummary, ProjectsService } from '../core/projects.service';
import { Channel, ChannelsService } from '../core/channels.service';
import { en } from '@localization/en';
import { formatInZone } from '../core/display-time';
import { AssetsService } from '../core/assets.service';

const SUMMARY: ProjectSummary = {
    id: 'p1', name: 'Cedar Quest', description: '', projectType: 'fullgame', coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null,
    documentCount: 3, openTaskCount: 8, assetCount: 2481, buildCount: 2, latestBuildVersion: '0.3.1',
    lastActivityAt: '2026-08-19T11:00:00',
    lastPublishedAt: new Date(Date.now() - 3 * 86_400_000).toISOString(),
    engine: '', targetPlatforms: [],
};

const OTHER: ProjectSummary = { ...SUMMARY, id: 'p2', name: 'Night Lanterns', assetCount: 0 };

const DETAIL: ProjectDetail = {
    id: 'p1', name: 'Cedar Quest', description: 'A game about a bench.', projectType: 'fullgame',
    coverUrl: null, teamId: null, createdAt: '2026-08-01T09:00:00', archivedAt: null, engine: '', targetPlatforms: [],
    showcaseSlug: null, showcaseLinks: '', showcaseGallery: '', showcaseTrailerUrl: null, showcaseBlocksJson: '', customDomain: null,
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

const JOURNAL: ActivityItem[] = [
    { at: '2026-08-19T11:00:00Z', kind: 'document-updated', title: 'Devlog #12', subtitle: 'post', href: '/editor?draft=d-new', actor: null },
    { at: '2026-08-18T09:00:00Z', kind: 'publish-failed', title: 'Devlog #11', subtitle: 'x · rate limited', href: null, actor: null },
    { at: '2026-08-17T09:00:00Z', kind: 'blog-published', title: 'Devlog #11', subtitle: 'blog', href: 'https://blog.example/devlog-11', actor: null },
    { at: '2026-08-16T09:00:00Z', kind: 'build-created', title: '0.3.1', subtitle: null, href: '/projects/p1/builds', actor: null },
    { at: '2026-08-15T09:00:00Z', kind: 'task-completed', title: 'Fix saves on quit', subtitle: 'done', href: '/projects/p1/tasks', actor: 'Marty' },
];

const CHANNELS: Channel[] = [
    { id: 'c1', title: 'Dev Dairy', telegramChatId: 1, username: 'devdairy', avatarUrl: null },
];

class FakeProjects {
    detail: ProjectDetail | null = DETAIL;
    list_: ProjectSummary[] | null = [SUMMARY, OTHER];
    updates: unknown[] = [];
    journal: ActivityItem[] | null = JOURNAL;
    takes: number[] = [];
    async activity(_id: string, take: number) {
        this.takes.push(take);
        if (!this.journal) throw new Error('nope');
        return { items: this.journal.slice(0, take) };
    }
    async get() { if (!this.detail) throw new Error('nope'); return structuredClone(this.detail); }
    async list() { if (!this.list_) throw new Error('nope'); return structuredClone(this.list_); }
    async update(id: string, name: string, description: string, coverUrl: string | null, engine?: string, targetPlatforms?: string[]) {
        this.updates.push({ id, name, description, coverUrl, engine, targetPlatforms });
        return { ...SUMMARY, id, name, description, coverUrl };
    }
    async setShowcase(_id: string, _enabled: boolean, slug: string | null, _links: string) {
        return { showcaseSlug: slug };
    }
}

class FakeChannels {
    channels: Channel[] | null = CHANNELS;
    streak: number | null = 5;
    async list() { if (!this.channels) throw new Error('nope'); return structuredClone(this.channels); }
    async publishingStats() {
        if (this.streak === null) throw new Error('nope');
        return { currentStreakWeeks: this.streak, longestStreakWeeks: 9, weeks: [] };
    }
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
        channels = new FakeChannels();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: ProjectsService, useValue: projects },
                { provide: ChannelsService, useValue: channels },
                { provide: AssetsService, useValue: new FakeAssets() },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1' })) } },
            ],
        });
        fixture = TestBed.createComponent(ProjectComponent);
        fixture.detectChanges();
        // load(), loadProjects() and loadChannels() are a few awaits deep before the rows land.
        for (let i = 0; i < 6; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    // ADR-239 clause 6 — the state, the kind, the count and the last edit are the header's meta line.
    it('says what the project is in the header: state tag, kind, documents, last edit', () => {
        expect(meta()).toEqual([
            t.stateActive, t.projectTypes.fullgame.name, t.documentCount(4), expect.stringContaining(t.hub.lastEdit),
            t.hub.sinceLastPublish(3),
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
    it('shows a dash, never a zero, when the project list cannot be asked', () => {
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

    // T-249 — the journal: one stamped line per thing that happened, the address on the title.
    it('journals what happened, newest first, with the kind stamped and the title linked', () => {
        const lines = [...el().querySelectorAll('.journal app-log-line')] as HTMLElement[];
        expect(lines.length).toBe(5);
        expect(lines.map(l => l.querySelector('app-stamp-badge')?.textContent?.trim())).toEqual([
            t.hub.journalKinds['document-updated'], t.hub.journalKinds['publish-failed'], t.hub.journalKinds['blog-published'],
            t.hub.journalKinds['build-created'], t.hub.journalKinds['task-completed'],
        ]);
        expect(lines[0].querySelector('a.jl-title')?.getAttribute('href')).toBe('/editor?draft=d-new');
        expect(lines[1].querySelector('a.jl-title')).toBeNull();
        expect(lines[1].querySelector('.jl-title')?.textContent?.trim()).toBe('Devlog #11');
        expect(lines[2].querySelector('a.jl-title')?.getAttribute('href')).toBe('https://blog.example/devlog-11');
        expect(lines[2].querySelector('a.jl-title')?.getAttribute('target')).toBe('_blank');
        expect(lines[4].textContent).toContain('Marty');
        expect(lines[0].querySelector('.ll-time')?.textContent?.trim()).toBe(formatInZone(JOURNAL[0].at, 'HH:mm'));
        expect(lines[0].querySelector('.ll-at')?.textContent?.trim()).toBe(formatInZone(JOURNAL[0].at, 'd MMM'));
    });

    it('stamps a kind by what it means: finished is ok, a cut version is build, a failure warns, the rest inform', () => {
        expect(journalLevel('blog-published')).toBe('ok');
        expect(journalLevel('telegram-published')).toBe('ok');
        expect(journalLevel('published')).toBe('ok');
        expect(journalLevel('build-released')).toBe('ok');
        expect(journalLevel('task-completed')).toBe('ok');
        expect(journalLevel('build-created')).toBe('build');
        expect(journalLevel('publish-failed')).toBe('warn');
        expect(journalLevel('document-created')).toBe('info');
        expect(journalLevel('document-updated')).toBe('info');
        expect(journalLevel('task-created')).toBe('info');
    });

    it('splits an in-app address into a route and its query, and leaves an absolute URL alone', () => {
        expect(journalLink('/editor?draft=d-new')).toEqual({ external: false, path: '/editor', query: { draft: 'd-new' } });
        expect(journalLink('/projects/p1/tasks')).toEqual({ external: false, path: '/projects/p1/tasks', query: {} });
        expect(journalLink('https://t.me/devdairy/12')).toEqual({ external: true, url: 'https://t.me/devdairy/12' });
    });

    it('asks for twelve lines, offers more only when the page came back full, and doubles the ask', async () => {
        expect(projects.takes).toEqual([12]);
        expect(el().querySelector('.journal-foot')).toBeNull();

        projects.journal = Array.from({ length: 30 }, (_, i) => ({ ...JOURNAL[0], title: `Line ${i}` }));
        await fixture.componentInstance.load('p1');
        for (let i = 0; i < 4; i++) await Promise.resolve();
        fixture.detectChanges();
        expect(el().querySelectorAll('.journal app-log-line').length).toBe(12);
        expect(el().querySelector('.journal-foot app-button')?.textContent?.trim()).toBe(t.hub.journalMore);

        fixture.componentInstance.showMoreJournal();
        for (let i = 0; i < 4; i++) await Promise.resolve();
        fixture.detectChanges();
        expect(projects.takes.at(-1)).toBe(24);
        expect(el().querySelectorAll('.journal app-log-line').length).toBe(24);
    });

    it('prints the empty sentence when nothing happened, and when the journal could not be asked', async () => {
        projects.journal = [];
        await fixture.componentInstance.load('p1');
        for (let i = 0; i < 4; i++) await Promise.resolve();
        fixture.detectChanges();
        expect(el().querySelector('.journal app-empty-state')?.textContent).toContain(t.hub.journalEmpty);

        projects.journal = null;
        await fixture.componentInstance.load('p1');
        for (let i = 0; i < 4; i++) await Promise.resolve();
        fixture.detectChanges();
        expect(el().querySelectorAll('.journal app-log-line').length).toBe(0);
        expect(el().querySelector('.journal app-empty-state')?.textContent).toContain(t.hub.journalEmpty);
    });

    // T-166 — the days since the last post: plain under a week, a muted tag from seven, warn from
    // fourteen, and "nothing published yet" when there never was one. No score, nothing to lose.
    it('counts whole days since a publish', () => {
        const now = Date.parse('2026-09-05T10:00:00Z');
        expect(daysSince('2026-09-05T02:00:00Z', now)).toBe(0);
        expect(daysSince('2026-09-04T10:00:01Z', now)).toBe(0);
        expect(daysSince('2026-09-04T10:00:00Z', now)).toBe(1);
        expect(daysSince('2026-08-22T10:00:00Z', now)).toBe(14);
        expect(daysSince('2026-09-06T10:00:00Z', now)).toBe(0);
    });

    it('nudges quietly in the header: a sentence under a week, a tag from seven days, warn from fourteen', () => {
        const tags = () => [...el().querySelectorAll('app-page-header .page-meta .tag')].map(x => ({
            text: x.textContent?.trim(), warn: x.classList.contains('warn'), muted: x.classList.contains('muted'),
        }));
        const publishedDaysAgo = (n: number | null) => {
            const at = n === null ? null : new Date(Date.now() - n * 86_400_000 - 60_000).toISOString();
            fixture.componentInstance.projects.set([{ ...SUMMARY, lastPublishedAt: at }, OTHER]);
            fixture.detectChanges();
        };

        publishedDaysAgo(3);
        expect(meta().at(-1)).toBe(t.hub.sinceLastPublish(3));
        expect(tags().map(x => x.text)).toEqual([t.stateActive]);

        publishedDaysAgo(7);
        expect(tags().at(-1)).toEqual({ text: t.hub.sinceLastPublish(7), warn: false, muted: true });

        publishedDaysAgo(14);
        expect(tags().at(-1)).toEqual({ text: t.hub.sinceLastPublish(14), warn: true, muted: false });

        publishedDaysAgo(null);
        expect(tags().at(-1)).toEqual({ text: t.hub.neverPublished, warn: false, muted: true });
    });

    // ADR-281 — the one account number on a project screen, labelled as the account's.
    it('prints the account streak as one labelled row, and no row for zero or a failed call', async () => {
        expect(kvValue(t.hub.streakLabel)?.textContent?.trim()).toBe(t.hub.streak(5));

        fixture.componentInstance.streakWeeks.set(null);
        fixture.detectChanges();
        expect(kvValue(t.hub.streakLabel)).toBeNull();

        channels.streak = 0;
        fixture.componentInstance.streakWeeks.set(7);
        await fixture.componentInstance['loadStreak']();
        fixture.detectChanges();
        expect(kvValue(t.hub.streakLabel)).toBeNull();

        channels.streak = null;
        fixture.componentInstance.streakWeeks.set(7);
        await fixture.componentInstance['loadStreak']();
        fixture.detectChanges();
        expect(kvValue(t.hub.streakLabel)).toBeNull();
    });

    // T-247 — the kit's hero tag, drawn only from what is stored (ADR-160 clause 7).
    it('puts "Unity · Windows, Switch" after the kind once either is set, and nothing when neither is', () => {
        expect(meta()).not.toContain(expect.stringContaining('·'));

        fixture.componentInstance.project.set({ ...DETAIL, engine: 'unity', targetPlatforms: ['switch', 'windows'] });
        fixture.detectChanges();
        expect(meta()[2]).toBe(`${t.engines.unity} · ${t.platforms.windows}, ${t.platforms.switch}`);

        fixture.componentInstance.project.set({ ...DETAIL, engine: '', targetPlatforms: ['web'] });
        fixture.detectChanges();
        expect(meta()[2]).toBe(t.platforms.web);

        fixture.componentInstance.project.set({ ...DETAIL, engine: 'godot', targetPlatforms: [] });
        fixture.detectChanges();
        expect(meta()[2]).toBe(t.engines.godot);
    });

    it('offers the engine as a select and the platforms as checkboxes, and sends each only when it moved', async () => {
        const component = fixture.componentInstance;
        component.startEdit();
        fixture.detectChanges();
        expect(el().querySelectorAll('#edit-engine option').length).toBe(1 + 12);
        expect(el().querySelectorAll('.platform-grid app-checkbox').length).toBe(10);

        await component.saveEdit();
        expect(projects.updates.at(-1)).toEqual(expect.objectContaining({ engine: undefined, targetPlatforms: undefined }));

        component.startEdit();
        component.editEngine.set('unity');
        component.togglePlatform('switch', true);
        component.togglePlatform('windows', true);
        await component.saveEdit();
        expect(projects.updates.at(-1)).toEqual(expect.objectContaining({ engine: 'unity', targetPlatforms: ['windows', 'switch'] }));
        expect(component.project()?.engine).toBe('unity');
        expect(component.summary()?.targetPlatforms).toEqual(['windows', 'switch']);

        component.startEdit();
        component.togglePlatform('windows', false);
        await component.saveEdit();
        expect(projects.updates.at(-1)).toEqual(expect.objectContaining({ engine: undefined, targetPlatforms: ['switch'] }));
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
