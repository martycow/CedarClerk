import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectTasksComponent } from './project-tasks.component';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { GameTask, TasksService } from '../core/tasks.service';
import { Sprint, SprintsService } from '../core/sprints.service';
import { BuildsService } from '../core/builds.service';
import { en } from '../core/i18n/en';
import { AssetsService } from '../core/assets.service';

const SPRINT: Sprint = {
    id: 's1', projectId: 'p1', number: 4, name: 'Autumn build',
    startsAt: '2026-08-01T00:00:00', endsAt: '2099-08-31T00:00:00',
    state: 'current', taskCount: 2, doneCount: 1, overdueCount: 1,
};

const DETAIL: ProjectDetail = {
    id: 'p1', name: 'Cedar Quest', description: '', projectType: 'fullgame', coverUrl: null, teamId: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null, showcaseSlug: null, showcaseLinks: '', showcaseGallery: '', showcaseTrailerUrl: null, showcaseBlocksJson: '', customDomain: null,
    pressContactEmail: null, pressPrice: null, pressEngine: null, pressGenre: null, pressFactsheetRows: null,
    documents: [], upNext: [], taskCounts: {}, currentSprint: SPRINT, openTaskCount: 2,
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
    links: unknown[] = [];
    async list() { return structuredClone(TASKS); }
    async create(_projectId: string, input: unknown) {
        this.created.push(input);
        return task({ id: 'created', ...(input as Partial<GameTask>) });
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
        expect(rows.length).toBe(3);
        expect(rows.every(r => r.tagName === 'A')).toBe(true);
        expect(rows.map(r => r.getAttribute('href')).sort()).toEqual([
            '/projects/p1/tasks?task=t1', '/projects/p1/tasks?task=t2', '/projects/p1/tasks?task=t3',
        ]);
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
    it('names the next action in every empty column', () => {
        fixture.componentInstance.tasks.set([]);
        fixture.detectChanges();

        expect(column(t.status.backlog).querySelector('app-empty-state')?.textContent).toContain(t.emptyBacklog);
        expect(column(t.status.planned).querySelector('app-empty-state')?.textContent).toContain(t.emptyPlanned('S4'));
        expect(column(t.status.in_progress).querySelector('app-empty-state')?.textContent).toContain(t.emptyInProgress);
        expect(column(t.status.done).querySelector('app-empty-state')?.textContent).toContain(t.emptyDone);

        const createButtons = [...el().querySelectorAll('app-page-header app-button')]
            .filter(button => button.textContent?.trim() === t.newTask);
        expect(createButtons.length).toBe(1);
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
});
