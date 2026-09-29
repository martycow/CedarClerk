import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { ExternalAuthService, TelegramWidgetUser } from '../core/external-auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { VersionService } from '../core/version.service';
import { TelegramLinkService } from '../core/telegram-link.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { BrandIconComponent } from './brand-icon.component';

@Component({
    selector: 'app-external-auth-buttons',
    imports: [ButtonComponent, BrandIconComponent],
    template: `
        @if (google() || discord() || telegramBotId()) {
            <div class="providers">
                @if (google()) {
                    <app-button variant="paper" [disabled]="busy()" (clicked)="signInWithGoogle()">
                        <img src="/assets/auth/google-g.png" alt="" width="20" height="20" />{{ t().externalAuth.google }}
                    </app-button>
                }
                @if (discord()) {
                    <app-button variant="paper" [disabled]="busy()" (clicked)="signInWithDiscord()">
                        <app-brand-icon name="discord" [size]="20" />{{ t().externalAuth.discord }}
                    </app-button>
                }
                @if (telegramBotId()) {
                    <app-button variant="paper" [disabled]="loading() || busy()" (clicked)="signInWithTelegram()">
                        <app-brand-icon class="telegram-mark" name="telegram" [size]="18" />
                        {{ loading() || busy() ? t().externalAuth.loading : t().externalAuth.telegram }}
                    </app-button>
                }
                @if (error()) { <div class="providers-error" role="alert">{{ error() }}</div> }
                <div class="providers-rule"><span>{{ t().externalAuth.emailAlternative }}</span></div>
            </div>
        }
    `,
    styles: [`
        .telegram-mark { color: var(--auth-telegram); }
        .providers { display: flex; flex-direction: column; gap: var(--space-3); margin-bottom: var(--space-5); }
        .providers-rule { display: flex; align-items: center; gap: var(--space-3); margin-top: var(--space-3); font-size: var(--fs-meta); color: var(--t2); }
        .providers-rule::before, .providers-rule::after { content: ''; flex: 1; height: 1px; background: var(--border); opacity: .6; }
        .providers-error { font-size: var(--fs-ui); color: var(--danger); }
    `],
})
export class ExternalAuthButtonsComponent {
    private external = inject(ExternalAuthService);
    private destroyRef = inject(DestroyRef);
    private version = inject(VersionService);
    private telegram = inject(TelegramLinkService);
    private locale = inject(LocaleService);
    readonly t = this.locale.t;
    readonly returnUrl = input('');
    readonly signedIn = output<void>();
    protected readonly error = signal('');
    protected readonly loading = signal(false);
    protected readonly busy = signal(false);
    protected readonly google = this.version.googleAuth;
    protected readonly discord = this.version.discordAuth;
    protected readonly telegramBotId = this.version.telegramBotId;
    private ready = false;

    constructor() {
        effect(() => { if (this.telegramBotId()) void this.prepareTelegram(); });
    }

    protected signInWithDiscord(): void { this.external.startDiscord(this.returnUrl()); }

    protected signInWithGoogle(): void { this.external.startGoogle(this.returnUrl()); }

    private async prepareTelegram(): Promise<void> {
        this.loading.set(true);
        this.error.set('');
        try {
            await this.telegram.prepare();
            if (!this.destroyRef.destroyed) this.ready = true;
        } catch {
            if (!this.destroyRef.destroyed) this.error.set(this.t().externalAuth.widgetFailed);
        } finally {
            if (!this.destroyRef.destroyed) this.loading.set(false);
        }
    }

    protected signInWithTelegram(): void {
        const botId = this.telegramBotId();
        if (!botId || this.busy() || this.loading()) return;
        if (!this.ready) { void this.prepareTelegram(); return; }
        this.error.set('');
        let completed = false;
        try {
            const opened = this.telegram.authorizeLogin(botId, this.locale.uiLang(), user => {
                if (completed || this.destroyRef.destroyed) return;
                completed = true;
                if (user) void this.onTelegramAuth(user);
            });
            if (!opened) this.error.set(this.t().externalAuth.popupBlocked);
        } catch {
            this.ready = false;
            this.error.set(this.t().externalAuth.widgetFailed);
        }
    }

    private async onTelegramAuth(user: TelegramWidgetUser): Promise<void> {
        if (this.busy()) return;
        this.busy.set(true);
        const result = await this.external.telegram(user);
        if (this.destroyRef.destroyed) return;
        this.busy.set(false);
        if (result.ok) this.signedIn.emit();
        else this.error.set(result.error);
    }
}
