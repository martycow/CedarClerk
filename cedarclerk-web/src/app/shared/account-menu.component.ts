import { Component, inject } from '@angular/core';
import { AuthService } from '../core/auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { RouterLink } from '@angular/router';
import { PopoverComponent } from './popover.component';
import { IconComponent } from './icon.component';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';

// IB9 — the avatar was a live account popover in the editor and a dead <span> on /drafts,
// /settings and /posts, which read as "the profile button doesn't work on some pages". The menu
// is the only page-chrome element with real behaviour (routing, logout, a live badge), so it
// becomes one component rather than markup copied four times.
//
// Navigation is the hook rail's (ADR-139), so this popover carries only Profile and Logout.
@Component({
    selector: 'app-account-menu',
    imports: [IconComponent, PopoverComponent, StampBadgeComponent, RouterLink],
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
                <!--Who is signed in, drawn the way the account is drawn everywhere else: the plate
                first, the address beside it, the plan under it. The address alone, centred over a
                rule and two full-width plaques, was a menu with a caption and no identity.-->
                <div class="who">
                    @if (auth.avatarUrl(); as url) {
                    <img class="avatar avatar-img who-face" [src]="url" alt="">
                    } @else {
                    <span class="avatar who-face">{{ avatarInitial() }}</span>
                    }
                    <span class="who-text">
                        <span class="who-email" [title]="auth.userEmail()">{{ auth.userEmail() }}</span>
                        <app-stamp-badge class="who-plan" [tone]="planTone()" [rotate]="0">{{ planLabel() }}</app-stamp-badge>
                    </span>
                </div>

                <ul class="account-actions">
                    <!--I12: the profile half of Settings opens from here — "clicking the user" is
                    where a profile belongs; the topbar's Settings button goes to the general page.-->
                    <li><a class="account-item" routerLink="/settings" [queryParams]="{ tab: 'profile' }">
                        <app-icon name="user" size="sm" />
                        {{ t().settings.tabs.profile }}
                    </a></li>
                    <li><a class="account-item" routerLink="/settings" [queryParams]="{ tab: 'account' }">
                        <app-icon name="sparkle" size="sm" />
                        {{ t().settings.tabs.account }}
                    </a></li>
                    <li><button type="button" class="account-item is-danger" (click)="auth.logout()">
                        <app-icon name="sign-out" size="sm" />
                        {{ t().editor.logout }}
                    </button></li>
                </ul>
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

    planLabel(): string {
        const names = this.t().plans;
        const tier = this.auth.planTier() ?? 'Free';
        return names[tier as keyof typeof names] ?? names.Free;
    }

    planTone(): 'pine' | 'brass' {
        const tier = this.auth.planTier();
        return tier === 'Pro' || tier === 'ProPlus' || tier === 'Forever' ? 'brass' : 'pine';
    }
}
