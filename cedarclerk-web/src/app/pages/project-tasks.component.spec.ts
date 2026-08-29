import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectTasksComponent } from './project-tasks.component';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { GameTask, TasksService } from '../core/tasks.service';
import { SprintsService } from '../core/sprints.service';
import { BuildsService } from '../core/builds.service';
import { RulerService } from '../core/ruler.service';
import { en } from '../core/i18n/en';
import { AssetsService } from '../core/assets.service';

const DETAIL: ProjectDetail = {
    id: 'p1', name: 'Cedar Quest', description: '', projectType: 'fullgame', coverUrl: null,
    createdAt: '2026-08-01T09:00:00', archivedAt: null, showcaseSlug: null, showcaseLinks: '', showcaseGallery: '', showcaseTrailerUrl: null, customDomain: null,
    pressContactEmail: null, pressPrice: null, pressEngine: null, pressGenre: null, pressFactsheetRows: null,
    documents: [], upNext: [], taskCounts: {}, currentSprint: null, openTaskCount: 2,
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
    task({ id: 't1', title: 'Fix saves on quit', status: 'backlog', priority: 1, dueAt: '2020-01-31T00:00:00' }),
    task({ id: 't2', title: 'Cave lighting', status: 'in_progress', priority: 2 }),
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
    let ruler: RulerService;
    let tasksApi: FakeTasks;
    let assetsApi: FakeAssets;
    const t = en.projects.tasks;

    const el = () => fixture.nativeElement as HTMLElement;
    const tags = () => [...el().querySelectorAll('app-task-tag')] as HTMLElement[];
    const panels = () => [...el().querySelectorAll('app-shelf-panel')] as HTMLElement[];
    const panelCount = (title: string) =>
        panels().find(p => p.getAttribute('aria-label') === title)
            ?.querySelector('.sp-count')?.textContent?.trim();

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
                { provide: SprintsService, useValue: { async list() { return []; } } },
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
        ruler = TestBed.inject(RulerService);
        fixture.detectChanges();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    // ADR-163 — opening a card is a navigation, so the card is an anchor and carries the query
    // itself. A click handler has no URL to hand the browser, which is middle click, copy link
    // address and the hover preview gone.
    it('hangs every card as a tag addressed at its own task', () => {
        const plates = tags().map(x => x.querySelector('.tt-plate') as HTMLElement);
        expect(plates.length).toBe(3);
        expect(plates.every(p => p.tagName === 'A')).toBe(true);
        expect(plates.map(p => p.getAttribute('href'))).toEqual([
            '/projects/p1/tasks?task=t1', '/projects/p1/tasks?task=t2', '/projects/p1/tasks?task=t3',
        ]);
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

    // The tag reddens a late date, and colour on its own carries nothing — so the word travels in
    // the string the tag is handed. Only t1 is late; t2 has no date at all.
    it('says overdue in words on the tag, not only in the colour', () => {
        const due = (id: string) =>
            tags().find(x => x.querySelector('.tt-plate')?.getAttribute('href')?.endsWith(id))
                ?.querySelector('.tt-due');
        expect(due('t1')?.textContent).toContain(t.overdue);
        expect(due('t1')?.classList.contains('tt-overdue')).toBe(true);
        expect(due('t2')).toBeNull();
    });

    // A column header counts the column, not the filter: the strip above says what the filter is
    // doing, and a header that moved with it would leave nothing saying how many there really are.
    it('counts a column against every task in it while the search hides two of them', () => {
        fixture.componentInstance.search.set('cave');
        fixture.detectChanges();

        expect(tags().length).toBe(1);
        expect(panelCount(t.status.backlog)).toBe('1');
        expect(panelCount(t.status.in_progress)).toBe('1');
        expect(panelCount(t.status.done)).toBe('1');
        expect(panelCount(t.status.planned)).toBe('0');
    });

    // ADR-159 clause 3 — the rail carries the project and the crumb, so the tally goes to the rule
    // and this screen draws no heading of its own.
    it('publishes its tally to the rule instead of drawing a title', () => {
        expect(el().querySelector('h1')).toBeNull();
        expect(ruler.label()).toBe('Cedar Quest');
        expect(ruler.left().map(r => r.text)).toEqual([t.sub(2, 3)]);
        expect(ruler.right().map(r => r.text)).toEqual([`1 ${t.overdue}`]);
    });

    it('clears the rule when the screen goes away', () => {
        fixture.destroy();
        expect(ruler.left()).toEqual([]);
        expect(ruler.label()).toBe('');
    });

    it('keeps the empty board on a shelf and offers New task only once', () => {
        fixture.componentInstance.tasks.set([]);
        fixture.detectChanges();

        const createButtons = [...el().querySelectorAll('app-button')]
            .filter(button => button.textContent?.trim() === t.newTask);
        expect(createButtons.length).toBe(1);
        expect(el().querySelector('app-shelf-panel.empty-panel')?.getAttribute('aria-label')).toBe(t.title);
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
