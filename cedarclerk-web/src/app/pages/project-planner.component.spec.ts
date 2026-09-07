import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectPlannerComponent } from './project-planner.component';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { Sprint, SprintsService } from '../core/sprints.service';
import { GameTask, TasksService } from '../core/tasks.service';
import { en } from '@localization/en';

const PROJECT = { id: 'p1', name: 'Cedar Quest' } as ProjectDetail;

const SPRINTS: Sprint[] = [
    {
        id: 's-now', projectId: 'p1', number: 4, name: 'Autumn build',
        startsAt: '2026-08-01T00:00:00', endsAt: '2026-08-31T00:00:00',
        state: 'current', taskCount: 2, doneCount: 1, overdueCount: 1,
    },
    {
        id: 's-next', projectId: 'p1', number: 5, name: 'Winter build',
        startsAt: '2026-09-01T00:00:00', endsAt: '2026-09-14T00:00:00',
        state: 'planned', taskCount: 0, doneCount: 0, overdueCount: 0,
    },
    {
        id: 's-later', projectId: 'p1', number: 6, name: 'Spring build',
        startsAt: '2026-09-15T00:00:00', endsAt: '2026-09-30T00:00:00',
        state: 'planned', taskCount: 0, doneCount: 0, overdueCount: 0,
    },
    {
        id: 's-old', projectId: 'p1', number: 3, name: 'Summer build',
        startsAt: '2026-07-01T00:00:00', endsAt: '2026-07-14T00:00:00',
        state: 'finished', taskCount: 1, doneCount: 1, overdueCount: 0,
    },
];

// Two planned and one finished, so the summary's state tallies are two different numbers: with
// one apiece a row reading the other state's count would print the same digit as its own.

function task(over: Partial<GameTask>): GameTask {
    return {
        id: 't', projectId: 'p1', title: 'A task', status: 'backlog', priority: 2,
        description: '', assignee: '', sprintId: null, buildId: null, dueAt: null,
        isPublicRoadmap: false, createdAt: '', updatedAt: '', completedAt: null,
        archivedAt: null, links: [], ...over,
    };
}

const TASKS: GameTask[] = [
    task({ id: 't-late', title: 'Fix saves on quit', sprintId: 's-now', status: 'in_progress', priority: 1, dueAt: '2026-01-01T00:00:00' }),
    task({ id: 't-done', title: 'Ship the demo', sprintId: 's-now', status: 'done' }),
    task({ id: 't-old', title: 'Old and finished', sprintId: 's-old', status: 'done' }),
    task({ id: 't-pile', title: 'Not planned yet', sprintId: null, status: 'backlog' }),
];

class FakeSprints {
    devlog = { documentId: 'doc-9', title: 'Devlog', doneCount: 1 };
    async list() { return structuredClone(SPRINTS); }
    async createDevlog() { return { ...this.devlog }; }
}

class FakeTasks {
    async list() { return structuredClone(TASKS); }
}

class FakeProjects {
    async get() { return structuredClone(PROJECT); }
}

