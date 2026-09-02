import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CanvasRole } from '../core/boards.service';
import { MembersService } from '../core/members.service';
import { ProjectAccessService } from '../core/project-access.service';
import { TeamsService } from '../core/teams.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { PageHeaderComponent } from '../shell/page-header.component';

interface InvitePeek {
    /** The project's name, or the team's — the sentence differs, the shape does not. */
    subject: string;
    invitedBy: string;
    role: CanvasRole;
    alreadyMember: boolean;
}

// T-301 / ADR-217 — the one screen behind authGuard rather than indieDevGuard: an invitation has to
// survive an install with the module switched off, and land the reader somewhere honest either way.
//
// T-358 — and it reads both kinds. A team invitation and a project invitation are the same page
// with a different noun and a different endpoint; the route says which through its `invite` data,
// and a second component would have been a copy of this one.
@Component({
    selector: 'app-invite-accept',
    imports: [PageHeaderComponent, ButtonComponent],
    templateUrl: 'invite-accept.component.html',
    styleUrls: ['invite-accept.component.css'],
})
export class InviteAcceptComponent {
    private members = inject(MembersService);
    private teams = inject(TeamsService);
    private access = inject(ProjectAccessService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    t = inject(LocaleService).t;

    peek = signal<InvitePeek | null>(null);
    loading = signal(true);
    failed = signal<string | null>(null);
    busy = signal(false);

    readonly isTeam = this.route.snapshot.data['invite'] === 'team';

    private token = '';

    constructor() {
        this.token = this.route.snapshot.paramMap.get('token') ?? '';
        void this.load();
    }

    title(): string {
        const c = this.t().projects.canvas;
        return this.isTeam ? this.t().teams.acceptTitle : c.acceptTitle;
    }

    body(invite: InvitePeek): string {
        return this.isTeam
            ? this.t().teams.acceptBody(invite.subject, invite.invitedBy)
            : this.t().projects.canvas.acceptBody(invite.subject, invite.invitedBy);
    }

    roleWord(role: CanvasRole) {
        const c = this.t().projects.canvas;
        return role === 'owner' ? c.roleOwner : role === 'viewer' ? c.roleViewer : c.roleEditor;
    }

    private async load() {
        this.loading.set(true);
        try {
            if (this.isTeam) {
                const peek = await this.teams.peekInvite(this.token);
                this.peek.set({ subject: peek.teamName, invitedBy: peek.invitedBy, role: peek.role, alreadyMember: peek.alreadyMember });
            } else {
                const peek = await this.members.peekInvite(this.token);
                this.peek.set({ subject: peek.projectName, invitedBy: peek.invitedBy, role: peek.role, alreadyMember: peek.alreadyMember });
            }
            this.failed.set(null);
        } catch {
            // A spent or unknown token is a 404 and reads the same to the person holding it, so the
            // page says the one true thing rather than repeating the server's wording.
            this.failed.set(this.t().projects.canvas.acceptFailed);
        } finally {
            this.loading.set(false);
        }
    }

    async accept() {
        this.busy.set(true);
        try {
            if (this.isTeam) {
                // A team grants projects, not one project, so there is nowhere single to land: the
                // hub is where those projects are listed.
                await this.teams.acceptInvite(this.token);
                await this.router.navigate(['/projects']);
            } else {
                const done = await this.members.accept(this.token);
                // The cached "you are nothing to this project" is now a lie — the shell reads it to
                // decide which tools to hang on the wall.
                this.access.forget(done.projectId);
                await this.router.navigate(['/projects', done.projectId, 'canvas']);
            }
        } catch (e) {
            this.failed.set(httpErrorMessage(e, this.t().projects.canvas.acceptFailed));
        } finally {
            this.busy.set(false);
        }
    }
}
