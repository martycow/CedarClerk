import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { PopoverComponent } from './popover.component';
import { IconComponent } from './icon.component';

// IB9 — the avatar was a live account popover in the editor and a dead <span> on /drafts,
// /settings and /posts, which read as "the profile button doesn't work on some pages". The menu
// is the only page-chrome element with real behaviour (routing, logout, a live badge), so it
// becomes one component rather than markup copied four times.
//
// Navigation is the hook rail's (ADR-139), so this popover carries none of it: Profile, Admin
// where it applies, and Logout — the three things that are about the account rather than the app.
@Component({
    selector: 'app-account-menu',
    imports: [IconComponent, RouterLink, PopoverComponent],
    template: `
        <app-popover align="right">
            <button trigger class="account-trigger" [title]="t().editor.account">
                <!--IF1: the uploaded picture when there is one, the initial letter otherwise.-->
                @if (auth.avatarUrl(); as url) {
                <img class="avatar avatar-img" [src]="url" alt="">
                } @else {
                <span class="avatar">{{ avatarInitial() }}</span>
                }
            </button>
            <div panel class="account-popover">
                <p class="profile-email">{{ auth.userEmail() }}</p>
                <div class="popover-divider"></div>
                <!--I12: the profile half of Settings opens from here — "clicking the user" is
                where a profile belongs; the topbar's Settings button goes to the general page.-->
                <a class="account-action-btn" routerLink="/settings" [queryParams]="{ tab: 'profile' }">
                    <app-icon name="user" size="sm"></app-icon>
                    {{ t().settings.tabs.profile }}
                </a>
                <!--IF2: only rendered for an admin, and only as a shortcut — /api/admin is gated
                server-side, so hiding it here is convenience, not security.-->
                @if (auth.isAdmin()) {
                <a class="account-action-btn" routerLink="/admin">
                    <app-icon name="shield-check" size="sm"></app-icon>
                    {{ t().admin.open }}
                </a>
                }
                <button class="logout-btn" (click)="auth.logout()">
                    <app-icon name="sign-out" size="sm"></app-icon>
                    {{ t().editor.logout }}
                </button>
            </div>
        </app-popover>
    `,
    styleUrls: ['account-menu.component.css'],
})
export class AccountMenuComponent {
    auth = inject(AuthService);
    t = inject(LocaleService).t;

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }
}
