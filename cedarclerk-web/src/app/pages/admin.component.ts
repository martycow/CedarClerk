import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
    AdminService, AdminAuditEntry, AdminBilling, AdminFeedbackEntry, AdminInviteCode, AdminLanding, AdminPost,
    AdminSummary, AdminUsage, AdminUser, AdminWaitlistEntry, LandingTextPair,
} from '../core/admin.service';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { ButtonComponent } from '../bench/forms/button.component';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';
import { ModalComponent } from '../shared/modal.component';
import { IconComponent } from '../shared/icon.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { LogLineComponent } from '../bench/worktop/log-line.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { avatarFill, avatarInitial as initialOf } from '../core/avatar-color.util';

export type AdminTab = 'users' | 'invites' | 'posts' | 'landing' | 'reports' | 'feedback';

// The landing editor's own shapes (ADR-215). A roadmap column carries a list of
// bilingual items, and a list of pairs is a miserable thing to edit field by field —
// so each language's items are one textarea, one item per line, zipped back by index.
export interface RoadmapVm { titleEn: string; titleRu: string; mark: string; itemsEn: string; itemsRu: string; }
export interface StoryVm { whenEn: string; whenRu: string; titleEn: string; titleRu: string; textEn: string; textRu: string; }
export interface ShotVm { file: string; capEn: string; capRu: string; }

// Admin panel (IF2), built in five scoped steps (ADR-122). Users and their management,
// invite codes, a read-only cross-owner post list, billing/usage reporting, and the audit log.
@Component({
    selector: 'app-admin',
    imports: [
        ZonedDatePipe, FormsModule, IndexTabsComponent, ShelfPanelComponent, SpecRowComponent,
        LogLineComponent, PaperCardComponent, ButtonComponent, ModalComponent, IconComponent,
        StampBadgeComponent,
    ],
    templateUrl: 'admin.component.html',
    styleUrls: ['admin.component.css'],
})
export class AdminComponent implements OnInit {
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
    invites = signal<AdminInviteCode[]>([]);
    posts = signal<AdminPost[]>([]);
    billing = signal<AdminBilling | null>(null);
    usage = signal<AdminUsage[]>([]);
    feedback = signal<AdminFeedbackEntry[]>([]);
    feedbackOnlyOpen = signal(false);

    // The panel outgrew one scroll once steps 4-5 landed — same tab pattern as the Posts Manager
    // and Settings, so the app's three secondary pages behave alike.
    tab = signal<AdminTab>('users');

    // Step 3 — new code form.
    newCode = '';
    newLabel = '';
    newExpiresAt = '';
    newMaxUses: number | null = null;

    // Step 2 — one expanded row at a time; the actions are destructive-adjacent enough that
    // having six accounts' worth of controls on screen at once invites a misclick.
    expandedId = signal<string | null>(null);
    deleteTarget = signal<AdminUser | null>(null);
    busy = signal(false);
    readonly tiers = ['Free', 'Pro', 'ProPlus', 'Forever'];
    planTier = 'Free';
    planExpiresAt = '';

    // ---------- the landing tab (ADR-215) ----------
    // Loaded when the tab is first opened rather than with the panel: it is a form and a file
    // listing, and four of the five admin tabs never look at it.
    landing = signal<AdminLanding | null>(null);
    waitlistEntries = signal<AdminWaitlistEntry[]>([]);
    landingBusy = signal(false);
    landingSaved = signal(false);
    readonly marks = ['done', 'doing', 'next'];
    lf = {
        kickerEn: '', kickerRu: '',
        heroTitleEn: '', heroTitleRu: '',
        heroSubEn: '', heroSubRu: '',
        proofEn: '', proofRu: '',
        noteEn: '', noteRu: '',
        showcaseBlog: '',
        showShots: true, showFeatures: true, showPricing: true, showRoadmap: false, showStory: false,
        showDownload: false,
    };
    shots: ShotVm[] = [];
    roadmapCols: RoadmapVm[] = [];
    storySteps: StoryVm[] = [];

    async ngOnInit() {
        await this.reload();
        this.loading.set(false);
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
        try {
            const [users, summary, audit, invites, posts, billing, usage] = await Promise.all([
                this.api.listUsers(), this.api.summary(), this.api.audit(), this.api.listInvites(),
                this.api.listPosts(), this.api.billing(), this.api.usage(),
            ]);
            this.users.set(users);
            this.summary.set(summary);
            this.audit.set(audit.entries);
            this.auditHasMore.set(audit.hasMore);
            this.invites.set(invites);
            this.posts.set(posts);
            this.billing.set(billing);
            this.usage.set(usage);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        }
    }

