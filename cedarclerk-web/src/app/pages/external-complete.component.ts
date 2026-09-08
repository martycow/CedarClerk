import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ExternalAuthService } from '../core/external-auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthLayoutComponent } from '../shared/auth-layout.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';

// T-003 / ADR-237 — where the Google callback lands somebody who has no account yet. The provider
// gave us a verified address and nothing else; the invite code and the account name are what it
// cannot give, and they are the same two things /register asks for.
//
// The provider identity itself is not on this screen and never travels through the browser: it sits
// in Identity's external cookie, and the server reads it back when this form is submitted.
@Component({
    selector: 'app-external-complete',
    imports: [
        AuthLayoutComponent, LangSwitchComponent,
        ButtonComponent, InputComponent,
    ],
    templateUrl: 'external-complete.component.html',
    styleUrls: ['auth-form.css'],
})
export class ExternalCompleteComponent {
    /** Consts.URLs.TenantHost — where <name> becomes an address, same as on /register. */
    private static readonly TenantHost = 'cedarclerk.app';
    private static readonly CheckDebounceMs = 400;

    private auth = inject(AuthService);
    private external = inject(ExternalAuthService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    t = inject(LocaleService).t;

    inviteCode = '';
    username = signal('');
    usernameState = signal<'idle' | 'checking' | 'free' | 'invalid' | 'reserved' | 'taken'>('idle');
    busy = signal(false);
    error = signal('');

    private checkTimer?: ReturnType<typeof setTimeout>;

    blogHost = computed(() => `${this.username() || 'name'}.${ExternalCompleteComponent.TenantHost}`);

    usernameNote = computed(() => {
        const s = this.usernameState();
        if (s === 'idle') return '';
        const dict = this.t().register;
        return {
            checking: dict.usernameChecking, free: dict.usernameFree, invalid: dict.usernameInvalid,
            reserved: dict.usernameReserved, taken: dict.usernameTaken,
        }[s];
    });

    usernameTone = computed(() => {
        const s = this.usernameState();
        if (s === 'free') return 'ok';
        return s === 'idle' || s === 'checking' ? '' : 'bad';
    });

    // Same normalisation as /register, so what can be typed is what can be registered.
    onUsernameInput(raw: string) {
        const name = raw.trim().toLowerCase().replace(/[^a-z0-9-]/g, '');
        this.username.set(name);
        clearTimeout(this.checkTimer);
        if (!name) { this.usernameState.set('idle'); return; }
        this.usernameState.set('checking');
        this.checkTimer = setTimeout(() => this.checkUsername(name), ExternalCompleteComponent.CheckDebounceMs);
    }

    private async checkUsername(name: string) {
        const answer = await this.auth.checkUsername(name);
        if (this.username() !== name) return;
        if (!answer) { this.usernameState.set('idle'); return; }
        this.usernameState.set(answer.available ? 'free' : answer.reason ?? 'invalid');
    }

    async submit() {
        if (this.busy()) return;
        this.busy.set(true);
        this.error.set('');
        const result = await this.external.complete(this.inviteCode, this.username());
        if (!result.ok) {
            this.busy.set(false);
            this.error.set(result.error);
            return;
        }

        // The session exists now, but AuthService has never asked who it belongs to — without this
        // the guard on the next route would bounce a signed-in person back to /login.
        await this.auth.refresh();
        this.busy.set(false);
        this.router.navigateByUrl(this.returnUrl() || '/');
    }

    /** Same-origin paths only, exactly as the two doors read it. */
    private returnUrl(): string {
        const url = this.route.snapshot.queryParamMap.get('returnUrl');
        return url && url.startsWith('/') && !url.startsWith('//') ? url : '';
    }
}
