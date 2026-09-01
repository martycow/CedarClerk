import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CanvasRole } from './boards.service';
import { InvitableRole } from './members.service';

// T-358 — teams. A team is a named group the owner invites people into once; handing it to a
// project then gives every one of them the project, which is what replaces re-inviting the same
// five people to every new project.
//
// Mirrors CedarClerk.Server.TeamEndpoints. Roles are the canvas's two (editor/viewer); what a team
// adds is a status, because "what may they do" and "may they do anything right now" are different
// questions and one column could not hold both without forgetting the role on the way back.

/** Mirrors CedarClerk.Core.TeamMemberStatuses. */
export type TeamMemberStatus = 'active' | 'restricted' | 'banned';
export const TEAM_MEMBER_STATUSES: readonly TeamMemberStatus[] = ['active', 'restricted', 'banned'];

export interface Team {
    id: string;
    name: string;
    createdAt: string;
    memberCount: number;
    /** Accepted, not banned, not restricted — the people who can actually act today. */
    activeCount: number;
    projectCount: number;
}

export interface TeamMember {
    id: string;
    userId: string | null;
    email: string;
    name: string;
    role: CanvasRole;
    status: TeamMemberStatus;
    statusNote: string | null;
    pending: boolean;
    invitedAt: string;
    acceptedAt: string | null;
    isYou: boolean;
}

/** A team this account was invited into — the only way a member learns one exists. */
export interface JoinedTeam {
    id: string;
    name: string;
    ownerName: string;
    role: CanvasRole;
    status: TeamMemberStatus;
}

export interface TeamInvitePeek {
    teamName: string;
    invitedBy: string;
    role: CanvasRole;
    alreadyMember: boolean;
}

@Injectable({ providedIn: 'root' })
export class TeamsService {
    private http = inject(HttpClient);

    list() {
        return firstValueFrom(this.http.get<Team[]>('/api/teams'));
    }

    joined() {
        return firstValueFrom(this.http.get<JoinedTeam[]>('/api/teams/joined'));
    }

    create(name: string) {
        return firstValueFrom(this.http.post<{ id: string; name: string }>('/api/teams', { name }));
    }

    rename(teamId: string, name: string) {
        return firstValueFrom(this.http.put<{ id: string; name: string }>(`/api/teams/${teamId}`, { name }));
    }

    /** Takes the team away from its projects; never deletes a project. */
    remove(teamId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/teams/${teamId}`));
    }

    members(teamId: string) {
        return firstValueFrom(this.http.get<TeamMember[]>(`/api/teams/${teamId}/members`));
    }

    /** The invitation URL comes back whether or not mail is configured. */
    invite(teamId: string, email: string, role: InvitableRole) {
        return firstValueFrom(this.http.post<{ member: TeamMember; inviteUrl: string }>(
            `/api/teams/${teamId}/members`, { email, role }));
    }

    resend(teamId: string, memberId: string) {
        return firstValueFrom(this.http.post<{ member: TeamMember; inviteUrl: string }>(
            `/api/teams/${teamId}/members/${memberId}/resend`, {}));
    }

    setRole(teamId: string, memberId: string, role: InvitableRole) {
        return firstValueFrom(this.http.put<TeamMember>(
            `/api/teams/${teamId}/members/${memberId}`, { role }));
    }

    /** Restrict, ban, and the way back from both. Its own route, never a field on the role PUT. */
    setStatus(teamId: string, memberId: string, status: TeamMemberStatus, note?: string) {
        return firstValueFrom(this.http.put<TeamMember>(
            `/api/teams/${teamId}/members/${memberId}/status`, { status, note: note ?? null }));
    }

    removeMember(teamId: string, memberId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/teams/${teamId}/members/${memberId}`));
    }

    /** Which team a project belongs to — a property of the project, set by the project's owner. */
    setProjectTeam(projectId: string, teamId: string | null) {
        return firstValueFrom(this.http.put<{ id: string; teamId: string | null }>(
            `/api/projects/${projectId}/team`, { teamId }));
    }

    peekInvite(token: string) {
        return firstValueFrom(this.http.get<TeamInvitePeek>(`/api/team-invites/${token}`));
    }

    acceptInvite(token: string) {
        return firstValueFrom(this.http.post<{ teamId: string; role: CanvasRole }>(
            `/api/team-invites/${token}/accept`, {}));
    }
}
