import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CanvasRole } from '../core/boards.service';
import { MembersService } from '../core/members.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';

interface InvitePeek {
    projectName: string;
    invitedBy: string;
    role: CanvasRole;
    alreadyMember: boolean;
}

// T-301 / ADR-217 — the one screen behind authGuard rather than indieDevGuard: an invitation has to
// survive an install with the module switched off, and land the reader somewhere honest either way.
@Component({
    selector: 'app-invite-accept',
    imports: [WorktopComponent, PaperCardComponent, ButtonComponent],
    templateUrl: 'invite-accept.component.html',
    styleUrls: ['invite-accept.component.css'],
})
export class InviteAcceptComponent {
    private members = inject(MembersService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    t = inject(LocaleService).t;

    peek = signal<InvitePeek | null>(null);
    loading = signal(true);
    failed = signal<string | null>(null);
    busy = signal(false);

    private token = '';

    constructor() {
        this.token = this.route.snapshot.paramMap.get('token') ?? '';
        void this.load();
    }

    roleWord(role: CanvasRole) {
        const c = this.t().projects.canvas;
        return role === 'owner' ? c.roleOwner : role === 'viewer' ? c.roleViewer : c.roleEditor;
    }

    private async load() {
        this.loading.set(true);
        try {
            this.peek.set(await this.members.peekInvite(this.token));
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
            const done = await this.members.accept(this.token);
            await this.router.navigate(['/projects', done.projectId, 'canvas']);
        } catch (e) {
            this.failed.set(httpErrorMessage(e, this.t().projects.canvas.acceptFailed));
        } finally {
            this.busy.set(false);
        }
    }
}
