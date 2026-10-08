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
    statuses: string[];
    total: number;
    pageSize: number;
}

export interface AdminPostPage {
    items: AdminPost[];
    total: number;
    pageSize: number;
}

export interface AdminCollectionQuery {
    search: string;
    filter: string;
    sort: string;
    direction: 'asc' | 'desc';
    skip: number;
}

export interface AdminUsage {
    ownerId: string;
    ownerEmail: string | null;
    bytes: number;
    files: number;
    aiToday: number;
}

export interface AdminFeedbackEntry {
    id: string;
    kind: string;
    message: string;
    path: string | null;
    handledAt: string | null;
    createdAt: string;
    email: string;
}

export interface AdminAuditEntry {
    id: string;
    actorEmail: string;
    action: string;
    /** Stamped server-side from the action (`AdminEndpoints.SeverityOf`); the journal itself stays a plain record. */
    severity: 'ok' | 'warn' | 'info';
    targetEmail: string | null;
    details: string | null;
    createdAt: string;
}

// The landing as ADR-323 stores it: sections of typed blocks, every text a language map.
export interface LandingTextPair {
    en: string | null;
    ru: string | null;
}

export type LandingTextMap = Record<string, string>;

export interface LandingFeature {
    id: string;
    icon: string;
    title: LandingTextMap;
    body: LandingTextMap;
    shot: string | null;
}

export interface LandingItem {
    text: Record<string, LandingTextMap>;
    icon: string | null;
    file: string | null;
    /** done | doing | next — which glyph a board column's entries take. */
    mark: string | null;
    entries: LandingTextMap[];
}

export interface LandingBlock {
    id: string;
    type: string;
    style: string | null;
    hidden: boolean;
    text: Record<string, LandingTextMap>;
    items: LandingItem[];
    options: Record<string, boolean>;
    url: string | null;
    image: string | null;
}

export interface LandingSection {
    id: string;
    layout: string;
    anchor: string | null;
    hidden: boolean;
    nav: LandingTextMap;
    label: LandingTextMap;
    blocks: LandingBlock[];
}

export interface LandingDocument {
    languages: string[];
    showcaseBlog: string | null;
    features: LandingFeature[];
    sections: LandingSection[];
}

export interface LandingStyleSpec {
    id: string;
    fields: string[];
    itemFields: string[];
    icons: boolean;
    files: boolean;
    marks: boolean;
    entries: boolean;
    image: boolean;
    url: boolean;
}

export interface LandingBlockSpec {
    type: string;
    styles: LandingStyleSpec[];
    required: string[];
    requiredItem: string[];
    options: string[];
    single: boolean;
}

export interface LandingSchema {
    layouts: string[];
    blocks: LandingBlockSpec[];
    marks: string[];
    requiredLanguages: string[];
    languages: { code: string; endonym: string }[];
    tokens: string[];
}

export interface AdminLanding {
    document: LandingDocument;
    /** False while the page is still read from the pre-block columns. */
    stored: boolean;
    schema: LandingSchema;
    configuredShowcaseBlog: string | null;
    /** Every image actually on disk, including any the document no longer points at. */
    files: string[];
    waitlist: number;
}

export interface LandingPreview {
    html: string;
    language: string;
    /** Why this document would be refused on save, or null. */
    problem: string | null;
}

export interface AdminDiscovery {
    enabled: boolean;
    showScreenshotSaturday: boolean;
    showProjects: boolean;
    showBlogs: boolean;
    titleEn: string | null;
    titleRu: string | null;
    introEn: string | null;
    introRu: string | null;
    defaults: { title: LandingTextPair; intro: LandingTextPair };
    optedInAuthors: number;
    eligibleProjects: number;
    eligiblePosts: number;
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

    // T-191 — the feedback inbox.
    feedback(onlyOpen = false) {
        return firstValueFrom(this.http.get<AdminFeedbackEntry[]>(
            `/api/admin/feedback${onlyOpen ? '?handled=false' : ''}`));
    }

    setFeedbackHandled(id: string, handled: boolean) {
        return firstValueFrom(this.http.post<{ handledAt: string | null }>(
            `/api/admin/feedback/${id}/handled`, { handled }));
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

    listPosts(query?: AdminCollectionQuery) {
        return firstValueFrom(this.http.get<AdminPostPage>('/api/admin/posts', {
            params: query ? {
                q: query.search,
                state: query.filter,
                sort: query.sort,
                direction: query.direction,
                skip: query.skip,
            } : {},
        }));
    }

    billing(query?: AdminCollectionQuery) {
        return firstValueFrom(this.http.get<AdminBilling>('/api/admin/billing', {
            params: query ? {
                q: query.search,
                status: query.filter,
                sort: query.sort,
                direction: query.direction,
                skip: query.skip,
            } : {},
        }));
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
    saveLanding(document: LandingDocument) {
        return firstValueFrom(this.http.put('/api/admin/landing', { document }));
    }

    previewLanding(document: LandingDocument, language: string) {
        return firstValueFrom(this.http.post<LandingPreview>('/api/admin/landing/preview', { document, language }));
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

    discovery() {
        return firstValueFrom(this.http.get<AdminDiscovery>('/api/admin/discovery'));
    }

    saveDiscovery(body: Pick<AdminDiscovery,
        'enabled' | 'showScreenshotSaturday' | 'showProjects' | 'showBlogs'
        | 'titleEn' | 'titleRu' | 'introEn' | 'introRu'>) {
        return firstValueFrom(this.http.put('/api/admin/discovery', body));
    }
}
