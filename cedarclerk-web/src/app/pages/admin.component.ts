import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AdminService, AdminSummary, AdminUser } from '../core/admin.service';
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
    imports: [DatePipe, RouterLink, CedarLogoComponent, AccountMenuComponent, ArrowLeft, ShieldCheck],
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

    async ngOnInit() {
        try {
            const [users, summary] = await Promise.all([this.api.listUsers(), this.api.summary()]);
            this.users.set(users);
            this.summary.set(summary);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        } finally {
            this.loading.set(false);
        }
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