    setTab(tab: AdminTab) {
        this.tab.set(tab);
        if (tab === 'landing' && !this.landing()) void this.loadLanding();
        if (tab === 'feedback' && this.feedback().length === 0) void this.loadFeedback();
    }

    sectionTabs(): IndexTabItem[] {
        const labels = this.t().admin;
        return [
            { id: 'users', label: labels.usersTitle, badge: this.users().length },
            { id: 'invites', label: labels.invites.title, badge: this.invites().length },
            { id: 'posts', label: labels.posts.title, badge: this.posts().length },
            // The badge is the waitlist, which is the one countable thing the landing produces.
            { id: 'landing', label: labels.landing.title, badge: this.landing()?.waitlist },
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

    toggleExpanded(u: AdminUser) {
        if (this.expandedId() === u.id) {
            this.expandedId.set(null);
            return;
        }
        this.expandedId.set(u.id);
        // Seed the form from what the account currently has, so "save" without touching anything
        // is a no-op rather than a silent reset to Free.
        this.planTier = u.planTier;
        this.planExpiresAt = u.planExpiresAt ? u.planExpiresAt.slice(0, 10) : '';
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
    // dismissed with a reflex click.
    askDelete(u: AdminUser) {
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

    // ---------- the landing page (ADR-215) ----------

    private pair(value: LandingTextPair | undefined, key: 'en' | 'ru'): string {
        return value?.[key] ?? '';
    }

    async loadLanding() {
        this.landingBusy.set(true);
        try {
            const [data, waitlist] = await Promise.all([this.api.landing(), this.api.waitlist()]);
            this.landing.set(data);
            this.waitlistEntries.set(waitlist);
            this.lf = {
                kickerEn: data.kickerEn ?? '', kickerRu: data.kickerRu ?? '',
                heroTitleEn: data.heroTitleEn ?? '', heroTitleRu: data.heroTitleRu ?? '',
                heroSubEn: data.heroSubEn ?? '', heroSubRu: data.heroSubRu ?? '',
                proofEn: data.proofEn ?? '', proofRu: data.proofRu ?? '',
                noteEn: data.noteEn ?? '', noteRu: data.noteRu ?? '',
                showcaseBlog: data.showcaseBlog ?? '',
                showShots: data.showShots, showFeatures: data.showFeatures,
                showPricing: data.showPricing, showRoadmap: data.showRoadmap, showStory: data.showStory,
                showDownload: data.showDownload,
            };
            this.shots = data.shots.map(s => ({
                file: s.file, capEn: this.pair(s.caption, 'en'), capRu: this.pair(s.caption, 'ru'),
            }));
            this.roadmapCols = data.roadmap.map(c => ({
                titleEn: this.pair(c.title, 'en'), titleRu: this.pair(c.title, 'ru'), mark: c.mark || 'next',
                itemsEn: c.items.map(i => i.en ?? '').join('\n'),
                itemsRu: c.items.map(i => i.ru ?? '').join('\n'),
            }));
            this.storySteps = data.story.map(v => ({
                whenEn: this.pair(v.when, 'en'), whenRu: this.pair(v.when, 'ru'),
                titleEn: this.pair(v.title, 'en'), titleRu: this.pair(v.title, 'ru'),
                textEn: this.pair(v.text, 'en'), textRu: this.pair(v.text, 'ru'),
            }));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        } finally {
            this.landingBusy.set(false);
        }
    }

    /** Blank stays blank all the way to the column, where null means "use what the code says". */
    private text(en: string, ru: string): LandingTextPair {
        return { en: en.trim() || null, ru: ru.trim() || null };
    }

    /** Two textareas zipped by line. A language with fewer lines simply has fewer halves filled. */
    private zip(en: string, ru: string): LandingTextPair[] {
        const a = en.split('\n').map(l => l.trim());
        const b = ru.split('\n').map(l => l.trim());
        return Array.from({ length: Math.max(a.length, b.length) }, (_, i) => this.text(a[i] ?? '', b[i] ?? ''))
            .filter(pair => pair.en || pair.ru);
    }

    async saveLanding() {
        if (this.landingBusy()) return;
        this.landingBusy.set(true);
        this.error.set('');
        this.landingSaved.set(false);
        try {
            await this.api.saveLanding({
                kickerEn: this.lf.kickerEn.trim() || null, kickerRu: this.lf.kickerRu.trim() || null,
                heroTitleEn: this.lf.heroTitleEn.trim() || null, heroTitleRu: this.lf.heroTitleRu.trim() || null,
                heroSubEn: this.lf.heroSubEn.trim() || null, heroSubRu: this.lf.heroSubRu.trim() || null,
                proofEn: this.lf.proofEn.trim() || null, proofRu: this.lf.proofRu.trim() || null,
                noteEn: this.lf.noteEn.trim() || null, noteRu: this.lf.noteRu.trim() || null,
                showcaseBlog: this.lf.showcaseBlog.trim() || null,
                showShots: this.lf.showShots, showFeatures: this.lf.showFeatures,
                showPricing: this.lf.showPricing, showRoadmap: this.lf.showRoadmap, showStory: this.lf.showStory,
                showDownload: this.lf.showDownload,
                shots: this.shots.map(s => ({ file: s.file, caption: this.text(s.capEn, s.capRu) })),
                roadmap: this.roadmapCols.map(c => ({
                    title: this.text(c.titleEn, c.titleRu), mark: c.mark, items: this.zip(c.itemsEn, c.itemsRu),
                })),
                story: this.storySteps.map(v => ({
                    when: this.text(v.whenEn, v.whenRu),
                    title: this.text(v.titleEn, v.titleRu),
                    text: this.text(v.textEn, v.textRu),
                })),
            });
            this.landingSaved.set(true);
            await this.loadLanding();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.actionFailed));
        } finally {
            this.landingBusy.set(false);
        }
    }

    // Uploading adds the screenshot to the end of the page's list, because that is what somebody
    // who just picked a file meant by picking it. It is not on the page until the tab is saved.
    async uploadShot(event: Event) {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        if (!file) return;
        this.landingBusy.set(true);
        this.error.set('');
        try {
            const saved = await this.api.uploadLandingShot(file);
            this.shots = [...this.shots, { file: saved.file, capEn: '', capRu: '' }];
            this.landingSaved.set(false);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.landing.uploadFailed));
        } finally {
            this.landingBusy.set(false);
        }
    }

