import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { avatarFill, avatarInitial } from '../core/avatar-color.util';
import { INVITABLE_ROLES, InvitableRole, MembersService, ProjectMember } from '../core/members.service';
import { IconComponent } from './icon.component';
import { ModalComponent } from './modal.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';

/**
 * The people on a project, and the owner's controls over them (ADR-217). One component so a second
 * screen that wants the list gets this one rather than a copy — the second home ADR-095 paid for
 * once. It loads its own list and publishes it, because the screen around it may read a caller's
 * role off the same rows.
 */
@Component({
    selector: 'app-project-members-panel',
    imports: [FormsModule, IconComponent, ModalComponent, ButtonComponent, InputComponent],
    templateUrl: 'project-members-panel.component.html',
    styleUrls: ['project-members-panel.component.css'],
})
export class ProjectMembersPanelComponent {
    private members = inject(MembersService);
    t = inject(LocaleService).t;

    readonly projectId = input.required<string>();
    readonly peopleChange = output<readonly ProjectMember[]>();

    readonly roles = INVITABLE_ROLES;
    readonly fill = avatarFill;
    readonly initial = avatarInitial;

    people = signal<readonly ProjectMember[]>([]);
    peopleError = signal<string | null>(null);
    busy = signal(false);

    inviteEmail = signal('');
    inviteRole = signal<InvitableRole>('editor');
    inviteLink = signal('');
    inviteCopied = signal(false);
    confirmRemove = signal<ProjectMember | null>(null);

    me = computed(() => this.people().find(m => m.isYou) ?? null);
    isOwner = computed(() => this.me()?.role === 'owner');

    constructor() {
        effect(() => {
            const id = this.projectId();
            untracked(() => void this.load(id));
        });
    }

    async load(id = this.projectId()) {
        if (!id) return;
        this.peopleError.set(null);
        try {
            this.publish(await this.members.list(id));
        } catch (e) {
            this.peopleError.set(httpErrorMessage(e, this.t().projects.canvas.peopleFailed));
        }
    }

    // Clears the error it will render: the last failure of some other action, shown inside a
    // dialog asking about this one, reads as a refusal of the button not yet pressed.
    openRemove(member: ProjectMember) {
        this.peopleError.set(null);
        this.confirmRemove.set(member);
    }

    async invite() {
        const email = this.inviteEmail().trim().toLowerCase();
        if (!email || this.busy()) return;
        this.busy.set(true);
        this.peopleError.set(null);
        this.inviteCopied.set(false);
        try {
            const answer = await this.members.invite(this.projectId(), email, this.inviteRole());
            this.publish([...this.people(), answer.member]);
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
            this.publish(this.people().map(m => (m.id === answer.member.id ? answer.member : m)));
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
            this.publish(this.people().map(m => (m.id === next.id ? next : m)));
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
            this.publish(this.people().filter(m => m.id !== member.id));
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

    private publish(people: readonly ProjectMember[]) {
        this.people.set(people);
        this.peopleChange.emit(people);
    }
}
