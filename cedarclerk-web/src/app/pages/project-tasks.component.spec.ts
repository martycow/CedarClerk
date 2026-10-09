import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectTasksComponent } from './project-tasks.component';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { GameTask, TasksService } from '../core/tasks.service';
import { Sprint, SprintsService } from '../core/sprints.service';
import { BuildsService } from '../core/builds.service';
import { en } from '@localization/en';
import { AssetsService } from '../core/assets.service';
import { setDisplayTimeZone } from '../core/display-time';

const SPRINT: Sprint = {
    id: 's1', projectId: 'p1', number: 4, name: 'Autumn build',
    startsAt: '2026-08-01T00:00:00', endsAt: '2099-08-31T00:00:00',
    state: 'current', taskCount: 2, doneCount: 1, overdueCount: 1,
};

const DETAIL: ProjectDetail = {
    id: 'p1', name: 'Cedar Quest', description: '', createdFromPreset: 'fullgame', modules: {}, coverUrl: null, teamId: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null, showcaseSlug: null, showcaseLinks: '', showcaseGallery: '', showcaseTrailerUrl: null, showcaseBlocksJson: '', customDomain: null,
    pressContactEmail: null, pressPrice: null, pressEngine: null, pressGenre: null, pressFactsheetRows: null,
    documents: [], upNext: [], taskCounts: {}, currentSprint: SPRINT, openTaskCount: 2,
    engine: '', targetPlatforms: [],
};

const task = (over: Partial<GameTask>): GameTask => ({
    id: 't', projectId: 'p1', title: 'Task', status: 'backlog', priority: 2, description: '',
    assignee: '', sprintId: null, buildId: null, dueAt: null, isPublicRoadmap: false,
    createdAt: '2026-08-01T09:00:00', updatedAt: '2026-08-01T09:00:00', completedAt: null,
    archivedAt: null, links: [], ...over,
});

// Three tasks in three different columns, and only one of them matches the search below — so a
// count taken from the filtered set and a count taken from the whole set cannot come out equal.
const TASKS: GameTask[] = [
    task({ id: 't1', title: 'Fix saves on quit', status: 'backlog', priority: 1, dueAt: '2020-01-31T00:00:00',
        links: [{ type: 'document', id: 'd1', label: 'Design bible', url: null }, { type: 'document', id: 'd2', label: 'Notes', url: null }] }),
    task({ id: 't2', title: 'Cave lighting', status: 'in_progress', priority: 2, sprintId: 's1' }),
    task({ id: 't3', title: 'Ship 0.3.1', status: 'done', priority: 3, completedAt: '2026-08-02T09:00:00' }),
];

class FakeTasks {
    created: unknown[] = [];
    updated: { id: string; input: Record<string, unknown> }[] = [];
    links: unknown[] = [];
    async list() { return structuredClone(TASKS); }
    async create(_projectId: string, input: unknown) {
        this.created.push(input);
        return task({ id: 'created', ...(input as Partial<GameTask>) });
    }
    async update(id: string, input: Record<string, unknown>) {
        this.updated.push({ id, input });
        return { ...(TASKS.find(item => item.id === id) ?? task({ id })), ...input };
    }
    async link(taskId: string, type: string, id: string) { this.links.push({ taskId, type, id }); }
}

class FakeAssets {
    uploaded: File[] = [];
    async upload(file: File) {
        this.uploaded.push(file);
        return { id: `asset-${this.uploaded.length}`, url: '/media/file' };
    }
}

