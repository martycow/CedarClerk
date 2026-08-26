import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Admin panel data (IF2). Everything here is cross-owner and therefore lives behind /api/admin,
// which is gated server-side — see ADR-122 (docs/DECISIONS.md) for why this is a separate endpoint
// set rather than an "admin bypasses the owner filter" flag on the normal ones.
export interface AdminUser {
    id: string;
    email: string | null;
    createdAt: string;
    isAdmin: boolean;
    // Null for accounts that predate invite tracking or used the config fallback.
    inviteCodeId: string | null;
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
    /** ADR-092 — the sum of the credit ledger, not a stored counter. */
    credits: number;
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

export interface AdminInviteCode {
    id: string;
    code: string;
    label: string;
    isActive: boolean;
    expiresAt: string | null;
    maxUses: number | null;
    uses: number;
    createdAt: string;
    // Counted from the users table, not from `uses` — that counter can only drift, this can't.
    joined: number;
    // Active AND not expired AND under its cap, resolved server-side so the UI doesn't re-derive it.
    isUsable: boolean;
}

export interface AdminPost {
    id: string;
    title: string;
    ownerEmail: string | null;
    updatedAt: string;
    isBlogPublished: boolean;
    isPrivate: boolean;
    isArchived: boolean;
    viewCount: number;
    comments: number;
    blogUrl: string | null;
    telegramUrl: string | null;
}

export interface AdminPayment {
    id: string;
    provider: string;
    plan: string;
    amount: number;
    currency: string;
    status: string;
    createdAt: string;
    ownerEmail: string | null;
}

export interface AdminBilling {
    payments: AdminPayment[];
    // Completed payments only — a failed or pending row is not money.
    totalByCurrency: { currency: string; total: number }[];
}

export interface AdminUsage {
    ownerEmail: string | null;
    bytes: number;
    files: number;
    aiToday: number;
}

export interface AdminAuditEntry {
    id: string;
    actorEmail: string;
    action: string;
    targetEmail: string | null;
    details: string | null;
    createdAt: string;
}

// The landing page's editable half (ADR-215). The page's structure, prices and feature list stay in
// the server's code; these are the strings and lists no test can check.
export interface LandingTextPair {
    en: string | null;
    ru: string | null;
}

export interface LandingShot {
    file: string;
    caption: LandingTextPair;
}

/** `mark` is done | doing | next — which glyph and which colour the column's items take. */
export interface LandingRoadmapColumn {
    title: LandingTextPair;
    mark: string;
    items: LandingTextPair[];
}

export interface LandingStoryStep {
    when: LandingTextPair;
    title: LandingTextPair;
    text: LandingTextPair;
}

export interface AdminLanding {
    kickerEn: string | null;
    kickerRu: string | null;
    heroTitleEn: string | null;
    heroTitleRu: string | null;
    heroSubEn: string | null;
    heroSubRu: string | null;
    proofEn: string | null;
    proofRu: string | null;
    noteEn: string | null;
    noteRu: string | null;
    showcaseBlog: string | null;
    showShots: boolean;
    showFeatures: boolean;
    showPricing: boolean;
    showRoadmap: boolean;
    showStory: boolean;
    shots: LandingShot[];
    roadmap: LandingRoadmapColumn[];
    story: LandingStoryStep[];
    /** What the page falls back to when a field is left blank — shown as the field's placeholder. */
    defaults: { kicker: LandingTextPair; heroTitle: LandingTextPair; heroSub: LandingTextPair };
    configuredShowcaseBlog: string | null;
    /** Every image actually on disk, including any the current list no longer points at. */
    files: string[];
    waitlist: number;
}

export interface AdminWaitlistEntry {
    id: string;
    email: string;
    language: string;
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

    // Paged rather than capped: the log is append-only and never trimmed, so "the newest 100"
    // would quietly hide everything before them.
    audit(skip = 0) {
        return firstValueFrom(this.http.get<{ entries: AdminAuditEntry[]; hasMore: boolean }>(
            `/api/admin/audit?skip=${skip}`));
    }

    // expiresAt null on a paid tier is a manual grant that never expires — the same meaning the
    // rest of the app already gives it, not a second convention.
    setPlan(userId: string, tier: string, expiresAt: string | null) {
        return firstValueFrom(this.http.post<{ planTier: string; planExpiresAt: string | null }>(
            `/api/admin/users/${userId}/plan`, { tier, expiresAt }));
    }

    /** A signed movement: positive tops up, negative takes back. Returns the new balance. */
    adjustCredits(userId: string, amount: number, note: string | null) {
        return firstValueFrom(
            this.http.post<{ balance: number }>(`/api/admin/users/${userId}/credits`, { amount, note }));
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

    /** Irreversible: takes the account's documents, files, channels and payment records with it. */
    deleteAccount(userId: string) {
        return firstValueFrom(this.http.delete<{ drafts: number; assets: number; channels: number }>(
            `/api/admin/users/${userId}`));
    }

    listPosts() {
        return firstValueFrom(this.http.get<AdminPost[]>('/api/admin/posts'));
    }

    billing() {
        return firstValueFrom(this.http.get<AdminBilling>('/api/admin/billing'));
    }

    usage() {
        return firstValueFrom(this.http.get<AdminUsage[]>('/api/admin/usage'));
    }

    listInvites() {
        return firstValueFrom(this.http.get<AdminInviteCode[]>('/api/admin/invites'));
    }

    createInvite(code: string, label: string, expiresAt: string | null, maxUses: number | null) {
        return firstValueFrom(this.http.post('/api/admin/invites', { code, label, expiresAt, maxUses }));
    }

    // Deactivate, never delete: accounts point at the code row, and removing it would erase
    // their attribution.
    setInviteActive(id: string, isActive: boolean) {
        return firstValueFrom(this.http.post(`/api/admin/invites/${id}/active`, { isActive }));
    }

    // Manual attribution for accounts that predate invite tracking. Null clears it.
    setUserInvite(userId: string, inviteCodeId: string | null) {
        return firstValueFrom(this.http.post(`/api/admin/users/${userId}/invite`, { inviteCodeId }));
    }

    landing() {
        return firstValueFrom(this.http.get<AdminLanding>('/api/admin/landing'));
    }

    // The whole page in one PUT, not a field at a time: the sections, the copy and the lists are
    // read together to draw one page, and half-saving them is how a headline ends up describing a
    // section that was switched off.
    saveLanding(body: Omit<AdminLanding, 'defaults' | 'configuredShowcaseBlog' | 'files' | 'waitlist'>) {
        return firstValueFrom(this.http.put('/api/admin/landing', body));
    }

    uploadLandingShot(file: File) {
        const form = new FormData();
        form.append('file', file);
        return firstValueFrom(
            this.http.post<{ file: string; url: string }>('/api/admin/landing/upload', form));
    }

    /** Removes the image from disk. Taking it out of the list is a separate, saveable edit. */
    deleteLandingFile(file: string) {
        return firstValueFrom(this.http.delete(`/api/admin/landing/files/${encodeURIComponent(file)}`));
    }

    waitlist() {
        return firstValueFrom(this.http.get<AdminWaitlistEntry[]>('/api/admin/landing/waitlist'));
    }
}
