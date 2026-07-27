import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
    AdminService, AdminAuditEntry, AdminBilling, AdminInviteCode, AdminPost, AdminSummary,
    AdminUsage, AdminUser,
} from '../core/admin.service';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { AccountMenuComponent } from '../shared/account-menu.component';
import { LucideArrowLeft as ArrowLeft, LucideShieldCheck as ShieldCheck } from '@lucide/angular';

export type AdminTab = 'users' | 'invites' | 'posts' | 'reports';

// Admin panel (IF2) — all five steps of docs/admin-panel-scope.md. Users and their management,
// invite codes, a read-only cross-owner post list, billing/usage reporting, and the audit log.
@Component({
    selector: 'app-admin',
    imports: [DatePipe, FormsModule, RouterLink, CedarLogoComponent, AccountMenuComponent, ArrowLeft, ShieldCheck],
    templateUrl: 'admin.component.html',
    styleUrls: ['admin.component.css'],
})
export class AdminComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
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
    busy = signal(false);
    readonly tiers = ['Free', 'Pro', 'ProPlus', 'Forever'];
    planTier = 'Free';
    planExpiresAt = '';

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
    }

    // Payments are stored in minor units (cents/stars), like everywhere else in billing.
    formatAmount(amount: number, currency: string): string {
        return currency.toUpperCase() === 'XTR' ? `${amount} ⭐` : `${(amount / 100).toFixed(2)} ${currency.toUpperCase()}`;
    }

    isSelf(u: AdminUser): boolean {
        return u.email === this.auth.userEmail();
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

    resetTrial(u: AdminUser) {
        return this.run(() => this.api.resetTrial(u.id));
    }

    toggleLock(u: AdminUser) {
        return this.run(() => this.api.setLocked(u.id, !u.isLocked));
    }

    toggleAdmin(u: AdminUser) {
        return this.run(() => this.api.setAdmin(u.id, !u.isAdmin));
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

    // A lapsed paid plan is the case worth flagging: the account still says Pro but behaves Free.
    isLapsed(u: AdminUser): boolean {
        return u.planTier !== 'Free' && u.effectiveTier === 'Free';
    }
}