describe('project tasks', () => {
    let fixture: ComponentFixture<ProjectTasksComponent>;
    let tasksApi: FakeTasks;
    let assetsApi: FakeAssets;
    const t = en.projects.tasks;

    const el = () => fixture.nativeElement as HTMLElement;
    const cards = () => [...el().querySelectorAll('a.task-card')] as HTMLAnchorElement[];
    const columns = () => [...el().querySelectorAll('section.column')] as HTMLElement[];
    const column = (title: string) =>
        columns().find(c => c.querySelector('.column-name')?.textContent?.trim() === title)!;
    const columnCount = (title: string) => column(title).querySelector('.column-count')?.textContent?.trim();
    const meta = () => [...el().querySelectorAll('app-page-header .page-meta > span:not(.sep)')]
        .map(x => x.textContent?.trim());

    async function create() {
        localStorage.removeItem('cedar.taskView');
        setDisplayTimeZone('America/Los_Angeles');
        tasksApi = new FakeTasks();
        assetsApi = new FakeAssets();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: TasksService, useValue: tasksApi },
                { provide: AssetsService, useValue: assetsApi },
                { provide: ProjectsService, useValue: { async get() { return structuredClone(DETAIL); } } },
                { provide: SprintsService, useValue: { async list() { return [structuredClone(SPRINT)]; } } },
                { provide: BuildsService, useValue: { async list() { return []; } } },
                {
                    provide: ActivatedRoute,
                    useValue: {
                        paramMap: of(convertToParamMap({ id: 'p1' })),
                        queryParamMap: of(convertToParamMap({})),
                    },
                },
            ],
        });
        fixture = TestBed.createComponent(ProjectTasksComponent);
        fixture.detectChanges();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    // ADR-163 — opening a card is a navigation, so the card is an anchor and carries the query
    // itself. A click handler has no URL to hand the browser, which is middle click, copy link
    // address and the hover preview gone.
    it('hangs every card as an anchor addressed at its own task', () => {
        expect(cards().length).toBe(3);
        expect(cards().map(p => p.getAttribute('href'))).toEqual([
            '/projects/p1/tasks?task=t1', '/projects/p1/tasks?task=t2', '/projects/p1/tasks?task=t3',
        ]);
    });

    // Board.png — the card is the title, the priority tag, the sprint and the linked-document count.
    it('draws the card as the artboard does: priority, sprint, linked documents', () => {
        const first = cards()[0];
        expect(first.querySelector('.task-title')?.textContent?.trim()).toBe('Fix saves on quit');
        expect(first.querySelector('.tag')?.textContent?.trim()).toBe('P1');
        expect(first.querySelector('.tag')?.classList.contains('danger')).toBe(true);
        expect(first.querySelector('.task-links')?.textContent?.trim()).toBe('2');
        expect(first.querySelector('.task-due')?.textContent).toContain(t.overdue);

        const second = cards()[1];
        expect(second.querySelector('.task-sprint')?.textContent?.trim()).toBe('S4');
        expect(second.querySelector('.task-links')).toBeNull();
    });

    it('gives a list row the same address the board card has', () => {
        fixture.componentInstance.setView('list');
        fixture.detectChanges();

        const rows = [...el().querySelectorAll('.tasks-row:not(.head-row)')] as HTMLElement[];
        const links = rows.map(row => row.querySelector<HTMLAnchorElement>('.task-row-link')!);
        expect(rows.length).toBe(3);
        expect(rows.every(row => row.tagName === 'DIV' && row.getAttribute('role') === 'row')).toBe(true);
        expect(links.every(link => link.tagName === 'A' && !link.hasAttribute('role'))).toBe(true);
        expect(links.map(link => link.getAttribute('href')).sort()).toEqual([
            '/projects/p1/tasks?task=t1', '/projects/p1/tasks?task=t2', '/projects/p1/tasks?task=t3',
        ]);
    });

    it('sorts every list column with aria-sort and keeps missing due dates last', () => {
        fixture.componentInstance.setView('list');
        fixture.detectChanges();
        expect(el().querySelector('.tasks-table')?.getAttribute('role')).toBe('table');
        expect(el().querySelector('[role="grid"]')).toBeNull();
        expect([...el().querySelectorAll('.tasks-row:not(.head-row)')]
            .every(row => row.getAttribute('role') === 'row' && row.querySelectorAll('[role="cell"]').length === 5)).toBe(true);
        const header = (label: string) => [...el().querySelectorAll<HTMLElement>('[role="columnheader"]')]
            .find(cell => cell.textContent?.includes(label))!;
        const rowIds = () => [...el().querySelectorAll<HTMLAnchorElement>('.task-row-link')]
            .map(link => new URL(link.href).searchParams.get('task'));

        expect(header(t.colPriority).getAttribute('aria-sort')).toBe('ascending');
        header(t.colLinks).querySelector<HTMLButtonElement>('button')!.click();
        fixture.detectChanges();
        expect(header(t.colLinks).getAttribute('aria-sort')).toBe('ascending');
        expect(rowIds()).toEqual(['t2', 't3', 't1']);

        header(t.colDue).querySelector<HTMLButtonElement>('button')!.click();
        fixture.detectChanges();
        expect(rowIds()).toEqual(['t1', 't2', 't3']);
        header(t.colDue).querySelector<HTMLButtonElement>('button')!.click();
        fixture.detectChanges();
        expect(header(t.colDue).getAttribute('aria-sort')).toBe('descending');
        expect(rowIds()).toEqual(['t1', 't2', 't3']);
    });

    // A column header counts the column, not the filter: the strips above say what the filter is
    // doing, and a header that moved with it would leave nothing saying how many there really are.
    it('counts a column against every task in it while the search hides two of them', () => {
        fixture.componentInstance.search.set('cave');
        fixture.detectChanges();

        expect(cards().length).toBe(1);
        expect(columnCount(t.status.backlog)).toBe('1');
        expect(columnCount(t.status.in_progress)).toBe('1');
        expect(columnCount(t.status.done)).toBe('1');
        expect(columnCount(t.status.planned)).toBe('0');
    });

    it('replaces both views with a clearable filtered-empty state', () => {
        const component = fixture.componentInstance;
        component.search.set('nothing matches');
        fixture.detectChanges();

        expect(el().querySelector('.board')).toBeNull();
        expect(el().querySelector('.filtered-empty')?.textContent).toContain(t.emptyFiltered);
        const clear = el().querySelector('.filtered-empty app-button button') as HTMLButtonElement;
        expect(clear.textContent?.trim()).toBe(t.clearFilters);
        clear.click();
        fixture.detectChanges();
        expect(cards()).toHaveLength(3);

        component.setView('list');
        component.filter.set('open');
        component.search.set('ship');
        fixture.detectChanges();
        expect(el().querySelector('.tasks-table')).toBeNull();
        expect(el().querySelector('.filtered-empty app-button')).not.toBeNull();
        component.clearCollectionFilters();
        fixture.detectChanges();
        expect(el().querySelectorAll('.tasks-row:not(.head-row)')).toHaveLength(3);
    });

    // ADR-239 clause 6 — the rule's readouts are the header's meta line: the tally, the sprint
    // covering today, its days left, and the late count as a warning tag.
    it('carries the tally, the sprint and the late count in the header', () => {
        expect(el().querySelector('h1')?.textContent?.trim()).toBe(t.title);
        expect(meta()).toEqual([
            t.openCount(2), t.sprintMeta(4, 'Autumn build'), expect.stringContaining('left'), t.overdueCount(1),
        ]);
        expect(el().querySelector('app-page-header .tag.warn')?.textContent?.trim()).toBe(t.overdueCount(1));
    });

    // ADR-239 clause 8 — an empty column names what lands in it, never "nothing here".
    it('keeps one primary zero-state action and four contextual column placements', () => {
        fixture.componentInstance.tasks.set([]);
        fixture.detectChanges();

        expect(column(t.status.backlog).querySelector('app-empty-state')?.textContent).toContain(t.emptyBacklog);
        expect(column(t.status.planned).querySelector('app-empty-state')?.textContent).toContain(t.emptyPlanned('S4'));
        expect(column(t.status.in_progress).querySelector('app-empty-state')?.textContent).toContain(t.emptyInProgress);
        expect(column(t.status.done).querySelector('app-empty-state')?.textContent).toContain(t.emptyDone);

        const headerCreateButtons = [...el().querySelectorAll('app-page-header app-button')]
            .filter(button => button.textContent?.trim() === t.newTask);
        expect(headerCreateButtons).toEqual([]);

        const zeroCreate = column(t.status.backlog).querySelector<HTMLButtonElement>('app-empty-state app-button button')!;
        expect(zeroCreate.textContent?.trim()).toBe(t.newTask);
        expect(el().querySelectorAll('.column-head app-button').length).toBe(4);

        zeroCreate.click();
        fixture.detectChanges();
        expect(fixture.componentInstance.creating()).toBe(true);
        expect(fixture.componentInstance.newStatus()).toBe('backlog');

        fixture.componentInstance.creating.set(false);
        fixture.detectChanges();
        const plannedPlacement = column(t.status.planned).querySelector<HTMLButtonElement>('.column-head app-button button')!;
        plannedPlacement.click();
        fixture.detectChanges();
        expect(fixture.componentInstance.creating()).toBe(true);
        expect(fixture.componentInstance.newStatus()).toBe('planned');

        fixture.componentInstance.creating.set(false);
        fixture.componentInstance.setView('list');
        fixture.detectChanges();
        expect(el().querySelector('app-empty-state.list-empty app-button')?.textContent?.trim()).toBe(t.newTask);
    });

    it('shows a compact visible label on the toolbar search', () => {
        const search = el().querySelector('app-input.search-input')!;
        expect(search.getAttribute('data-surface')).toBe('chrome');
        expect(search.querySelector('label')?.textContent?.trim()).toBe(t.search);
        expect(search.querySelector('label')?.getAttribute('for')).toBe('tasks-search');
    });

    it('creates a named, described task and links every uploaded file', async () => {
        const component = fixture.componentInstance;
        component.startCreating();
        component.newTitle.set('Record trailer');
        component.newDescription.set('Capture the release build.');
        component.newFiles.set([
            new File(['poster'], 'poster.png', { type: 'image/png' }),
            new File(['notes'], 'notes.pdf', { type: 'application/pdf' }),
        ]);
        fixture.detectChanges();

        const labels = [...el().querySelectorAll('.task-modal label')].map(x => x.textContent?.trim());
        expect(labels).toContain(t.fieldName);
        expect(labels).toContain(t.fieldDescription);
        expect(labels).toContain(t.fieldFiles);

        await component.create();

        expect(tasksApi.created).toEqual([expect.objectContaining({
            title: 'Record trailer', description: 'Capture the release build.',
        })]);
        expect(assetsApi.uploaded.map(file => file.name)).toEqual(['poster.png', 'notes.pdf']);
        expect(tasksApi.links).toEqual([
            { taskId: 'created', type: 'attachment', id: 'asset-1' },
            { taskId: 'created', type: 'attachment', id: 'asset-2' },
        ]);
        expect(component.creating()).toBe(false);
    });

    it('offers the sprint in the new-task modal and opens on the sprint the board is filtered to', async () => {
        const component = fixture.componentInstance;
        component.startCreating();
        fixture.detectChanges();
        expect(el().querySelector('#new-task-sprint')).toBeTruthy();
        expect(component.newSprintId()).toBe('');
        component.newTitle.set('No sprint yet');
        await component.create();
        expect(tasksApi.created[0]).not.toHaveProperty('sprintId');

        component.sprintFilter.set(SPRINT.id);
        component.startCreating();
        component.newTitle.set('Sprint work');
        await component.create();
        expect(tasksApi.created[1]).toEqual(expect.objectContaining({ title: 'Sprint work', sprintId: SPRINT.id }));
    });

    it('names the edit card and gives the public-roadmap choice the full field grid', () => {
        const component = fixture.componentInstance;
        component.openTaskId.set('t1');
        component.beginEdit(TASKS[0]);
        fixture.detectChanges();

        const modal = el().querySelector('app-modal')!;
        expect(modal.querySelector('[modal-title]')?.textContent).toContain(t.editTask);
        expect(modal.querySelector('app-input.task-title-input label')?.textContent?.trim()).toBe(t.fieldName);
        expect(modal.querySelector('.roadmap-field')).not.toBeNull();
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
    });

    it('keeps every editable value local until one Save payload, including the free-text assignee', async () => {
        const component = fixture.componentInstance;
        const opened = TASKS[0];
        component.beginEdit(opened);
        component.draftTitle.set('Release trailer');
        component.draftDescription.set('Capture the final build.');
        component.draftAssignee.set('Freelance capture artist');
        component.draftDue.set('2026-08-11');
        component.draftStatus.set('planned');
        component.draftPriority.set(3);
        component.draftSprintId.set('s1');
        component.draftBuildId.set('b1');
        component.draftPublicRoadmap.set(true);

        expect(tasksApi.updated).toEqual([]);
        await component.saveEdits(opened);

        expect(tasksApi.updated).toEqual([{
            id: 't1',
            input: {
                title: 'Release trailer',
                description: 'Capture the final build.',
                assignee: 'Freelance capture artist',
                status: 'planned',
                priority: 3,
                isPublicRoadmap: true,
                dueAt: '2026-08-11T07:00:00.000Z',
                sprintId: 's1',
                buildId: 'b1',
            },
        }]);
    });

    it('round-trips a due civil date in the account timezone and does not mark its last hour overdue', async () => {
        const component = fixture.componentInstance;
        const opened = task({ id: 'tz', dueAt: '2026-08-10T15:00:00.000Z' });
        setDisplayTimeZone('Asia/Tokyo');

        component.beginEdit(opened);
        expect(component.draftDue()).toBe('2026-08-11');
        await component.saveEdits(opened);
        expect(tasksApi.updated.at(-1)?.input['dueAt']).toBe('2026-08-10T15:00:00.000Z');

        setDisplayTimeZone('America/Los_Angeles');
        const pacificDue = task({ dueAt: '2026-08-11T07:00:00.000Z' });
        expect(component.overdue(pacificDue, new Date('2026-08-12T06:59:00.000Z'))).toBe(false);
        expect(component.overdue(pacificDue, new Date('2026-08-12T07:00:00.000Z'))).toBe(true);
    });

    it('marks done through the same Save payload without losing typed fields', async () => {
        const component = fixture.componentInstance;
        const opened = TASKS[1];
        component.beginEdit(opened);
        component.draftTitle.set('Cave lighting pass');
        component.draftAssignee.set('Alex — external');

        await component.toggleDone(opened);

        expect(tasksApi.updated.at(-1)).toEqual(expect.objectContaining({
            id: 't2',
            input: expect.objectContaining({
                title: 'Cave lighting pass',
                assignee: 'Alex — external',
                status: 'done',
            }),
        }));
    });
});
