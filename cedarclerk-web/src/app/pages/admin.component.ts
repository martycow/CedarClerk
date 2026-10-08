import { ConfirmationService } from '../core/confirmation.service';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
    AdminService, AdminAuditEntry, AdminBilling, AdminCollectionQuery, AdminDiscovery, AdminFeedbackEntry, AdminInviteCode,
    AdminLanding, AdminPost, AdminSummary, AdminUsage, AdminUser, AdminWaitlistEntry,
} from '../core/admin.service';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { SkeletonComponent, heldLoading } from '../shared/skeleton.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { ModalComponent } from '../shared/modal.component';
import { IconComponent } from '../shared/icon.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { LogLineComponent } from '../bench/worktop/log-line.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { SortHeaderComponent } from '../bench/worktop/sort-header.component';
import { SortDirection, ariaSort } from '../core/collection-query';
import { AuthService } from '../core/auth.service';
import { LandingEditorComponent } from './landing-editor.component';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { avatarFill, avatarInitial as initialOf } from '../core/avatar-color.util';

export type AdminTab = 'users' | 'invites' | 'posts' | 'landing' | 'discovery' | 'reports' | 'feedback';

type InviteFilter = 'all' | 'usable' | 'unusable';
type InviteSort = 'code' | 'label' | 'joined' | 'expires';
type PostFilter = 'all' | 'published' | 'private' | 'archived';
type PostSort = 'title' | 'owner' | 'state' | 'activity';
type WaitlistSort = 'email' | 'language' | 'created';
type PaymentSort = 'created' | 'owner' | 'plan' | 'amount' | 'status';
type UsageFilter = 'all' | 'storage' | 'ai';
type UsageSort = 'owner' | 'bytes' | 'files' | 'ai';

function compareValue(a: string | number, b: string | number): number {
    return typeof a === 'number' && typeof b === 'number'
        ? a - b
        : String(a).localeCompare(String(b), undefined, { sensitivity: 'base' });
}

function sortRows<T>(rows: readonly T[], direction: SortDirection,
    value: (row: T) => string | number, id: (row: T) => string): T[] {
    const multiplier = direction === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => compareValue(value(a), value(b)) * multiplier || id(a).localeCompare(id(b)));
}

// Admin panel (IF2), built in five scoped steps (ADR-122). Users and their management,
// invite codes, a read-only cross-owner post list, billing/usage reporting, and the audit log.
@Component({
    selector: 'app-admin',
    imports: [
        ZonedDatePipe, FormsModule, IndexTabsComponent, SpecRowComponent, LogLineComponent, ButtonComponent, SkeletonComponent,
        ModalComponent, IconComponent, PageHeaderComponent, EmptyStateComponent, SortHeaderComponent, LandingEditorComponent,
    ],
    templateUrl: 'admin.component.html',
    styleUrls: ['admin.component.css'],
})
export class AdminComponent implements OnInit, OnDestroy {
    private readonly confirmation = inject(ConfirmationService);
    auth = inject(AuthService);
    t = inject(LocaleService).t;
    private api = inject(AdminService);

    loading = signal(true);
    error = signal('');
    users = signal<AdminUser[]>([]);
    summary = signal<AdminSummary | null>(null);
    audit = signal<AdminAuditEntry[]>([]);
    auditHasMore = signal(false);
    auditLoadingMore = signal(false);
    showSkeleton = heldLoading(this.loading);
    showAuditSkeleton = heldLoading(this.auditLoadingMore);
    invites = signal<AdminInviteCode[]>([]);
    posts = signal<AdminPost[]>([]);
    postTotal = signal(0);
    postPageSize = signal(0);
    postSkip = signal(0);
    billing = signal<AdminBilling | null>(null);
    paymentSkip = signal(0);
    usage = signal<AdminUsage[]>([]);
    feedback = signal<AdminFeedbackEntry[]>([]);
    waitlistEntries = signal<AdminWaitlistEntry[]>([]);
    feedbackOnlyOpen = signal(false);
    readonly ariaSort = ariaSort;

