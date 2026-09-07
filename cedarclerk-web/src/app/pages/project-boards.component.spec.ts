import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ProjectBoardsComponent } from './project-boards.component';
import { BoardsService, CanvasBoardSummary } from '../core/boards.service';
import { MembersService, ProjectMember } from '../core/members.service';
import { ProjectAccess, ProjectAccessService } from '../core/project-access.service';
import { ProjectsService } from '../core/projects.service';
import { en } from '@localization/en';

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
    listOutcome: 'ok' | 'never' | 'fail' = 'ok';
    lists = 0;
    created: { name: string; background?: string }[] = [];
    removed: string[] = [];
    async list() {
        this.lists++;
        if (this.listOutcome === 'never') return new Promise<CanvasBoardSummary[]>(() => { });
        if (this.listOutcome === 'fail') throw new Error('boom');
        return structuredClone(this.boards);
    }
    async create(_projectId: string, input: { name: string; background?: string }) {
        this.created.push(input);
        return { ...BOARD, id: `b${this.created.length + 1}`, name: input.name, itemCount: 0 };
    }
    async update(id: string, input: { name?: string }) {
        return { ...this.boards.find(b => b.id === id)!, ...input };
    }
    async remove(id: string) { this.removed.push(id); }
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
    const text = (selector: string) => el().querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();
    const kicker = () => text('.page-kicker');
    const headerButtons = () => [...el().querySelectorAll('app-page-header app-button')].map(b => b.textContent?.trim());
    const cards = () => [...el().querySelectorAll('.board-card')] as HTMLElement[];
    const hints = () => [...el().querySelectorAll('p.hint')].map(p => p.textContent?.trim());
    const owner = () => { access.answer = { role: 'owner', canWrite: true, archived: false }; };

    async function settle() {
        for (let i = 0; i < 8; i++) await Promise.resolve();
        fixture.detectChanges();
    }

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
        await settle();
    }

    describe('loading', () => {
        it('says so and draws no list until the boards arrive', async () => {
            await create(() => { owner(); boards.listOutcome = 'never'; });

            expect(hints()).toContain(en.projects.loading);
            expect(cards()).toEqual([]);
            expect(el().querySelector('app-empty-state')).toBeNull();
        });
    });

    describe('loaded', () => {
        it('draws a card per board that links to its canvas, with the count and the background', async () => {
            await create(() => {
                owner();
                boards.boards = [BOARD, { ...BOARD, id: 'b2', name: 'Palette', background: 'blank', itemCount: 1 }];
            });

            expect(cards().length).toBe(2);
            expect(text('.board-card:nth-child(2) .board-name')).toBe('Palette');
            expect(text('.board-card:nth-child(2) .board-meta')).toBe(`${t.itemCount(1)} · ${t.background.blank}`);
            expect(cards()[1].querySelector('a.board')?.getAttribute('href')).toBe('/projects/p1/canvas/b2');
            expect(text('.page-meta')).toBe(t.sub(2, 4));
        });
    });

    describe('error', () => {
        it('shows the failure with Retry, and Retry asks again', async () => {
            await create(() => { owner(); boards.listOutcome = 'fail'; });

            expect(text('.error-line')).toBe(t.loadFailed);
            expect(cards()).toEqual([]);

            boards.listOutcome = 'ok';
            el().querySelector<HTMLButtonElement>('.main app-button button')?.click();
            await settle();
            expect(boards.lists).toBe(2);
            expect(el().querySelector('.error-line')).toBeNull();
            expect(cards().length).toBe(1);
        });
    });

    describe('read-only member', () => {
        it('is told so and gets cards without their write actions', async () => {
            await create(() => {
                access.answer = { role: 'viewer', canWrite: false, archived: false };
                members.people = [person({ isYou: true, role: 'viewer' })];
            });

            expect(hints()).toContain(t.readOnly);
            expect(cards().length).toBe(1);
            expect(el().querySelector('.board-actions')).toBeNull();
            expect(headerButtons()).toEqual([]);
        });

        it('falls back to the members list when access has no answer', async () => {
            await create(() => { members.people = [person({ isYou: true, role: 'editor' })]; });
            expect(fixture.componentInstance.canWrite()).toBe(true);
        });

        it('falls back to the first board when neither access nor the list answers', async () => {
            await create(() => { boards.boards = [{ ...BOARD, canWrite: false }]; });
            expect(fixture.componentInstance.canWrite()).toBe(false);
            expect(hints()).toContain(t.readOnly);
        });
    });

    describe('owner', () => {
        it('gets rename and delete on every card', async () => {
            await create(owner);

            const titles = [...el().querySelectorAll('.board-actions button')].map(b => b.getAttribute('title'));
            expect(titles).toEqual([t.renameBoard, t.deleteBoard]);
            expect(hints()).not.toContain(t.readOnly);
        });

        it('refuses a nameless board and prepends a named one', async () => {
            await create(owner);
            const page = fixture.componentInstance;

            page.openCreate();
            await page.submitForm();
            expect(page.actionError()).toBe(t.nameRequired);
            expect(boards.created).toEqual([]);

            page.formName.set('  Palette ');
            page.formBackground.set('blank');
            await page.submitForm();
            fixture.detectChanges();
            expect(boards.created).toEqual([{ name: 'Palette', background: 'blank' }]);
            expect(page.boards().map(b => b.name)).toEqual(['Palette', 'References']);
            expect(page.creating()).toBe(false);
        });

        it('deletes only after the confirmation, and drops the card', async () => {
            await create(owner);
            const page = fixture.componentInstance;

            page.openDelete(BOARD);
            expect(boards.removed).toEqual([]);
            await page.removeBoard();
            fixture.detectChanges();
            expect(boards.removed).toEqual(['b1']);
            expect(cards()).toEqual([]);
            expect(page.confirmDelete()).toBeNull();
        });
    });

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
