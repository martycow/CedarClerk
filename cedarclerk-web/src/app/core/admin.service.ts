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

@Injectable({ providedIn: 'root' })
export class AdminService {
    private http = inject(HttpClient);

    listUsers() {
        return firstValueFrom(this.http.get<AdminUser[]>('/api/admin/users'));
    }

    summary() {
        return firstValueFrom(this.http.get<AdminSummary>('/api/admin/summary'));
    }
}