    inviteQuery = signal('');
    inviteFilter = signal<InviteFilter>('all');
    inviteSortKey = signal<InviteSort>('code');
    inviteSortDirection = signal<SortDirection>('asc');
    visibleInvites = computed(() => {
        const query = this.inviteQuery().trim().toLocaleLowerCase();
        const filter = this.inviteFilter();
        const key = this.inviteSortKey();
        return sortRows(this.invites().filter(row =>
            (!query || `${row.code} ${row.label}`.toLocaleLowerCase().includes(query))
            && (filter === 'all' || (filter === 'usable') === row.isUsable)), this.inviteSortDirection(), row =>
                key === 'code' ? row.code : key === 'label' ? row.label
                    : key === 'joined' ? row.joined : row.expiresAt ?? '\uffff', row => row.id);
    });
    inviteFiltersActive = computed(() => Boolean(this.inviteQuery().trim()) || this.inviteFilter() !== 'all');

    postQuery = signal('');
    postFilter = signal<PostFilter>('all');
    postSortKey = signal<PostSort>('title');
    postSortDirection = signal<SortDirection>('asc');
    visiblePosts = computed(() => this.posts());
    postFiltersActive = computed(() => Boolean(this.postQuery().trim()) || this.postFilter() !== 'all');

    waitlistQuery = signal('');
    waitlistLanguage = signal('all');
    waitlistSortKey = signal<WaitlistSort>('created');
    waitlistSortDirection = signal<SortDirection>('desc');
    waitlistLanguages = computed(() => [...new Set(this.waitlistEntries().map(row => row.language))]
        .filter(Boolean).sort((a, b) => compareValue(a, b)));
    visibleWaitlist = computed(() => {
        const query = this.waitlistQuery().trim().toLocaleLowerCase();
        const language = this.waitlistLanguage();
        const key = this.waitlistSortKey();
        return sortRows(this.waitlistEntries().filter(row =>
            (!query || row.email.toLocaleLowerCase().includes(query))
            && (language === 'all' || row.language === language)), this.waitlistSortDirection(), row =>
                key === 'email' ? row.email : key === 'language' ? row.language : row.createdAt, row => row.id);
    });
    waitlistFiltersActive = computed(() => Boolean(this.waitlistQuery().trim()) || this.waitlistLanguage() !== 'all');

    paymentQuery = signal('');
    paymentStatus = signal('all');
    paymentSortKey = signal<PaymentSort>('created');
    paymentSortDirection = signal<SortDirection>('desc');
    paymentStatuses = computed(() => this.billing()?.statuses
        ?? [...new Set((this.billing()?.payments ?? []).map(row => row.status))]
        .filter(Boolean).sort((a, b) => compareValue(a, b)));
    visiblePayments = computed(() => this.billing()?.payments ?? []);
    paymentFiltersActive = computed(() => Boolean(this.paymentQuery().trim()) || this.paymentStatus() !== 'all');

    usageQuery = signal('');
    usageFilter = signal<UsageFilter>('all');
    usageSortKey = signal<UsageSort>('owner');
    usageSortDirection = signal<SortDirection>('asc');
    visibleUsage = computed(() => {
        const query = this.usageQuery().trim().toLocaleLowerCase();
        const filter = this.usageFilter();
        const key = this.usageSortKey();
        return sortRows(this.usage().filter(row =>
            (!query || (row.ownerEmail ?? '').toLocaleLowerCase().includes(query))
            && (filter === 'all' || filter === 'storage' && row.bytes > 0 || filter === 'ai' && row.aiToday > 0)),
        this.usageSortDirection(), row => key === 'owner' ? row.ownerEmail ?? ''
            : key === 'bytes' ? row.bytes : key === 'files' ? row.files : row.aiToday,
        row => row.ownerId);
    });
    usageFiltersActive = computed(() => Boolean(this.usageQuery().trim()) || this.usageFilter() !== 'all');

    private postSearchTimer: ReturnType<typeof setTimeout> | null = null;
    private paymentSearchTimer: ReturnType<typeof setTimeout> | null = null;
    private postRequestSequence = 0;
    private paymentRequestSequence = 0;

    // The panel outgrew one scroll once steps 4-5 landed — same tab pattern as the Posts Manager
    // and Settings, so the app's three secondary pages behave alike.
    tab = signal<AdminTab>('users');

    // Step 3 — new code form.
    newCode = '';
    newLabel = '';
    newExpiresAt = '';
    newMaxUses: number | null = null;

    // Step 2 — one account at a time; the actions are destructive-adjacent enough that having six
    // accounts' worth of controls on screen at once invites a misclick. T-257 moved the controls
    // from an inline row into a modal, so this now names the modal's subject.
    expandedId = signal<string | null>(null);
    deleteTarget = signal<AdminUser | null>(null);
    busy = signal(false);
    readonly tiers = ['Free', 'Pro', 'ProPlus', 'Forever'];
    planTier = 'Free';
    planExpiresAt = '';