    shotUrl(file: string): string {
        return file.startsWith('/') ? file : `/landing-media/${encodeURIComponent(file)}`;
    }

    /** The first screenshot is the hero beside the form; the rest are the gallery below it. */
    moveShot(index: number, by: -1 | 1) {
        const to = index + by;
        if (to < 0 || to >= this.shots.length) return;
        const next = [...this.shots];
        [next[index], next[to]] = [next[to], next[index]];
        this.shots = next;
        this.landingSaved.set(false);
    }

    // Two separate acts, deliberately: taking a screenshot off the page is an edit you undo by not
    // saving, while deleting the file is not.
    removeShot(index: number) {
        this.shots = this.shots.filter((_, i) => i !== index);
        this.landingSaved.set(false);
    }

    deleteShotFile(file: string) {
        return this.run(async () => {
            await this.api.deleteLandingFile(file);
            this.shots = this.shots.filter(s => s.file !== file);
            await this.loadLanding();
        });
    }

    addRoadmapColumn() {
        this.roadmapCols = [...this.roadmapCols,
            { titleEn: '', titleRu: '', mark: 'next', itemsEn: '', itemsRu: '' }];
    }

    removeRoadmapColumn(index: number) {
        this.roadmapCols = this.roadmapCols.filter((_, i) => i !== index);
    }

    addStoryStep() {
        this.storySteps = [...this.storySteps,
            { whenEn: '', whenRu: '', titleEn: '', titleRu: '', textEn: '', textRu: '' }];
    }

    removeStoryStep(index: number) {
        this.storySteps = this.storySteps.filter((_, i) => i !== index);
    }

    /** Files on disk the page no longer points at — uploaded, taken off again, still costing disk. */
    orphanFiles(): string[] {
        const used = new Set(this.shots.map(s => s.file));
        return (this.landing()?.files ?? []).filter(f => !used.has(f));
    }

    // A lapsed paid plan is the case worth flagging: the account still says Pro but behaves Free.
    isLapsed(u: AdminUser): boolean {
        return u.planTier !== 'Free' && u.effectiveTier === 'Free';
    }
}
