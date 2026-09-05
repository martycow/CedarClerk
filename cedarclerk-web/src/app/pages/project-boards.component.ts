import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { BoardsService, CANVAS_BACKGROUNDS, CanvasBackground, CanvasBoardSummary } from '../core/boards.service';
import { MembersService, ProjectMember } from '../core/members.service';
import { ProjectAccess, ProjectAccessService } from '../core/project-access.service';
import { ProjectsService } from '../core/projects.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { ProjectMembersPanelComponent } from '../shared/project-members-panel.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';

// T-301 — the project's boards, and the people who share them. The People card is here rather
// than in the project-settings modal because this is the only screen membership actually widens:
// a collaborator can reach the canvas and nothing else, so the list of collaborators belongs
// beside the thing they are collaborating on.
@Component({
    selector: 'app-project-boards',
    imports: [
        FormsModule, RouterLink, IconComponent, ModalComponent, ProjectMembersPanelComponent,
        PageHeaderComponent, EmptyStateComponent, ButtonComponent, InputComponent,
    ],
    templateUrl: 'project-boards.component.html',
    styleUrls: ['project-boards.component.css'],
})
export class ProjectBoardsComponent {
    private api = inject(BoardsService);
    private members = inject(MembersService);
    private projects = inject(ProjectsService);
    private accessApi = inject(ProjectAccessService);
    private route = inject(ActivatedRoute);
    t = inject(LocaleService).t;

    readonly backgrounds = CANVAS_BACKGROUNDS;

    projectId = signal('');
    projectName = signal('');
    /** What this account is to the project, asked before any owner-only route is tried. */
    access = signal<ProjectAccess | null>(null);
    boards = signal<readonly CanvasBoardSummary[]>([]);
    /** The People panel's list, as it publishes it. */
    people = signal<readonly ProjectMember[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);
    actionError = signal<string | null>(null);
    busy = signal(false);

    editing = signal<CanvasBoardSummary | null>(null);
    creating = signal(false);
    formName = signal('');
    formBackground = signal<CanvasBackground>('grid');
    confirmDelete = signal<CanvasBoardSummary | null>(null);

    me = computed(() => this.people().find(m => m.isYou) ?? null);

    /**
     * Who may write. The access answer is the authority; the members list stands in for it, and a
     * board's `canWrite` only exists once a board does. With none of the three, the screen offers
     * the action and lets the server refuse it: hiding New board from an owner whose first board
     * does not exist yet is the worse of the two failures.
     */
    canWrite = computed(() => {
        const access = this.access();
        if (access) return access.canWrite;
        const role = this.me()?.role;
        if (role) return role === 'owner' || role === 'editor';
        const first = this.boards()[0];
        return first ? first.canWrite : true;
    });

    itemTotal = computed(() => this.boards().reduce((sum, b) => sum + b.itemCount, 0));

    /** The former rule readout: boards and items. */
    headerMeta = computed<HeaderMeta[]>(() =>
        [{ text: this.t().projects.canvas.sub(this.boards().length, this.itemTotal()) }]);

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        const access = await this.accessApi.resolve(id);
        this.access.set(access);
        try {
            this.boards.set(await this.api.list(id));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.canvas.loadFailed));
        } finally {
            this.loading.set(false);
        }
        // `GET /api/projects/{id}` is owner-only and stays that way (ADR-217), so a member's copy
        // of the name comes from the shared list — chosen by the access answer rather than by
        // trying the owner's route first and logging its 404.
        try {
            this.projectName.set(access?.role === 'owner'
                ? (await this.projects.get(id)).name
                : (await this.members.shared()).find(p => p.id === id)?.name ?? '');
        } catch {
            this.projectName.set('');
        }
    }

    openCreate() {
        this.editing.set(null);
        this.formName.set('');
        this.formBackground.set('grid');
        this.actionError.set(null);
        this.creating.set(true);
    }

    openRename(board: CanvasBoardSummary) {
        this.editing.set(board);
        this.formName.set(board.name);
        this.formBackground.set(board.background);
        this.actionError.set(null);
        this.creating.set(true);
    }

    closeForm() {
        this.creating.set(false);
        this.editing.set(null);
    }

    async submitForm() {
        const name = this.formName().trim();
        if (!name) {
            this.actionError.set(this.t().projects.canvas.nameRequired);
            return;
        }
        if (this.busy()) return;
        this.busy.set(true);
        this.actionError.set(null);
        try {
            const board = this.editing();
            if (board) {
                const next = await this.api.update(board.id, { name, background: this.formBackground() });
                this.boards.set(this.boards().map(b => (b.id === next.id ? next : b)));
            } else {
                const next = await this.api.create(this.projectId(), { name, background: this.formBackground() });
                this.boards.set([next, ...this.boards()]);
            }
            this.closeForm();
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    // Clears the error it will render: the last failure of some other action, shown inside a
    // dialog asking about this one, reads as a refusal of the button not yet pressed.
    openDelete(board: CanvasBoardSummary) {
        this.actionError.set(null);
        this.confirmDelete.set(board);
    }

    async removeBoard() {
        const board = this.confirmDelete();
        if (!board || this.busy()) return;
        this.busy.set(true);
        try {
            await this.api.remove(board.id);
            this.boards.set(this.boards().filter(b => b.id !== board.id));
            this.confirmDelete.set(null);
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    backgroundWord(value: CanvasBackground): string {
        return this.t().projects.canvas.background[value];
    }
}