    // The landing tab's block editor is its own component (ADR-323); what stays here is the
    // waitlist readout and the count the tab strip shows.
    landing = signal<AdminLanding | null>(null);
    private waitlistLoaded = false;

    discovery = signal<AdminDiscovery | null>(null);
    discoveryBusy = signal(false);
    showDiscoverySkeleton = heldLoading(computed(() => this.discoveryBusy() && !this.discovery()));
    discoverySaved = signal(false);
    df = {
        enabled: true,
        showScreenshotSaturday: true,
        showProjects: true,
        showBlogs: true,
        titleEn: '', titleRu: '', introEn: '', introRu: '',
    };

    async ngOnInit() {
        await this.reload();
        this.loading.set(false);
    }

    ngOnDestroy() {
        if (this.postSearchTimer) clearTimeout(this.postSearchTimer);
        if (this.paymentSearchTimer) clearTimeout(this.paymentSearchTimer);
    }

    async loadMoreAudit() {
        if (this.auditLoadingMore() || !this.auditHasMore()) return;
        this.auditLoadingMore.set(true);
        try {
            const next = await this.api.audit(this.audit().length);
            this.audit.update(list => [...list, ...next.entries]);
            this.auditHasMore.set(next.hasMore);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        } finally {
            this.auditLoadingMore.set(false);
        }
    }

    private async reload() {
        const postRequest = ++this.postRequestSequence;
        const paymentRequest = ++this.paymentRequestSequence;
        try {
            const [users, summary, audit, invites, postPage, billing, usage] = await Promise.all([
                this.api.listUsers(), this.api.summary(), this.api.audit(), this.api.listInvites(),
                this.api.listPosts(this.postRequest()), this.api.billing(this.paymentRequest()), this.api.usage(),
            ]);
            this.users.set(users);
            this.summary.set(summary);
            this.audit.set(audit.entries);
            this.auditHasMore.set(audit.hasMore);
            this.invites.set(invites);
            if (postRequest === this.postRequestSequence) {
                this.posts.set(postPage.items);
                this.postTotal.set(postPage.total);
                this.postPageSize.set(postPage.pageSize);
            }
            if (paymentRequest === this.paymentRequestSequence) this.billing.set(billing);
            this.usage.set(usage);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        }
    }

    setTab(tab: AdminTab) {
        this.tab.set(tab);
        if (tab === 'landing' && !this.waitlistLoaded) { this.waitlistLoaded = true; void this.loadLanding(); }
        if (tab === 'discovery' && !this.discovery()) void this.loadDiscovery();
        if (tab === 'feedback' && this.feedback().length === 0) void this.loadFeedback();
    }

    sortInvites(key: InviteSort) {
        if (this.inviteSortKey() === key) this.inviteSortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
        else {
            this.inviteSortKey.set(key);
            this.inviteSortDirection.set(key === 'joined' ? 'desc' : 'asc');
        }
    }

    sortPosts(key: PostSort) {
        if (this.postSortKey() === key) this.postSortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
        else {
            this.postSortKey.set(key);
            this.postSortDirection.set(key === 'activity' ? 'desc' : 'asc');
        }
        this.postSkip.set(0);
        void this.reloadPosts();
    }

    sortWaitlist(key: WaitlistSort) {
        if (this.waitlistSortKey() === key) this.waitlistSortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
        else {
            this.waitlistSortKey.set(key);
            this.waitlistSortDirection.set(key === 'created' ? 'desc' : 'asc');
        }
    }

    sortPayments(key: PaymentSort) {
        if (this.paymentSortKey() === key) this.paymentSortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
        else {
            this.paymentSortKey.set(key);
            this.paymentSortDirection.set(key === 'created' || key === 'amount' ? 'desc' : 'asc');
        }
        this.paymentSkip.set(0);
        void this.reloadPayments();
    }

    sortUsage(key: UsageSort) {
        if (this.usageSortKey() === key) this.usageSortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
        else {
            this.usageSortKey.set(key);
            this.usageSortDirection.set(key === 'owner' ? 'asc' : 'desc');
        }
    }

    clearInviteFilters() { this.inviteQuery.set(''); this.inviteFilter.set('all'); }
    clearPostFilters() {
        this.postQuery.set('');
        this.postFilter.set('all');
        this.postSkip.set(0);
        void this.reloadPosts();
    }
    clearWaitlistFilters() { this.waitlistQuery.set(''); this.waitlistLanguage.set('all'); }
    clearPaymentFilters() {
        this.paymentQuery.set('');
        this.paymentStatus.set('all');
        this.paymentSkip.set(0);
        void this.reloadPayments();
    }
    clearUsageFilters() { this.usageQuery.set(''); this.usageFilter.set('all'); }

