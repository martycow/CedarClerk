import { Component, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { INVITABLE_ROLES, InvitableRole } from '../core/members.service';
import { JoinedTeam, TEAM_MEMBER_STATUSES, Team, TeamMember, TeamMemberStatus, TeamsService } from '../core/teams.service';
import { IconComponent } from '../shared/icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { HintDotComponent } from '../shared/hint-dot.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';

type StateTone = 'ok' | 'muted' | 'warn' | 'danger';

// T-358 — teams. The list of teams on a shelf, the selected team's people on the sheet, and an
// inspector shelf that says what the selection amounts to: the same three-part shape the Posts
// Manager and the Glossary already use, because this is the same kind of screen — a list you pick
// from and a thing you then edit.
//
// Two things a project's member list does not have, and the reason a team is its own screen rather
// than a longer member list: a status, and a reach. Restricting somebody holds them read-only on
// every project the team touches at once, which is the point of grouping people at all.
@Component({
    selector: 'app-teams',
    imports: [
        FormsModule, NgTemplateOutlet, IconComponent, ButtonComponent, InputComponent, SpecRowComponent, HintDotComponent,
        PageHeaderComponent, EmptyStateComponent,
    ],
    templateUrl: 'teams.component.html',
    styleUrls: ['teams.component.css'],
})
export class TeamsComponent {
    private api = inject(TeamsService);
    t = inject(LocaleService).t;

    readonly roles = INVITABLE_ROLES;
    readonly statuses = TEAM_MEMBER_STATUSES;

    teams = signal<Team[]>([]);
    joined = signal<JoinedTeam[]>([]);
    members = signal<TeamMember[]>([]);
    selectedId = signal<string | null>(null);

    loading = signal(true);
    membersLoading = signal(false);
    busy = signal(false);
    error = signal<string | null>(null);

    creating = signal(false);
    newName = signal('');

    inviteEmail = signal('');
    inviteRole = signal<InvitableRole>('editor');
    /** The last invitation's link, kept on screen because mail may not be configured at all. */
    lastInviteUrl = signal<string | null>(null);

    renaming = signal(false);
    renameName = signal('');

    selected = computed(() => this.teams().find(t => t.id === this.selectedId()) ?? null);
    selectedJoined = computed(() => this.joined().find(t => t.id === this.selectedId()) ?? null);

    headerMeta = computed<HeaderMeta[]>(() => [{ text: this.t().teams.count(this.teams().length) }]);

    constructor() {
        void this.load();
    }

    async load() {
        this.loading.set(true);
        this.error.set(null);
        try {
            const [own, joined] = await Promise.all([this.api.list(), this.api.joined()]);
            this.teams.set(own);
            this.joined.set(joined);
            // Keep either kind of selection: a joined-only account still has a real subject to
            // inspect even though it owns no editable member list.
            const keep = own.find(t => t.id === this.selectedId())
                ?? joined.find(t => t.id === this.selectedId())
                ?? own[0]
                ?? joined[0]
                ?? null;
            this.selectedId.set(keep?.id ?? null);
            if (keep && own.some(team => team.id === keep.id)) await this.loadMembers(keep.id);
            else this.members.set([]);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.loadFailed));
        } finally {
            this.loading.set(false);
        }
    }

    async select(team: Team) {
        if (team.id === this.selectedId()) return;
        this.selectedId.set(team.id);
        this.lastInviteUrl.set(null);
        this.renaming.set(false);
        await this.loadMembers(team.id);
    }

    selectJoined(team: JoinedTeam) {
        this.selectedId.set(team.id);
        this.members.set([]);
        this.lastInviteUrl.set(null);
        this.creating.set(false);
        this.renaming.set(false);
    }

    private async loadMembers(teamId: string) {
        this.membersLoading.set(true);
        try { this.members.set(await this.api.members(teamId)); }
        catch (e) { this.error.set(httpErrorMessage(e, this.t().teams.loadFailed)); }
        finally { this.membersLoading.set(false); }
    }

    startCreate() {
        this.newName.set('');
        this.renameName.set('');
        this.renaming.set(false);
        this.creating.set(true);
    }

    async create() {
        const name = this.newName().trim();
        if (!name || this.busy()) return;
        this.busy.set(true);
        this.error.set(null);
        try {
            const created = await this.api.create(name);
            this.creating.set(false);
            this.selectedId.set(created.id);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    startRename() {
        const team = this.selected();
        if (!team) return;
        this.renameName.set(team.name);
        this.renaming.set(true);
    }

    async rename() {
        const team = this.selected();
        const name = this.renameName().trim();
        if (!team || !name || this.busy()) return;
        this.busy.set(true);
        try {
            await this.api.rename(team.id, name);
            this.renaming.set(false);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    // Deleting a team never deletes a project — it takes the team away from them, which is what
    // the confirm says out loud so nobody has to guess which of the two this is.
    async remove() {
        const team = this.selected();
        if (!team || this.busy()) return;
        if (!confirm(this.t().teams.deleteConfirm(team.name, team.projectCount))) return;
        this.busy.set(true);
        try {
            await this.api.remove(team.id);
            this.selectedId.set(null);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    canInvite = computed(() => {
        const email = this.inviteEmail().trim();
        return email.length > 2 && email.includes('@');
    });

    async invite() {
        const team = this.selected();
        if (!team || !this.canInvite() || this.busy()) return;
        this.busy.set(true);
        this.error.set(null);
        try {
            const done = await this.api.invite(team.id, this.inviteEmail().trim(), this.inviteRole());
            this.inviteEmail.set('');
            this.lastInviteUrl.set(done.inviteUrl);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.inviteFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async resend(member: TeamMember) {
        const team = this.selected();
        if (!team || this.busy()) return;
        this.busy.set(true);
        try {
            const done = await this.api.resend(team.id, member.id);
            this.lastInviteUrl.set(done.inviteUrl);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.inviteFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async setRole(member: TeamMember, role: string) {
        const team = this.selected();
        if (!team || this.busy()) return;
        this.busy.set(true);
        try {
            await this.api.setRole(team.id, member.id, role as InvitableRole);
            await this.loadMembers(team.id);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    // A ban is asked about; restricting and restoring are not. The difference is that a ban also
    // kills a live invitation, so the same address cannot be invited back by anyone.
    async setStatus(member: TeamMember, status: string) {
        const team = this.selected();
        if (!team || this.busy()) return;
        if (status === 'banned' && !confirm(this.t().teams.banConfirm(member.email))) return;
        this.busy.set(true);
        try {
            await this.api.setStatus(team.id, member.id, status as TeamMemberStatus);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async removeMember(member: TeamMember) {
        const team = this.selected();
        if (!team || this.busy()) return;
        if (!confirm(this.t().teams.removeConfirm(member.email))) return;
        this.busy.set(true);
        try {
            await this.api.removeMember(team.id, member.id);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().teams.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    roleWord(role: string): string {
        const c = this.t().projects.canvas;
        return role === 'owner' ? c.roleOwner : role === 'viewer' ? c.roleViewer : c.roleEditor;
    }

    statusWord(status: string): string {
        return (this.t().teams.statuses as Record<string, string>)[status] ?? status;
    }

    /** Pending reads as pending whatever the status says: nobody is restricted before they arrive. */
    stateWord(member: TeamMember): string {
        return member.pending ? this.t().teams.pending : this.statusWord(member.status);
    }

    stateTone(member: TeamMember): StateTone {
        if (member.pending) return 'muted';
        return member.status === 'active' ? 'ok' : member.status === 'restricted' ? 'warn' : 'danger';
    }

    async copyInvite() {
        const url = this.lastInviteUrl();
        if (!url) return;
        try { await navigator.clipboard.writeText(url); } catch { /* the URL is on screen either way */ }
    }
}
