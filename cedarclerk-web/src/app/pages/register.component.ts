import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AnalyticsService } from '../core/analytics.service';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { VersionService } from '../core/version.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IconComponent } from '../shared/icon.component';
import { InputComponent } from '../bench/forms/input.component';
import { ExternalAuthButtonsComponent } from '../shared/external-auth-buttons.component';

@Component({
    selector: 'app-register',
    imports: [
        RouterLink, CedarLogoComponent, LangSwitchComponent,
        ButtonComponent, InputComponent, IconComponent,
        ExternalAuthButtonsComponent,
    ],
    templateUrl: 'register.component.html',
    styleUrls: ['register.component.css']
})
export class RegisterComponent {
    /** Consts.URLs.TenantHost — where <name> becomes an address. */
    private static readonly TenantHost = 'cedarclerk.app';
    private static readonly CheckDebounceMs = 400;

    private auth = inject(AuthService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    theme = inject(ThemeService);
    // T-121 follow-up — the desktop shell has no invite codes and nowhere to get one, so the field
    // would be asking for something that does not exist. See Consts.General.OpenRegistrationCfg.
    version = inject(VersionService);
    private locale = inject(LocaleService);
    private analytics = inject(AnalyticsService);
    t = this.locale.t;

    email = '';
    password = '';
    inviteCode = '';
    busy = signal(false);
    error = signal('');

    /**
     * T-304 — an invitation is its own way in. Registration is gated by an invite code, and a
     * project or team invitation carries none, so a stranger who was invited by name used to reach
     * the login page and stop there while the owner's screen said the invitation had been sent.
     * The token in the returnUrl is that code: the server accepts a live, unspent one, and the
     * field is filled and locked so nobody has to be told to paste a URL fragment into it.
     */
    readonly invitedToken = this.readInviteToken();

    private readInviteToken(): string | null {
        const url = this.returnUrl();
        const match = /^\/(?:invite|team-invite)\/([^/?#]+)$/.exec(url);
        return match ? decodeURIComponent(match[1]) : null;
    }

    /** The provider buttons take the destination as a value. */
    get externalReturnUrl(): string { return this.returnUrl(); }

    /** Telegram signs in without leaving the page; this door moves on afterwards. */
    async afterExternalSignIn(): Promise<void> {
        await this.auth.refresh();
        void this.router.navigateByUrl(this.returnUrl() || '/');
    }

    /** Same-origin paths only, exactly as the login screen reads it. */
    private returnUrl(): string {
        const url = this.route.snapshot.queryParamMap.get('returnUrl');
        return url && url.startsWith('/') && !url.startsWith('//') ? url : '';
    }

    username = signal('');
    usernameState = signal<'idle' | 'checking' | 'free' | 'invalid' | 'reserved' | 'taken'>('idle');
    private checkTimer?: ReturnType<typeof setTimeout>;

    blogHost = computed(() => `${this.username() || 'name'}.${RegisterComponent.TenantHost}`);

    usernameNote = computed(() => {
        const s = this.usernameState();
        if (s === 'idle') return '';
        const dict = this.t().register;
        return { checking: dict.usernameChecking, free: dict.usernameFree, invalid: dict.usernameInvalid, reserved: dict.usernameReserved, taken: dict.usernameTaken }[s];
    });

    usernameTone = computed(() => {
        const s = this.usernameState();
        if (s === 'free') return 'ok';
        return s === 'idle' || s === 'checking' ? '' : 'bad';
    });

    // The same shape Usernames.IsValidFormat accepts, so what can be typed is what can be
    // registered. The server still decides; this only keeps the address preview honest.
    onUsernameInput(raw: string) {
        const name = raw.trim().toLowerCase().replace(/[^a-z0-9-]/g, '');
        this.username.set(name);
        clearTimeout(this.checkTimer);
        if (!name) { this.usernameState.set('idle'); return; }
        this.usernameState.set('checking');
        this.checkTimer = setTimeout(() => this.checkUsername(name), RegisterComponent.CheckDebounceMs);
    }

    private async checkUsername(name: string) {
        const answer = await this.auth.checkUsername(name);
        if (this.username() !== name) return;
        if (!answer) { this.usernameState.set('idle'); return; }
        this.usernameState.set(answer.available ? 'free' : answer.reason ?? 'invalid');
    }

    async submit() {
        // The one funnel step the server cannot see: signup_completed is written when the account
        // exists, so without this the people who tried and were refused are invisible, and that
        // gap is the whole point of measuring registration (docs/product/METRICS.md).
        this.analytics.capture('signup_started', { invited: this.invitedToken !== null });
        this.busy.set(true);
        this.error.set('');
        const result = await this.auth.register(this.email, this.password, this.invitedToken ?? this.inviteCode, this.username());
        this.busy.set(false);
        if (result.ok) {
            // I1: the language picked on this screen becomes the account's own setting, so
            // Settings opens already holding it instead of showing an unset picker while the UI
            // is visibly in that language. Best-effort — a failure here must not block signup,
            // and localStorage already carries the choice regardless.
            try { await this.auth.saveUiLanguage(this.locale.uiLang()); } catch { /* ignore */ }
            // Somebody who arrived holding an invitation goes back to it, not to a blank editor.
            this.router.navigateByUrl(this.returnUrl() || '/editor');
        } else {
            this.error.set(result.error);
        }
    }
}
