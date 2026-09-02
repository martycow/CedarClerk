import { Component, HostListener, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { CreditBalanceService } from '../core/credit-balance.service';
import { DebugLogService } from '../core/debug-log.service';
import { FeedbackFormService } from '../core/feedback-form.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/theme.service';
import { VersionService } from '../core/version.service';
import { PopoverComponent } from './popover.component';
import { IconComponent } from './icon.component';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';

// The account menu is where everything that is not a screen lives (ADR-239 clause 4): the profile
// pair, the account-wide screens, display preferences, the window, the development surfaces, the
// console and the way out. The trigger is the sidebar's user row or, on the rail, the avatar alone.
@Component({
    selector: 'app-account-menu',
    imports: [IconComponent, PopoverComponent, StampBadgeComponent, RouterLink],
    template: `
        <app-popover align="left">
            <button trigger class="account-trigger" [class.is-row]="face() === 'row'" [title]="t().editor.account">
                @if (auth.avatarUrl(); as url) {
                <img class="avatar avatar-img" [src]="url" alt="">
                } @else {
                <span class="avatar">{{ avatarInitial() }}</span>
                }
                @if (face() === 'row') { <span class="account-name">{{ userName() }}</span> }
            </button>
            <div panel class="account-popover">
                <div class="who">
                    @if (auth.avatarUrl(); as url) {
                    <img class="avatar avatar-img who-face" [src]="url" alt="">
                    } @else {
                    <span class="avatar who-face">{{ avatarInitial() }}</span>
                    }
                    <span class="who-text">
                        <span class="who-email" [title]="auth.userEmail()">{{ auth.userEmail() }}</span>
                        <span class="who-plan">
                            <app-stamp-badge [tone]="planTone()" [rotate]="0">{{ planLabel() }}</app-stamp-badge>
                            @if (credits(); as n) {
                                <a class="who-credits" routerLink="/settings" [queryParams]="{ tab: 'billing' }"
                                   [title]="t().shell.credits(n)">
                                    <app-icon name="drop" size="xs" />{{ n }}
                                </a>
                            }
                        </span>
                    </span>
                </div>

                <ul class="account-actions">
                    <li><a class="account-item" routerLink="/settings" [queryParams]="{ tab: 'profile' }">
                        <app-icon name="user" size="sm" />{{ t().settings.tabs.profile }}
                    </a></li>
                    <li><a class="account-item" routerLink="/settings" [queryParams]="{ tab: 'account' }">
                        <app-icon name="sparkle" size="sm" />{{ t().settings.tabs.account }}
                    </a></li>
                    <li class="account-sep" role="separator"></li>
                    <li><a class="account-item" routerLink="/glossary">
                        <app-icon name="book-bookmark" size="sm" />{{ t().glossary.crumb }}
                    </a></li>
                    <li><a class="account-item" routerLink="/presets">
                        <app-icon name="squares-four" size="sm" />{{ t().presets.crumb }}
                    </a></li>
                    @if (auth.indieDev()) {
                    <li><a class="account-item" routerLink="/teams">
                        <app-icon name="user" size="sm" />{{ t().teams.crumb }}
                    </a></li>
                    }
                    <li class="account-sep" role="separator"></li>
                    <li><button type="button" class="account-item" (click)="openAppearance.emit()">
                        <app-icon name="palette" size="sm" />{{ t().settings.appearance.title }}
                    </button></li>
                    <li><button type="button" class="account-item" (click)="theme.toggle()">
                        <app-icon [name]="theme.theme() === 'dark' ? 'sun' : 'moon'" size="sm" />{{ t().common.toggleTheme }}
                    </button></li>
                    <li><button type="button" class="account-item" (click)="toggleFullscreen()">
                        <app-icon [name]="isFullscreen() ? 'arrows-in-simple' : 'arrows-out-simple'" size="sm" />{{ fullscreenLabel() }}
                    </button></li>
                    <li class="account-sep" role="separator"></li>
                    <li><button type="button" class="account-item" (click)="feedbackForm.open.set(true)">
                        <app-icon name="chat-teardrop-dots" size="sm" />{{ t().feedbackForm.title }}
                    </button></li>
                    @if (auth.isAdmin()) {
                    <li class="account-sep" role="separator"></li>
                    <li><a class="account-item" routerLink="/admin">
                        <app-icon name="shield-check" size="sm" />{{ t().shell.admin }}
                    </a></li>
                    <li><a class="account-item" routerLink="/dev/styleguide">
                        <app-icon name="palette" size="sm" />{{ t().shell.styleguide }}
                    </a></li>
                    <li><a class="account-item" routerLink="/dev/icons">
                        <app-icon name="squares-four" size="sm" />{{ t().shell.icons }}
                    </a></li>
                    }
                    <li class="account-sep" role="separator"></li>
                    <li><button type="button" class="account-item" (click)="log.open.update(toggle)">
                        <app-icon name="terminal-window" size="sm" />{{ t().shell.debugConsole }}
                        <kbd class="account-kbd">Ctrl+\`</kbd>
                    </button></li>
                    <li><a class="account-item" href="/welcome">
                        <app-icon name="tree-evergreen" size="sm" />{{ t().shell.aboutLanding }}
                        @if (versionLabel(); as v) { <span class="account-version" [title]="t().shell.version(v)">{{ v }}</span> }
                    </a></li>
                    <li class="account-sep" role="separator"></li>
                    <li><button type="button" class="account-item is-danger" (click)="auth.logout()">
                        <app-icon name="sign-out" size="sm" />{{ t().editor.logout }}
                    </button></li>
                </ul>
            </div>
        </app-popover>
    `,
    styleUrls: ['account-menu.component.css'],
})
export class AccountMenuComponent {
    readonly face = input<'avatar' | 'row'>('avatar');
    readonly openAppearance = output<void>();

    auth = inject(AuthService);
    theme = inject(ThemeService);
    log = inject(DebugLogService);
    feedbackForm = inject(FeedbackFormService);
    t = inject(LocaleService).t;
    private readonly version = inject(VersionService);
    private readonly creditBalance = inject(CreditBalanceService);

    protected readonly toggle = (v: boolean) => !v;

    protected readonly isFullscreen = signal(!!document.fullscreenElement);

    protected readonly userName = computed(() => {
        const email = this.auth.userEmail() ?? '';
        return this.auth.authorDisplayName() || email.split('@')[0];
    });

    protected readonly versionLabel = computed(() => {
        const v = this.version.version();
        return v ? `v${v}` : '';
    });

    /** T-351 — the wallet's number, or null while there is nothing worth watching. */
    protected readonly credits = computed<number | null>(() => {
        const balance = this.creditBalance.balance();
        if (balance === null) return null;
        const tier = this.auth.planTier();
        return balance > 0 || (tier !== null && tier !== 'Free') ? balance : null;
    });

    @HostListener('document:fullscreenchange')
    protected onFullscreenChange() {
        this.isFullscreen.set(!!document.fullscreenElement);
    }

    protected fullscreenLabel(): string {
        const t = this.t().editor;
        return this.isFullscreen() ? t.exitFullscreen : t.enterFullscreen;
    }

    protected async toggleFullscreen() {
        try {
            if (document.fullscreenElement) await document.exitFullscreen();
            else await document.documentElement.requestFullscreen();
        } catch {
            // Denied by the browser (permissions policy, or not a user gesture) — the entry simply
            // does not take effect.
        }
    }

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
