import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Admin panel data (IF2). Everything here is cross-owner and therefore lives behind /api/admin,
// which is gated server-side — see docs/admin-panel-scope.md for why this is a separate endpoint
// set rather than an "admin bypasses the owner filter" flag on the normal ones.
export interface AdminUser {
    id: string;
    email: string | null;
    createdAt: string;
    isAdmin: boolean;
    planTier: string;
    // What the account effectively has right now: a lapsed paid plan reads as Free here while
    // planTier still says what was bought.
    effectiveTier: string;
    planExpiresAt: string | null;
    trialUsed: boolean;
    telegramUsername: string | null;
    isLocked: boolean;
    drafts: number;
    published: number;
    channels: number;
}

export interface AdminSummary {
    users: number;
    paidUsers: number;
    drafts: number;
    published: number;
    comments: number;
    reactions: number;
    channels: number;
    storageBytes: number;
}

export interface AdminAuditEntry {
    id: string;
    actorEmail: string;
    action: string;
    targetEmail: string | null;
    details: string | null;
    createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
    private http = inject(HttpClient);

    listUsers() {
        return firstValueFrom(this.http.get<AdminUser[]>('/api/admin/users'));
    }

    summary() {
        return firstValueFrom(this.http.get<AdminSummary>('/api/admin/summary'));
    }

    audit() {
        return firstValueFrom(this.http.get<AdminAuditEntry[]>('/api/admin/audit'));
    }

    // expiresAt null on a paid tier is a manual grant that never expires — the same meaning the
    // rest of the app already gives it, not a second convention.
    setPlan(userId: string, tier: string, expiresAt: string | null) {
        return firstValueFrom(this.http.post<{ planTier: string; planExpiresAt: string | null }>(
            `/api/admin/users/${userId}/plan`, { tier, expiresAt }));
    }

    resetTrial(userId: string) {
        return firstValueFrom(this.http.post(`/api/admin/users/${userId}/reset-trial`, {}));
    }

    setLocked(userId: string, locked: boolean) {
        return firstValueFrom(this.http.post(`/api/admin/users/${userId}/lock`, { locked }));
    }

    setAdmin(userId: string, isAdmin: boolean) {
        return firstValueFrom(this.http.post(`/api/admin/users/${userId}/admin`, { isAdmin }));
    }
}
