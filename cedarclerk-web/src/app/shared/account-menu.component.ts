import { Component, HostListener, computed, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';
import { AuthService } from '../core/auth.service';
import { CreditBalanceService } from '../core/credit-balance.service';
import { FeedbackFormService } from '../core/feedback-form.service';
import { LocaleService } from '../core/i18n/locale.service';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';
import { VersionService } from '../core/version.service';
import { IconComponent } from './icon.component';
import { PopoverComponent } from './popover.component';

@Component({
    selector: 'app-account-menu',
    imports: [IconComponent, PopoverComponent, StampBadgeComponent, RouterLink],
    template: `
        <app-popover #accountPopover placement="right-start">
            <button trigger class="account-trigger" [class.is-row]="face() === 'row'"
                    [title]="t().editor.account" aria-haspopup="dialog"
                    [attr.aria-expanded]="accountPopover.isOpen()"
                    aria-controls="account-menu-panel"
                    [attr.aria-label]="face() === 'avatar' ? t().editor.account + ' — ' + userName() : null">
                @if (auth.avatarUrl(); as url) {
                <img class="avatar avatar-img" [src]="url" alt="">
                } @else {
                <span class="avatar">{{ avatarInitial() }}</span>
                }
                @if (face() === 'row') { <span class="account-name">{{ userName() }}</span> }
            </button>

            <div panel id="account-menu-panel" class="account-popover" role="dialog"
                 [attr.aria-label]="t().editor.account">
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
                    <li><a class="account-item" routerLink="/settings">
                        <app-icon name="gear" size="sm" />{{ t().shell.settings }}
                    </a></li>
                    <li><button type="button" class="account-item" (click)="openFeedback()">
                        <app-icon name="chat-teardrop-dots" size="sm" />{{ t().feedbackForm.title }}
                    </button></li>
                    <li><button type="button" class="account-item" (click)="toggleFullscreen()">
                        <app-icon [name]="isFullscreen() ? 'arrows-in-simple' : 'arrows-out-simple'" size="sm" />
                        {{ fullscreenLabel() }}
                    </button></li>

                    @if (auth.isAdmin()) {
                    <li class="account-sep" role="separator"></li>
                    <li class="label account-label">{{ t().shell.developer }}</li>
                    <li><a class="account-item" routerLink="/admin">
                        <app-icon name="shield-check" size="sm" />{{ t().shell.admin }}
                    </a></li>
                    <li><a class="account-item" routerLink="/dev/styleguide">
                        <app-icon name="palette" size="sm" />{{ t().shell.styleguide }}
                    </a></li>
                    <li><a class="account-item" routerLink="/dev/icons">
                        <app-icon name="squares-four" size="sm" />{{ t().shell.icons }}
                    </a></li>
                    <li><button type="button" class="account-item is-quiet" (click)="toggleConsole()">
                        <app-icon name="terminal-window" size="sm" />{{ t().shell.debugConsole }}
                        <kbd class="account-kbd">Ctrl+\`</kbd>
                    </button></li>
                    }

                    <li class="account-sep" role="separator"></li>
                    <li><a class="account-item is-quiet" href="/welcome">
                        <app-icon name="tree-evergreen" size="sm" />{{ t().shell.aboutLanding }}
                        @if (versionLabel(); as v) { <span class="account-version" [title]="t().shell.version(v)">{{ v }}</span> }
                    </a></li>
                    <li><button type="button" class="account-item is-danger" (click)="logout()">
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
    protected readonly isFullscreen = signal(!!document.fullscreenElement);

    protected readonly popover = viewChild.required(PopoverComponent);
    protected readonly auth = inject(AuthService);
    protected readonly t = inject(LocaleService).t;
    private readonly feedbackForm = inject(FeedbackFormService);
    private readonly version = inject(VersionService);
    private readonly creditBalance = inject(CreditBalanceService);
    private readonly overlays = inject(OverlayCoordinatorService);

    protected readonly userName = computed(() => {
        const email = this.auth.userEmail() ?? '';
        return this.auth.authorDisplayName() || email.split('@')[0];
    });

    protected readonly versionLabel = computed(() => {
        const value = this.version.version();
        return value ? `v${value}` : '';
    });

    protected readonly credits = computed<number | null>(() => {
        const balance = this.creditBalance.balance();
        if (balance === null) return null;
        const tier = this.auth.planTier();
        return balance > 0 || (tier !== null && tier !== 'Free') ? balance : null;
    });

    protected openFeedback(): void {
        this.popover().close();
        this.feedbackForm.openForm();
    }

    @HostListener('document:fullscreenchange')
    protected onFullscreenChange(): void {
        this.isFullscreen.set(!!document.fullscreenElement);
    }

    protected fullscreenLabel(): string {
        return this.isFullscreen() ? this.t().editor.exitFullscreen : this.t().editor.enterFullscreen;
    }

    protected async toggleFullscreen(): Promise<void> {
        this.popover().close();
        try {
            if (document.fullscreenElement) await document.exitFullscreen();
            else await document.documentElement.requestFullscreen();
        } catch {
            // A browser or desktop policy may refuse fullscreen; the current shell stays usable.
        }
    }

    protected toggleConsole(): void {
        this.popover().close();
        this.overlays.toggle('debug');
    }

    protected logout(): void {
        this.popover().close();
        this.auth.logout();
    }

    protected avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }

    protected planLabel(): string {
        const names = this.t().plans;
        const tier = this.auth.planTier() ?? 'Free';
        return names[tier as keyof typeof names] ?? names.Free;
    }

    protected planTone(): 'pine' | 'brass' {
        const tier = this.auth.planTier();
        return tier === 'Pro' || tier === 'ProPlus' || tier === 'Forever' ? 'brass' : 'pine';
    }
}
