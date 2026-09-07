import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectBuildsComponent } from './project-builds.component';
import { ProjectDetail, ProjectsService } from '../core/projects.service';
import { Build, BuildsService } from '../core/builds.service';
import { GameTask, TasksService } from '../core/tasks.service';
import { en } from '@localization/en';

const PROJECT = { id: 'p1', name: 'Cedar Quest' } as ProjectDetail;

const BUILDS: Build[] = [
    {
        id: 'b-next', projectId: 'p1', version: '0.4.0', notes: 'Fog and lanterns.',
        releasedAt: null, createdAt: '', released: false, taskCount: 1, doneCount: 0, documents: [], isPublic: false, downloadUrl: null,
    },
    {
        id: 'b-out', projectId: 'p1', version: '0.3.1', notes: '',
        releasedAt: '2026-08-17T09:00:00', createdAt: '', released: true,
        taskCount: 1, doneCount: 1, documents: [{ id: 'doc-1', title: 'Changelog 0.3.1' }],
        isPublic: false, downloadUrl: null,
    },
    {
        id: 'b-older', projectId: 'p1', version: '0.3.0', notes: '',
        releasedAt: '2026-07-02T09:00:00', createdAt: '', released: true,
        taskCount: 2, doneCount: 0, documents: [{ id: 'doc-2', title: 'Notes 0.3.0' }, { id: 'doc-3', title: 'Credits' }],
        isPublic: false, downloadUrl: null,
    },
];

// Two released, one unreleased, three attached documents and four tasks carrying a version: the
// four numbers the summary prints are four different numbers, so no row can be reading a
// neighbour's count and still come out right.

function task(over: Partial<GameTask>): GameTask {
    return {
        id: 't', projectId: 'p1', title: 'A task', status: 'backlog', priority: 2,
        description: '', assignee: '', sprintId: null, buildId: null, dueAt: null,
        isPublicRoadmap: false, createdAt: '', updatedAt: '', completedAt: null,
        archivedAt: null, links: [], ...over,
    };
}

const TASKS: GameTask[] = [
    task({ id: 't-fog', title: 'Volumetric fog', buildId: 'b-next', status: 'in_progress', priority: 1, dueAt: '2026-01-01T00:00:00' }),
    task({ id: 't-save', title: 'Save on quit', buildId: 'b-out', status: 'done' }),
    task({ id: 't-fx', title: 'Water shader', buildId: 'b-older' }),
    task({ id: 't-ui', title: 'Pause menu', buildId: 'b-older', status: 'done' }),
    task({ id: 't-loose', title: 'Nothing to do with a version' }),
];

class FakeBuilds {
    changelog = { documentId: 'doc-new', title: 'Changelog', taskCount: 1 };
    async list() { return structuredClone(BUILDS); }
    async createChangelog() { return { ...this.changelog }; }
}

class FakeTasks {
    async list() { return structuredClone(TASKS); }
}

class FakeProjects {
    async get() { return structuredClone(PROJECT); }
}

describe('project builds', () => {
    let fixture: ComponentFixture<ProjectBuildsComponent>;
    const t = en.projects;

    const el = () => fixture.nativeElement as HTMLElement;
    const cards = () => [...el().querySelectorAll('app-paper-card.build')] as HTMLElement[];
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
                { provide: BuildsService, useValue: new FakeBuilds() },
                { provide: TasksService, useValue: new FakeTasks() },
                { provide: ProjectsService, useValue: new FakeProjects() },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1' })) } },
            ],
        });
        fixture = TestBed.createComponent(ProjectBuildsComponent);
        fixture.detectChanges();
        for (let i = 0; i < 5; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    beforeEach(create);

    it('draws a card per version, in the order the server sent them', () => {
        expect(cards().map(c => c.querySelector('.build-version')?.textContent?.trim()))
            .toEqual(['0.4.0', '0.3.1', '0.3.0']);
    });

    // ADR-163/ADR-168 rule 3 — the chip is a door, and `draft` is the address the editor reads.
    it('makes an attached document a link the editor can actually open', () => {
        const chips = [...el().querySelectorAll('a.doc-chip')] as HTMLAnchorElement[];
        expect(chips.map(c => c.getAttribute('href')))
            .toEqual(['/editor?draft=doc-1', '/editor?draft=doc-2', '/editor?draft=doc-3']);
        expect(el().querySelectorAll('button.doc-chip').length).toBe(0);
    });

    // ADR-168 rule 4 — one object, one drawing of it: the tag the planner and the hub hang.
    it('hangs a version\'s tasks as the same tags the planner does, and only its own', () => {
        const plates = tags().map(x => x.querySelector('.tt-plate') as HTMLAnchorElement);
        expect(plates.map(p => p.getAttribute('href'))).toEqual([
            '/projects/p1/tasks?task=t-fog', '/projects/p1/tasks?task=t-save',
            '/projects/p1/tasks?task=t-fx', '/projects/p1/tasks?task=t-ui',
        ]);   // t-loose belongs to no version, so it hangs on no card
        expect(tags()[0].querySelector('.tt-prio')?.textContent?.trim()).toBe('P1');
        expect(tags()[0].querySelector('.tt-due')?.classList.contains('tt-overdue')).toBe(true);
        expect(tags()[0].querySelector('.tt-stamp app-stamp-badge')?.textContent?.trim())
            .toBe(t.tasks.status.in_progress);
    });

    it('says "not out yet" about a planned version and stamps the released one', () => {
        const stamps = cards().map(c => c.querySelector('.build-head app-stamp-badge')?.textContent?.trim());
        expect(stamps).toEqual([t.builds.unreleased, t.builds.released, t.builds.released]);
    });

    // An unreleased record is a plan, not a version: 0.4.0 is newer and must not be "latest".
    it('takes the latest from what is out, never from what is planned', () => {
        expect(specValue(t.builds.latest)).toBe('0.3.1');
        expect(specValue(t.builds.released)).toBe('2');
        expect(specValue(t.builds.unreleased)).toBe('1');
    });

    it('counts the summary out of the versions and tasks already loaded', () => {
        expect(specValue(t.builds.assignBuild)).toBe('4'); // t-loose belongs to no version
        expect(specValue(t.colDocs)).toBe('3');
    });

    // ADR-168 rule 3 — `draft` is the only query parameter the editor reads.
    it('opens a new changelog at the address the editor reads', async () => {
        const router = TestBed.inject(Router);
        const nav = vi.spyOn(router, 'navigate').mockResolvedValue(true);
        await fixture.componentInstance.createChangelog(BUILDS[1]);
        expect(nav).toHaveBeenCalledWith(['/editor'], { queryParams: { draft: 'doc-new' } });
    });

    // ADR-239 clause 6 — what the rule used to say is the header's kicker and meta line.
    it('names the project and the tally in the header', () => {
        expect(el().querySelector('app-page-header .page-kicker')?.textContent?.trim()).toBe('Cedar Quest');
        expect(meta()).toEqual([t.builds.sub(3, 1)]);
    });

    it('offers New version once: in the header when there are versions, in the empty state when not', () => {
        const buttons = () => [...el().querySelectorAll('app-button')]
            .filter(b => b.textContent?.trim() === t.builds.newBuild);
        expect(buttons().length).toBe(1);
        expect(el().querySelector('app-page-header app-button')).toBeTruthy();

        fixture.componentInstance.builds.set([]);
        fixture.detectChanges();
        expect(buttons().length).toBe(1);
        expect(el().querySelector('app-empty-state app-button')).toBeTruthy();
        expect(el().querySelector('app-empty-state')?.textContent).toContain(t.builds.emptyHint);
    });
});
