import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectBoardsComponent } from './project-boards.component';
import { BoardsService, CanvasBoardSummary } from '../core/boards.service';
import { MembersService, ProjectMember } from '../core/members.service';
import { ProjectAccess, ProjectAccessService } from '../core/project-access.service';
import { ProjectsService } from '../core/projects.service';
import { en } from '../core/i18n/en';

const BOARD: CanvasBoardSummary = {
    id: 'b1', projectId: 'p1', name: 'References', background: 'grid', itemCount: 3,
    createdAt: '', updatedAt: '', version: 1, canWrite: true,
};

function person(over: Partial<ProjectMember>): ProjectMember {
    return {
        id: 'm', userId: 'u', email: 'someone@example.test', role: 'editor', pending: false,
        invitedAt: null, acceptedAt: null, lastSeenAt: null, isYou: false, ...over,
    };
}

class FakeBoards {
    boards: CanvasBoardSummary[] = [BOARD];
    async list() { return structuredClone(this.boards); }
}

class FakeMembers {
    people: ProjectMember[] = [];
    async list() { return structuredClone(this.people); }
    async shared() { return [{ id: 'p1', name: 'Cedar Quest (shared)', ownerName: 'Ann', role: 'viewer', boardCount: 1, lastActivityAt: '' }]; }
}

class FakeProjects {
    calls = 0;
    async get() { this.calls++; return { id: 'p1', name: 'Cedar Quest' }; }
}

class FakeAccess {
    answer: ProjectAccess | null = null;
    async resolve() { return this.answer; }
}

describe('project boards', () => {
    let fixture: ComponentFixture<ProjectBoardsComponent>;
    let boards: FakeBoards;
    let members: FakeMembers;
    let projects: FakeProjects;
    let access: FakeAccess;
    const t = en.projects.canvas;

    const el = () => fixture.nativeElement as HTMLElement;
    const kicker = () => el().querySelector('.page-kicker')?.textContent?.trim();
    const headerButtons = () => [...el().querySelectorAll('app-page-header app-button')].map(b => b.textContent?.trim());

    async function create(setup: () => void = () => { }) {
        boards = new FakeBoards();
        members = new FakeMembers();
        projects = new FakeProjects();
        access = new FakeAccess();
        setup();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: BoardsService, useValue: boards },
                { provide: MembersService, useValue: members },
                { provide: ProjectsService, useValue: projects },
                { provide: ProjectAccessService, useValue: access },
                { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: 'p1' })) } },
            ],
        });
        fixture = TestBed.createComponent(ProjectBoardsComponent);
        fixture.detectChanges();
        for (let i = 0; i < 8; i++) await Promise.resolve();
        fixture.detectChanges();
    }

    // T-303 — the owner's route is never tried by a member, so nothing 404s into the console.
    it('reads a member\'s project name off the shared list without touching the owner-only route', async () => {
        await create(() => {
            access.answer = { role: 'viewer', canWrite: false, archived: false };
            members.people = [person({ id: null, role: 'owner', email: 'ann@example.test' }), person({ isYou: true, role: 'viewer' })];
        });

        expect(projects.calls).toBe(0);
        expect(kicker()).toBe('Cedar Quest (shared)');
        expect(el().querySelector('app-page-header .tag')?.textContent?.trim()).toBe(t.roleViewer);
        expect(headerButtons()).toEqual([]);
    });

    it('reads the owner\'s project name off the project row', async () => {
        await create(() => {
            access.answer = { role: 'owner', canWrite: true, archived: false };
            members.people = [person({ id: null, role: 'owner', isYou: true })];
        });

        expect(projects.calls).toBe(1);
        expect(kicker()).toBe('Cedar Quest');
        expect(headerButtons()).toEqual([t.newBoard]);
    });

    // T-313 — one screen offers one action once: with no boards the empty state holds New board
    // and the header drops its own copy, the same rule project-builds.component carries.
    it('offers New board once on an empty list', async () => {
        await create(() => {
            access.answer = { role: 'owner', canWrite: true, archived: false };
            boards.boards = [];
        });

        expect(headerButtons()).toEqual([]);
        expect(el().querySelectorAll('app-empty-state app-button').length).toBe(1);
    });
});
