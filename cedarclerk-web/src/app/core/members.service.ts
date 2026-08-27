import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CanvasRole } from './boards.service';

// T-301 / ADR-217 — who may act on a project besides its owner. Membership widens the canvas, this
// list, and the shared-project list; it widens nothing else, and the frozen contract is explicit
// that a member hitting /projects/:id still gets 404.

export interface ProjectMember {
    /** Null on the owner's row: the owner has no membership row, and must not look as if they had. */
    id: string | null;
    userId: string | null;
    email: string;
    role: CanvasRole;
    pending: boolean;
    invitedAt: string | null;
    acceptedAt: string | null;
    lastSeenAt: string | null;
    isYou: boolean;
}

export interface SharedProject {
    id: string;
    name: string;
    ownerName: string;
    role: CanvasRole;
    boardCount: number;
    lastActivityAt: string;
}

/** A role that can be handed out. The owner's is identity, never a value in a column. */
export type InvitableRole = 'editor' | 'viewer';
export const INVITABLE_ROLES: readonly InvitableRole[] = ['editor', 'viewer'];

@Injectable({ providedIn: 'root' })
export class MembersService {
    private http = inject(HttpClient);

    list(projectId: string) {
        return firstValueFrom(this.http.get<ProjectMember[]>(`/api/projects/${projectId}/members`));
    }

    /** The invitation URL comes back whether or not mail is configured — an owner who can copy the
        link is not blocked by a missing provider key. */
    invite(projectId: string, email: string, role: InvitableRole) {
        return firstValueFrom(this.http.post<{ member: ProjectMember; inviteUrl: string }>(
            `/api/projects/${projectId}/members`, { email, role }));
    }

    /** Its own route, not a second invite: a second POST would collide with the unique index, and
        the mail that did arrive keeps working because the token is reused. */
    resend(projectId: string, memberId: string) {
        return firstValueFrom(this.http.post<{ member: ProjectMember; inviteUrl: string }>(
            `/api/projects/${projectId}/members/${memberId}/resend`, {}));
    }

    setRole(projectId: string, memberId: string, role: InvitableRole) {
        return firstValueFrom(this.http.put<ProjectMember>(
            `/api/projects/${projectId}/members/${memberId}`, { role }));
    }

    /** Also how a pending invitation is revoked. Their images stay on the boards. */
    remove(projectId: string, memberId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/projects/${projectId}/members/${memberId}`));
    }

    peekInvite(token: string) {
        return firstValueFrom(this.http.get<{
            projectName: string; invitedBy: string; role: CanvasRole; alreadyMember: boolean;
        }>(`/api/project-invites/${token}`));
    }

    accept(token: string) {
        return firstValueFrom(this.http.post<{ projectId: string; role: CanvasRole }>(
            `/api/project-invites/${token}/accept`, {}));
    }

    shared() {
        return firstValueFrom(this.http.get<SharedProject[]>('/api/projects/shared'));
    }
}