describe('project planner', () => {
    let fixture: ComponentFixture<ProjectPlannerComponent>;
    const t = en.projects;

    const el = () => fixture.nativeElement as HTMLElement;
    const cards = () => [...el().querySelectorAll('app-paper-card.sprint')] as HTMLElement[];
    const cardNames = () => cards().map(c => c.querySelector('.sprint-name')?.textContent?.trim());
    const tags = () => [...el().querySelectorAll('app-task-tag')] as HTMLElement[];
    const side = () => el().querySelector('aside.side') as HTMLElement;
    const specValue = (label: string) =>
        [...side().querySelectorAll('app-spec-row')]
            .find(r => r.querySelector('.label')?.textContent?.trim() === label)
            ?.querySelector('.text')?.textContent?.trim();
    const meta = () => [...el().querySelectorAll('app-page-header .page-meta > span:not(.sep)')]
        .map(x => x.textContent?.trim());

    async function create() {
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: SprintsService, useValue: new FakeSprints() },
                { provide: TasksService, useValue: new FakeTasks() },
                { provide: ProjectsService, useValue: new FakeProjects() },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1' })) } },
            ],
        });
        fixture = TestBed.createComponent(ProjectPlannerComponent);
        fixture.detectChanges();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    // Current → planned → the pile → finished. The pile sits next to the future because that is
    // where work comes out of, and a finished sprint is the only one that collapses.
    it('stacks the stretches in the order the screen is read in', () => {
        expect(cardNames())
            .toEqual(['Autumn build', 'Winter build', 'Spring build', t.planner.noSprint, 'Summer build']);
    });

    it('uses the operational split and omits the no-sprint paper when there is no pile', () => {
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        expect(el().querySelectorAll('.grid.split-workspace > .split-pane')).toHaveLength(2);

        fixture.componentInstance.tasks.update(list => list.filter(task => task.sprintId));
        fixture.detectChanges();
        expect(cardNames()).not.toContain(t.planner.noSprint);
    });

    // ADR-168 rule 4 — the same tag the hub hangs, and it is a link (ADR-163).
    it('hangs each task as a tag addressed at the board', () => {
        const plates = tags().map(x => x.querySelector('.tt-plate') as HTMLAnchorElement);
        expect(plates.every(p => p.tagName === 'A')).toBe(true);
        expect(plates[0].getAttribute('href')).toBe('/projects/p1/tasks?task=t-late');
    });

    // The word, not only the ink: rust on the date says nothing to a reader who cannot see it.
    it('says overdue in words beside the reddened date', () => {
        const due = tags()[0].querySelector('.tt-due')!;
        expect(due.classList.contains('tt-overdue')).toBe(true);
        expect(due.textContent).toContain(t.tasks.overdue);
        expect(tags()[1].querySelector('.tt-due')?.textContent?.trim()).toBe(t.tasks.noDueDate);
    });

    it('carries the task status as the tag\'s stamp', () => {
        const stamps = tags().map(x => x.querySelector('.tt-stamp app-stamp-badge')?.textContent?.trim());
        // Three, not four: the finished sprint is collapsed, so its one done task hangs nowhere.
        expect(stamps).toEqual([
            t.tasks.status.in_progress, t.tasks.status.done, t.tasks.status.backlog,
        ]);
    });

    // Nothing is hidden by collapsing: a finished sprint with an unfinished task inside stays open.
    it('collapses a finished sprint only when everything in it is done', () => {
        expect(el().querySelectorAll('.collapsed-line').length).toBe(1);

        fixture.componentInstance.tasks.update(list =>
            list.map(x => (x.id === 't-old' ? { ...x, status: 'in_progress' as const } : x)));
        fixture.detectChanges();
        expect(el().querySelectorAll('.collapsed-line').length).toBe(0);
    });

    it('expands a collapsed sprint when its line is clicked', () => {
        (el().querySelector('.collapsed-line') as HTMLButtonElement).click();
        fixture.detectChanges();
        expect(el().querySelectorAll('.collapsed-line').length).toBe(0);
        expect(tags().some(x => x.textContent?.includes('Old and finished'))).toBe(true);
    });

    // ADR-168 rule 3 — `draft` is the only query parameter the editor reads; `id` silently opened
    // whichever draft happened to be newest.
    it('opens a new devlog at the address the editor reads', async () => {
        const router = TestBed.inject(Router);
        const nav = vi.spyOn(router, 'navigate').mockResolvedValue(true);
        await fixture.componentInstance.createDevlog(SPRINTS[0]);
        expect(nav).toHaveBeenCalledWith(['/editor'], { queryParams: { draft: 'doc-9' } });
    });

    it('counts the summary out of the sprints and tasks already loaded', () => {
        expect(specValue(t.planner.state.current)).toBe('S4 — Autumn build');
        expect(specValue(t.hub.sprintDone)).toBe(t.planner.progress(1, 2));
        expect(specValue(t.tasks.filterOverdue)).toBe(t.planner.overdueInside(1));
        expect(specValue(t.planner.state.planned)).toBe('2');
        expect(specValue(t.planner.state.finished)).toBe('1');
        expect(specValue(t.tasks.filterOpen)).toBe('2');   // t-late and t-pile
        expect(specValue(t.planner.noSprint)).toBe('1');   // t-pile
        const current = [...side().querySelectorAll('app-spec-row')]
            .find(row => row.querySelector('.label')?.textContent?.trim() === t.planner.state.current)!;
        expect(current.classList).toContain('wrap');
    });

    // ADR-239 clause 6 — what the rule used to say is the header's kicker and meta line.
    it('names the project and the tally in the header', () => {
        expect(el().querySelector('app-page-header .page-kicker')?.textContent?.trim()).toBe('Cedar Quest');
        expect(meta()).toEqual([t.planner.sub(4, 2)]);
    });

    it('names the next action when there is nothing planned at all', () => {
        fixture.componentInstance.sprints.set([]);
        fixture.componentInstance.tasks.set([]);
        fixture.detectChanges();
        const empty = el().querySelector('app-empty-state')!;
        expect(empty.textContent).toContain(t.planner.empty);
        expect(empty.querySelector('app-button')?.textContent?.trim()).toBe(t.planner.newSprint);
        expect(el().querySelector('app-page-header app-button')).toBeNull();
    });

    it('uses shared paper buttons for sprint modal actions', () => {
        fixture.componentInstance.startCreating();
        fixture.detectChanges();
        expect(el().querySelector('label[for="sprint-start"]')).not.toBeNull();
        expect(el().querySelector('#sprint-start')).not.toBeNull();
        expect(el().querySelector('label[for="sprint-end"]')).not.toBeNull();
        expect(el().querySelector('#sprint-end')).not.toBeNull();
        expect(el().querySelector('.modal-foot-row .btn.paper')).not.toBeNull();
        expect(el().querySelector('.modal-foot-row .btn.pine')).not.toBeNull();
        expect(el().querySelector('.modal-foot-row .btn-accent, .modal-foot-row .btn-ghost')).toBeNull();

        fixture.componentInstance.creating.set(false);
        fixture.componentInstance.startEditing(SPRINTS[0]);
        fixture.detectChanges();
        expect(el().querySelector('.modal-foot-row .btn.danger')).not.toBeNull();
    });
});