    setPostFilter(filter: PostFilter) {
        this.postFilter.set(filter);
        this.postSkip.set(0);
        void this.reloadPosts();
    }

    setPaymentStatus(status: string) {
        this.paymentStatus.set(status);
        this.paymentSkip.set(0);
        void this.reloadPayments();
    }

    queuePostReload() {
        if (this.postSearchTimer) clearTimeout(this.postSearchTimer);
        this.postSkip.set(0);
        this.postSearchTimer = setTimeout(() => void this.reloadPosts(), 250);
    }

    queuePaymentReload() {
        if (this.paymentSearchTimer) clearTimeout(this.paymentSearchTimer);
        this.paymentSkip.set(0);
        this.paymentSearchTimer = setTimeout(() => void this.reloadPayments(), 250);
    }

    private postRequest(): AdminCollectionQuery {
        return {
            search: this.postQuery().trim(),
            filter: this.postFilter(),
            sort: this.postSortKey(),
            direction: this.postSortDirection(),
            skip: this.postSkip(),
        };
    }

    private paymentRequest(): AdminCollectionQuery {
        return {
            search: this.paymentQuery().trim(),
            filter: this.paymentStatus(),
            sort: this.paymentSortKey(),
            direction: this.paymentSortDirection(),
            skip: this.paymentSkip(),
        };
    }

    private async reloadPosts() {
        const request = ++this.postRequestSequence;
        try {
            const page = await this.api.listPosts(this.postRequest());
            if (request === this.postRequestSequence) {
                this.posts.set(page.items);
                this.postTotal.set(page.total);
                this.postPageSize.set(page.pageSize);
            }
        } catch (e) {
            if (request === this.postRequestSequence)
                this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        }
    }

    private async reloadPayments() {
        const request = ++this.paymentRequestSequence;
        try {
            const report = await this.api.billing(this.paymentRequest());
            if (request === this.paymentRequestSequence) this.billing.set(report);
        } catch (e) {
            if (request === this.paymentRequestSequence)
                this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        }
    }

    canPagePostsBack(): boolean { return this.postSkip() > 0; }
    canPagePostsForward(): boolean { return this.postSkip() + this.posts().length < this.postTotal(); }

    async pagePostsBack() {
        const pageSize = this.postPageSize();
        if (!this.canPagePostsBack() || !pageSize) return;
        this.postSkip.update(skip => Math.max(0, skip - pageSize));
        await this.reloadPosts();
    }

    async pagePostsForward() {
        const pageSize = this.postPageSize();
        if (!this.canPagePostsForward() || !pageSize) return;
        this.postSkip.update(skip => skip + pageSize);
        await this.reloadPosts();
    }

    postRangeLabel(): string {
        if (!this.postTotal()) return '';
        if (!this.posts().length) return `0 / ${this.postTotal()}`;
        return `${this.postSkip() + 1}–${Math.min(this.postSkip() + this.posts().length, this.postTotal())} / ${this.postTotal()}`;
    }

    canPagePaymentsBack(): boolean { return this.paymentSkip() > 0; }
    canPagePaymentsForward(): boolean {
        return this.paymentSkip() + (this.billing()?.payments.length ?? 0) < (this.billing()?.total ?? 0);
    }

    async pagePaymentsBack() {
        const pageSize = this.billing()?.pageSize ?? 0;
        if (!this.canPagePaymentsBack() || !pageSize) return;
        this.paymentSkip.update(skip => Math.max(0, skip - pageSize));
        await this.reloadPayments();
    }

    async pagePaymentsForward() {
        const pageSize = this.billing()?.pageSize ?? 0;
        if (!this.canPagePaymentsForward() || !pageSize) return;
        this.paymentSkip.update(skip => skip + pageSize);
        await this.reloadPayments();
    }

    paymentRangeLabel(): string {
        const report = this.billing();
        if (!report?.total) return '';
        if (!report.payments.length) return `0 / ${report.total}`;
        return `${this.paymentSkip() + 1}–${Math.min(this.paymentSkip() + report.payments.length, report.total)} / ${report.total}`;
    }

