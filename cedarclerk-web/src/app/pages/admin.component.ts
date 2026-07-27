import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AdminService, AdminAuditEntry, AdminSummary, AdminUser } from '../core/admin.service';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { AccountMenuComponent } from '../shared/account-menu.component';
import { LucideArrowLeft as ArrowLeft, LucideShieldCheck as ShieldCheck } from '@lucide/angular';

// Admin panel, step 1 of docs/admin-panel-scope.md: the shell plus the user list. User
// management, invite codes and cross-owner posts are steps 2–4 and land here as further tabs.
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

    private async reload() {
        try {
            const [users, summary, audit] = await Promise.all([
                this.api.listUsers(), this.api.summary(), this.api.audit(),
            ]);
            this.users.set(users);
            this.summary.set(summary);
            this.audit.set(audit);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        }
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
