import { Component, DestroyRef, effect, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { ExternalAuthService, TelegramWidgetUser } from '../core/external-auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { VersionService } from '../core/version.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { BrandIconComponent } from './brand-icon.component';

// T-003 / ADR-237 — the provider row under the password form on both doors. One component rather
// than two copies: /login and /register offer exactly the same buttons, and the difference between
// signing in and signing up is decided by the server, not by which page was open.
//
// The row draws nothing when no provider is configured, so a self-hosted install with neither set
// looks exactly as it did before this existed.
@Component({
    selector: 'app-external-auth-buttons',
    imports: [ButtonComponent, BrandIconComponent],
    template: `
        @if (google() || telegramBot()) {
            <div class="providers">
                <div class="providers-rule"><span>{{ t().externalAuth.or }}</span></div>

                @if (google()) {
                    <app-button variant="paper" (clicked)="signInWithGoogle()">
                        <app-brand-icon name="google" [size]="16" />
                        {{ t().externalAuth.google }}
                    </app-button>
                }

                <!-- Telegram's widget is an iframe the script injects here; it draws its own button
                     and we cannot restyle it, which is why it sits below ours rather than beside. -->
                <div #telegramHost class="telegram-host"></div>

                @if (error()) { <div class="providers-error" role="alert">{{ error() }}</div> }
                @if (scriptFailed()) {
                    <app-button variant="paper" (clicked)="retryTelegram()">{{ t().login.retry }}</app-button>
                }
            </div>
        }
    `,
    styles: [`
        .providers { display: flex; flex-direction: column; gap: var(--space-3); margin-top: var(--space-4); }

        .providers-rule {
            display: flex; align-items: center; gap: var(--space-3);
            font-size: var(--fs-meta); color: var(--ink-3);
        }
        .providers-rule::before, .providers-rule::after {
            content: ''; flex: 1; height: 1px; background: var(--paper-edge);
        }

        .telegram-host { min-height: 40px; display: flex; justify-content: center; }
        .telegram-host:empty { display: none; }

        .providers-error { font-size: var(--fs-meta); color: var(--danger); }
    `],
})
export class ExternalAuthButtonsComponent {
    private external = inject(ExternalAuthService);
    private version = inject(VersionService);
    t = inject(LocaleService).t;

    /** Where to land after signing in; the server refuses anything that is not same-origin. */
    readonly returnUrl = input('');

    /** Telegram signs in without leaving the page, so the door decides what happens next. */
    readonly signedIn = output<void>();

    protected readonly error = signal('');
    protected readonly scriptFailed = signal(false);
    private readonly destroyRef = inject(DestroyRef);
    private callback?: (user: TelegramWidgetUser) => void;
    protected readonly google = this.version.googleAuth;
    protected readonly telegramBot = this.version.telegramBot;

    private host = viewChild<ElementRef<HTMLElement>>('telegramHost');

    constructor() {
        this.destroyRef.onDestroy(() => {
            const target = window as unknown as Record<string, unknown>;
            if (target['cedarTelegramAuth'] === this.callback) delete target['cedarTelegramAuth'];
        });
        // An effect rather than a call in the constructor: the host div lives inside the @if, so it
        // does not exist until the bot name has arrived AND the view has been rendered. The effect
        // re-runs on both, and mounts on the first pass where the two are true together.
        effect(() => {
            const bot = this.telegramBot();
            const host = this.host()?.nativeElement;
            if (bot && host && host.childElementCount === 0) this.mountTelegram(bot, host);
        });
    }

    protected signInWithGoogle(): void {
        this.external.startGoogle(this.returnUrl());
    }

    private mountTelegram(bot: string, host: HTMLElement): void {
        // data-onauth is evaluated by the widget as a global expression, so the handler has to be
        // reachable by name from window — it cannot stay a method. Only one of these components is
        // ever on screen (one door at a time), so a single global name is safe.
        const callbackName = 'cedarTelegramAuth';
        this.callback = (user: TelegramWidgetUser) => void this.onTelegramAuth(user);
        (window as unknown as Record<string, unknown>)[callbackName] = this.callback;

        const script = document.createElement('script');
        script.async = true;
        script.src = 'https://telegram.org/js/telegram-widget.js?22';
        script.setAttribute('data-telegram-login', bot);
        script.setAttribute('data-size', 'large');
        script.setAttribute('data-userpic', 'false');
        script.setAttribute('data-onauth', `${callbackName}(user)`);
        script.onerror = () => {
            this.scriptFailed.set(true);
            this.error.set(this.t().externalAuth.failed);
        };
        host.appendChild(script);
    }

    protected retryTelegram(): void {
        const host = this.host()?.nativeElement;
        const bot = this.telegramBot();
        if (!host || !bot) return;
        this.error.set('');
        this.scriptFailed.set(false);
        host.replaceChildren();
        this.mountTelegram(bot, host);
    }

    private async onTelegramAuth(user: TelegramWidgetUser): Promise<void> {
        this.error.set('');
        const result = await this.external.telegram(user);
        if (result.ok) this.signedIn.emit();
        else this.error.set(result.error);
    }
}
