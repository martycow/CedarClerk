import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { VersionService } from '../core/version.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IconComponent } from '../shared/icon.component';
import { InputComponent } from '../bench/forms/input.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';

@Component({
    selector: 'app-register',
    imports: [
        RouterLink, CedarLogoComponent, LangSwitchComponent,
        ButtonComponent, InputComponent, PaperCardComponent, IconComponent,
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
    theme = inject(ThemeService);
    // T-121 follow-up — the desktop shell has no invite codes and nowhere to get one, so the field
    // would be asking for something that does not exist. See Consts.General.OpenRegistrationCfg.
    version = inject(VersionService);
    private locale = inject(LocaleService);
    t = this.locale.t;

    email = '';
    password = '';
    inviteCode = '';
    busy = signal(false);
    error = signal('');

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
        this.busy.set(true);
        this.error.set('');
        const result = await this.auth.register(this.email, this.password, this.inviteCode, this.username());
        this.busy.set(false);
        if (result.ok) {
            // I1: the language picked on this screen becomes the account's own setting, so
            // Settings opens already holding it instead of showing an unset picker while the UI
            // is visibly in that language. Best-effort — a failure here must not block signup,
            // and localStorage already carries the choice regardless.
            try { await this.auth.saveUiLanguage(this.locale.uiLang()); } catch { /* ignore */ }
            this.router.navigateByUrl('/editor');
        } else {
            this.error.set(result.error);
        }
    }
}