    sectionTabs(): IndexTabItem[] {
        const labels = this.t().admin;
        return [
            { id: 'users', label: labels.usersTitle, badge: this.users().length },
            { id: 'invites', label: labels.invites.title, badge: this.invites().length },
            { id: 'posts', label: labels.posts.title, badge: this.postTotal() },
            // The badge is the waitlist, which is the one countable thing the landing produces.
            { id: 'landing', label: labels.landing.title, badge: this.landing()?.waitlist },
            { id: 'discovery', label: labels.discovery.title, badge: this.discovery()?.eligiblePosts },
            // Reports is three tables and a journal, not a countable set of things.
            { id: 'reports', label: labels.reports.title },
            { id: 'feedback', label: this.t().feedbackForm.inbox, badge: this.feedback().filter(f => !f.handledAt).length },
        ];
    }

    // T-191 — loaded when the tab is first opened, like the landing tab.
    async loadFeedback() {
        try { this.feedback.set(await this.api.feedback(this.feedbackOnlyOpen())); }
        catch { /* the empty list stands */ }
    }

    async toggleFeedbackFilter() {
        this.feedbackOnlyOpen.update(v => !v);
        await this.loadFeedback();
    }

    feedbackKindLabel(kind: string): string {
        const kinds = this.t().feedbackForm.kinds;
        return kind === 'bug' ? kinds.bug : kind === 'idea' ? kinds.idea : kinds.other;
    }

    async markFeedback(entry: AdminFeedbackEntry, handled: boolean) {
        const res = await this.api.setFeedbackHandled(entry.id, handled);
        this.feedback.update(list => list.map(f => f.id === entry.id ? { ...f, handledAt: res.handledAt } : f));
    }

    sectionTitle(): string {
        return this.sectionTabs().find(item => item.id === this.tab())?.label ?? '';
    }

    headerMeta(): HeaderMeta[] {
        const s = this.summary();
        if (!s) return [];
        const t = this.t().admin;
        return [
            { text: t.headerUsers(s.users) },
            { text: t.headerPaid(s.paidUsers) },
            { text: t.headerStorage(this.formatBytes(s.storageBytes)) },
        ];
    }

    // Payments are stored in minor units (cents/stars), like everywhere else in billing.
    formatAmount(amount: number, currency: string): string {
        return currency.toUpperCase() === 'XTR' ? `${amount} Stars` : `${(amount / 100).toFixed(2)} ${currency.toUpperCase()}`;
    }

    isSelf(u: AdminUser): boolean {
        return u.email === this.auth.userEmail();
    }

    // Priority Fixes 01 (Claude Design, 28.07.2026) — the Users table became a card list, each
    // card fronted by an initials avatar instead of a bare email string.
    avatarColor(email: string | null): string {
        return avatarFill(email);
    }

    avatarInitial(email: string | null): string {
        return initialOf(email);
    }

    /** Re-read from the list rather than held, so every `run()` reload refreshes the open modal. */
    modalUser(): AdminUser | null {
        const id = this.expandedId();
        return id ? this.users().find(u => u.id === id) ?? null : null;
    }

    openUser(u: AdminUser) {
        this.expandedId.set(u.id);
        // Seed the form from what the account currently has, so "save" without touching anything
        // is a no-op rather than a silent reset to Free.
        this.planTier = u.planTier;
        this.planExpiresAt = u.planExpiresAt ? u.planExpiresAt.slice(0, 10) : '';
    }

    closeUser() {
        this.expandedId.set(null);
    }

