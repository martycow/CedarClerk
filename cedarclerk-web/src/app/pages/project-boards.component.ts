import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { avatarFill, avatarInitial } from '../core/avatar-color.util';
import { BoardsService, CANVAS_BACKGROUNDS, CanvasBackground, CanvasBoardSummary } from '../core/boards.service';
import { INVITABLE_ROLES, InvitableRole, MembersService, ProjectMember } from '../core/members.service';
import { ProjectsService } from '../core/projects.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
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
        FormsModule, RouterLink, IconComponent, ModalComponent, PageHeaderComponent, EmptyStateComponent,
        ButtonComponent, InputComponent,
    ],
    templateUrl: 'project-boards.component.html',
    styleUrls: ['project-boards.component.css'],
})
export class ProjectBoardsComponent {
    private api = inject(BoardsService);
    private members = inject(MembersService);
    private projects = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    t = inject(LocaleService).t;

    readonly backgrounds = CANVAS_BACKGROUNDS;
    readonly roles = INVITABLE_ROLES;
    readonly fill = avatarFill;
    readonly initial = avatarInitial;

    projectId = signal('');
    projectName = signal('');
    boards = signal<readonly CanvasBoardSummary[]>([]);
    people = signal<readonly ProjectMember[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);
    actionError = signal<string | null>(null);
    peopleError = signal<string | null>(null);
    busy = signal(false);

    editing = signal<CanvasBoardSummary | null>(null);
    creating = signal(false);
    formName = signal('');
    formBackground = signal<CanvasBackground>('grid');
    confirmDelete = signal<CanvasBoardSummary | null>(null);

    inviteEmail = signal('');
    inviteRole = signal<InvitableRole>('editor');
    inviteLink = signal('');
    inviteCopied = signal(false);
    confirmRemove = signal<ProjectMember | null>(null);

    me = computed(() => this.people().find(m => m.isYou) ?? null);
    isOwner = computed(() => this.me()?.role === 'owner');

    /**
     * Who may write. The members list is the authority when it answered — a role is a fact about
     * the account, while a board's `canWrite` only exists once a board does. With neither, the
     * screen offers the action and lets the server refuse it: hiding New board from an owner whose
     * first board does not exist yet is the worse of the two failures.
     */
    canWrite = computed(() => {
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
            void this.loadPeople();
        });
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        try {
            this.boards.set(await this.api.list(id));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.canvas.loadFailed));
        } finally {
            this.loading.set(false);
        }
        // A member cannot read the project row at all — `GET /api/projects/{id}` is owner-only and
        // stays that way — so the shared list is where their copy of the name comes from. Without
        // this fallback the header's kicker is simply blank for everyone but the owner.
        try {
            this.projectName.set((await this.projects.get(id)).name);
        } catch {
            const shared = await this.members.shared().catch(() => []);
            this.projectName.set(shared.find(p => p.id === id)?.name ?? '');
        }
    }

    async loadPeople() {
        const id = this.projectId();
        if (!id) return;
        this.peopleError.set(null);
        try {
            this.people.set(await this.members.list(id));
        } catch (e) {
            this.peopleError.set(httpErrorMessage(e, this.t().projects.canvas.peopleFailed));
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

    // Both confirms clear the error they will render: the last failure of some other action, shown
    // inside a dialog asking about this one, reads as a refusal of the button not yet pressed.
    openDelete(board: CanvasBoardSummary) {
        this.actionError.set(null);
        this.confirmDelete.set(board);
    }

    openRemove(member: ProjectMember) {
        this.peopleError.set(null);
        this.confirmRemove.set(member);
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

    async invite() {
        const email = this.inviteEmail().trim().toLowerCase();
        if (!email || this.busy()) return;
        this.busy.set(true);
        this.peopleError.set(null);
        this.inviteCopied.set(false);
        try {
            const answer = await this.members.invite(this.projectId(), email, this.inviteRole());
            this.people.set([...this.people(), answer.member]);
            this.inviteLink.set(answer.inviteUrl);
            this.inviteEmail.set('');
        } catch (e) {
            this.peopleError.set(httpErrorMessage(e, this.t().projects.canvas.inviteFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async resend(member: ProjectMember) {
        if (!member.id || this.busy()) return;
        this.busy.set(true);
        this.peopleError.set(null);
        this.inviteCopied.set(false);
        try {
            const answer = await this.members.resend(this.projectId(), member.id);
            this.people.set(this.people().map(m => (m.id === answer.member.id ? answer.member : m)));
            this.inviteLink.set(answer.inviteUrl);
        } catch (e) {
            this.peopleError.set(httpErrorMessage(e, this.t().projects.canvas.inviteFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async setRole(member: ProjectMember, role: string) {
        if (!member.id || this.busy() || (role !== 'editor' && role !== 'viewer')) return;
        this.busy.set(true);
        try {
            const next = await this.members.setRole(this.projectId(), member.id, role);
            this.people.set(this.people().map(m => (m.id === next.id ? next : m)));
        } catch (e) {
            this.peopleError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async removeMember() {
        const member = this.confirmRemove();
        if (!member?.id || this.busy()) return;
        this.busy.set(true);
        try {
            await this.members.remove(this.projectId(), member.id);
            this.people.set(this.people().filter(m => m.id !== member.id));
            this.confirmRemove.set(null);
        } catch (e) {
            this.peopleError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async copyInvite() {
        const link = this.inviteLink();
        if (!link) return;
        try {
            await navigator.clipboard.writeText(link);
            this.inviteCopied.set(true);
        } catch {
            // No clipboard permission — the address is on screen and selectable, which is the
            // fallback the copy button was a shortcut for.
        }
    }

    roleWord(role: string): string {
        const t = this.t().projects.canvas;
        return role === 'owner' ? t.roleOwner : role === 'viewer' ? t.roleViewer : t.roleEditor;
    }

    backgroundWord(value: CanvasBackground): string {
        return this.t().projects.canvas.background[value];
    }
}
