import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { PopoverComponent } from './popover.component';
import { CountBadgeComponent } from './count-badge.component';
import { LucideLineChart as LineChart, LucideSettings as Settings, LucideLogOut as LogOut, LucideUserRound as UserRound } from '@lucide/angular';

// IB9 — the avatar was a live account popover in the editor and a dead <span> on /drafts,
// /settings and /posts, which read as "the profile button doesn't work on some pages". The menu
// is the only page-chrome element with real behaviour (routing, logout, a live badge), so it
// becomes one component rather than markup copied four times.
@Component({
    selector: 'app-account-menu',
    imports: [RouterLink, PopoverComponent, CountBadgeComponent, LineChart, Settings, LogOut, UserRound],
    template: `
        <app-popover align="right">
            <button trigger class="account-trigger" [title]="t().editor.account">
                <span class="avatar">{{ avatarInitial() }}</span>
                @if (showEmail()) { <span class="user">{{ auth.userEmail() }}</span> }
            </button>
            <div panel class="account-popover">
                <p class="profile-email">{{ auth.userEmail() }}</p>
                <div class="popover-divider"></div>
                <!--I12: the profile half of Settings opens from here — "clicking the user" is
                where a profile belongs. Shown even when showNav is false, because the topbar's
                Settings button goes to the general page, not to this.-->
                <a class="account-action-btn" routerLink="/settings" [queryParams]="{ tab: 'profile' }">
                    <svg lucideUserRound class="icon-sm"></svg>
                    {{ t().settings.tabs.profile }}
                </a>
                @if (showNav()) {
                <a class="account-action-btn" routerLink="/editor">
                    {{ t().common.backToEditor }}
                </a>
                <a class="account-action-btn" routerLink="/posts">
                    <svg lucideLineChart class="icon-sm"></svg>
                    {{ t().editor.postsManager }}
                    <app-count-badge [count]="feedback.newComments() + feedback.newReactions()" [title]="t().editor.newBadge"></app-count-badge>
                </a>
                <a class="account-action-btn" routerLink="/settings">
                    <svg lucideSettings class="icon-sm"></svg>
                    {{ t().editor.settings }}
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
    feedback = inject(CommentsService);
    t = inject(LocaleService).t;

    // Only the editor topbar has the width for it; the other headers carry a breadcrumb instead.
    showEmail = input(false);

    // False on the editor, whose topbar carries these as real buttons (I11) — showing them here
    // too would put two routes to the same page on one screen, which is what B6 objected to.
    showNav = input(true);

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }
}
