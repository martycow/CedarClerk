import { Component, computed, inject, input, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';
import { AuthService } from '../core/auth.service';
import { CreditBalanceService } from '../core/credit-balance.service';
import { LocaleService } from '../core/i18n/locale.service';
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
                        <span class="who-name" [title]="userName()">{{ userName() }}</span>
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

    protected readonly popover = viewChild.required(PopoverComponent);
    protected readonly auth = inject(AuthService);
    protected readonly t = inject(LocaleService).t;
    private readonly creditBalance = inject(CreditBalanceService);

    protected readonly userName = computed(() => {
        const email = this.auth.userEmail() ?? '';
        return this.auth.authorDisplayName() || email.split('@')[0];
    });

    protected readonly credits = computed<number | null>(() => {
        const balance = this.creditBalance.balance();
        if (balance === null) return null;
        const tier = this.auth.planTier();
        return balance > 0 || (tier !== null && tier !== 'Free') ? balance : null;
    });

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