    // Every action reloads rather than patching local state: an admin change can move several
    // things at once (tier + effective tier + audit log), and a stale row here is worse than a
    // round-trip.
    private async run(action: () => Promise<unknown>) {
        if (this.busy()) return;
        this.busy.set(true);
        this.error.set('');
        try {
            await action();
            await this.reload();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    savePlan(u: AdminUser) {
        // A date input gives a bare day; the server stores an instant. End of day UTC, so
        // "expires on the 5th" means the 5th is still usable.
        const expiry = this.planTier === 'Free' || !this.planExpiresAt
            ? null
            : new Date(`${this.planExpiresAt}T23:59:59Z`).toISOString();
        return this.run(() => this.api.setPlan(u.id, this.planTier, expiry));
    }

    creditAmount = 10;
    creditNote = '';

    adjustCredits(u: AdminUser, sign: 1 | -1) {
        const amount = Math.trunc(Math.abs(this.creditAmount)) * sign;
        if (!amount) return Promise.resolve();
        return this.run(async () => {
            await this.api.adjustCredits(u.id, amount, this.creditNote.trim() || null);
            this.creditNote = '';
        });
    }

    resetTrial(u: AdminUser) {
        return this.run(() => this.api.resetTrial(u.id));
    }

    toggleLock(u: AdminUser) {
        return this.run(() => this.api.setLocked(u.id, !u.isLocked));
    }

    toggleAdmin(u: AdminUser) {
        return this.run(() => this.api.setAdmin(u.id, !u.isAdmin));
    }

    // Two steps, like deleting a draft: the modal names the account so the wrong row cannot be
    // dismissed with a reflex click. The account modal closes rather than stacking under this one —
    // ModalComponent binds document:keydown.escape on every mounted instance, so two on screen mean
    // one Escape dismisses both, taking the destructive question away with its context (ADR-238).
    askDelete(u: AdminUser) {
        this.expandedId.set(null);
        this.deleteTarget.set(u);
    }

    cancelDelete() {
        this.deleteTarget.set(null);
    }

    async confirmDelete() {
        const target = this.deleteTarget();
        if (!target) return;
        this.deleteTarget.set(null);
        await this.run(async () => {
            await this.api.deleteAccount(target.id);
            this.users.update(list => list.filter(u => u.id !== target.id));
        });
    }

    // ---------- Step 3: invite codes ----------

    createInvite() {
        const code = this.newCode.trim();
        if (!code) return Promise.resolve();
        const expiry = this.newExpiresAt ? new Date(`${this.newExpiresAt}T23:59:59Z`).toISOString() : null;
        return this.run(async () => {
            await this.api.createInvite(code, this.newLabel.trim(), expiry, this.newMaxUses);
            this.newCode = '';
            this.newLabel = '';
            this.newExpiresAt = '';
            this.newMaxUses = null;
        });
    }

    toggleInvite(c: AdminInviteCode) {
        return this.run(() => this.api.setInviteActive(c.id, !c.isActive));
    }

    inviteLabel(u: AdminUser): string {
        if (!u.inviteCodeId) return this.t().admin.invites.unknownOrigin;
        return this.invites().find(c => c.id === u.inviteCodeId)?.code ?? this.t().admin.invites.unknownOrigin;
    }

    // The one-off fix for accounts that predate invite tracking (Marty's answer 4).
    attribute(u: AdminUser, codeId: string) {
        return this.run(() => this.api.setUserInvite(u.id, codeId || null));
    }

    // Bytes are the honest unit server-side; nobody reads a raw byte count.
    formatBytes(bytes: number): string {
        if (bytes < 1024) return `${bytes} B`;
        const units = ['KB', 'MB', 'GB'];
        let value = bytes / 1024;
        let unit = 0;
        while (value >= 1024 && unit < units.length - 1) {
            value /= 1024;
            unit++;
        }
        return `${value.toFixed(value < 10 ? 1 : 0)} ${units[unit]}`;
    }

    async loadLanding() {
        try {
            this.waitlistEntries.set(await this.api.waitlist());
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        }
    }

    async loadDiscovery() {
        this.discoveryBusy.set(true);
        try {
            const data = await this.api.discovery();
            this.discovery.set(data);
            this.df = {
                enabled: data.enabled,
                showScreenshotSaturday: data.showScreenshotSaturday,
                showProjects: data.showProjects,
                showBlogs: data.showBlogs,
                titleEn: data.titleEn ?? '',
                titleRu: data.titleRu ?? '',
                introEn: data.introEn ?? '',
                introRu: data.introRu ?? '',
            };
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        } finally {
            this.discoveryBusy.set(false);
        }
    }

    async saveDiscovery() {
        if (this.discoveryBusy()) return;
        this.discoveryBusy.set(true);
        this.discoverySaved.set(false);
        this.error.set('');
        try {
            await this.api.saveDiscovery({
                enabled: this.df.enabled,
                showScreenshotSaturday: this.df.showScreenshotSaturday,
                showProjects: this.df.showProjects,
                showBlogs: this.df.showBlogs,
                titleEn: this.df.titleEn.trim() || null,
                titleRu: this.df.titleRu.trim() || null,
                introEn: this.df.introEn.trim() || null,
                introRu: this.df.introRu.trim() || null,
            });
            this.discoverySaved.set(true);
            await this.loadDiscovery();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.actionFailed));
        } finally {
            this.discoveryBusy.set(false);
        }
    }

    // A lapsed paid plan is the case worth flagging: the account still says Pro but behaves Free.
    isLapsed(u: AdminUser): boolean {
        return u.planTier !== 'Free' && u.effectiveTier === 'Free';
    }
}
