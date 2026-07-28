import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { PopoverComponent } from './popover.component';
import { LucideLogOut as LogOut, LucideUserRound as UserRound, LucideShieldCheck as ShieldCheck } from '@lucide/angular';

// IB9 — the avatar was a live account popover in the editor and a dead <span> on /drafts,
// /settings and /posts, which read as "the profile button doesn't work on some pages". The menu
// is the only page-chrome element with real behaviour (routing, logout, a live badge), so it
// becomes one component rather than markup copied four times.
//
// Header redesign (27.07.2026, docs/DECISIONS.md): every screen now carries the nav row itself
// (app-page-header / the editor topbar's own buttons, I11), so the popover no longer duplicates
// Posts/Glossary/Settings links — the old showNav input is gone, this menu is just Profile,
// Admin (if applicable) and Logout everywhere.
@Component({
    selector: 'app-account-menu',
    imports: [RouterLink, PopoverComponent, UserRound, ShieldCheck, LogOut],
    template: `
        <app-popover align="right">
            <button trigger class="account-trigger" [title]="t().editor.account">
                <!--IF1: the uploaded picture when there is one, the initial letter otherwise.-->
                @if (auth.avatarUrl(); as url) {
                <img class="avatar avatar-img" [src]="url" alt="">
                } @else {
                <span class="avatar">{{ avatarInitial() }}</span>
                }
                @if (showEmail()) { <span class="user">{{ auth.userEmail() }}</span> }
            </button>
            <div panel class="account-popover">
                <p class="profile-email">{{ auth.userEmail() }}</p>
                <div class="popover-divider"></div>
                <!--I12: the profile half of Settings opens from here — "clicking the user" is
                where a profile belongs; the topbar's Settings button goes to the general page.-->
                <a class="account-action-btn" routerLink="/settings" [queryParams]="{ tab: 'profile' }">
                    <svg lucideUserRound class="icon-sm"></svg>
                    {{ t().settings.tabs.profile }}
                </a>
                <!--IF2: only rendered for an admin, and only as a shortcut — /api/admin is gated
                server-side, so hiding it here is convenience, not security.-->
                @if (auth.isAdmin()) {
                <a class="account-action-btn" routerLink="/admin">
                    <svg lucideShieldCheck class="icon-sm"></svg>
                    {{ t().admin.open }}
                </a>
                }
                <button class="logout-btn" (click)="auth.logout()">
                    <svg lucideLogOut class="icon-sm"></svg>
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

    // Only the editor topbar has the width for it; the other headers carry a breadcrumb instead.
    showEmail = input(false);

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }
}
