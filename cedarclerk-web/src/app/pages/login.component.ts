import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ExternalAuthService } from '../core/external-auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthLayoutComponent } from '../shared/auth-layout.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { ExternalAuthButtonsComponent } from '../shared/external-auth-buttons.component';

@Component({
    selector: 'app-login',
    imports: [
        RouterLink, AuthLayoutComponent, LangSwitchComponent,
        ButtonComponent, InputComponent,
        ExternalAuthButtonsComponent,
    ],
    templateUrl: 'login.component.html',
    styleUrls: ['auth-form.css']
})
export class LoginComponent {
    auth = inject(AuthService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    private external = inject(ExternalAuthService);

    // T-003 — the callback sent this person here because an account already holds the address the
    // provider vouched for. The password is what proves it is the same person (ADR-237 clause 3);
    // the link is added right after it is accepted.
    readonly externalOutcome = this.route.snapshot.queryParamMap.get('external');
    readonly linkPending = this.externalOutcome === 'link';
    t = inject(LocaleService).t;

    /** Where authGuard or the expiry interceptor was heading; the hub when nobody said. */
    private get returnUrl(): string {
        const url = this.route.snapshot.queryParamMap.get('returnUrl');
        // Same-origin paths only: a returnUrl is a query parameter, and anything a stranger can put
        // in one must not become somewhere this app navigates to.
        return url && url.startsWith('/') && !url.startsWith('//') ? url : '/projects';
    }

    /**
     * T-304 — the Register link keeps where we were heading, so somebody who followed an invitation
     * with no account yet is still holding it after signing up. Public because the template reads it.
     */
    get registerParams(): Record<string, string> {
        const url = this.returnUrl;
        return url === '/projects' ? {} : { returnUrl: url };
    }

    email = '';
    password = '';
    busy = signal(false);
    error = signal('');
    probing = signal(false);

    // guestGuard has already asked the server once by the time this page renders; this is the
    // manual retry offered when that attempt got no answer at all, not a second automatic probe.
    async retrySession() {
        this.probing.set(true);
        const outcome = await this.auth.refresh();
        this.probing.set(false);
        if (outcome === 'ok') this.router.navigateByUrl(this.returnUrl);
    }

    /** The provider buttons need the destination as a plain value, not the private accessor. */
    get externalReturnUrl(): string { return this.returnUrl; }

    /** Telegram signs in without leaving the page, so the door is what moves on afterwards. */
    async afterExternalSignIn(): Promise<void> {
        await this.auth.refresh();
        void this.router.navigateByUrl(this.returnUrl);
    }

    async submit() {
        if (this.busy()) return;
        this.busy.set(true);
        this.error.set('');
        const result = await this.auth.login(this.email, this.password);
        this.busy.set(false);
        if (result.ok) {
            // Best effort, deliberately: the sign-in already succeeded, and failing to attach a
            // convenience must not turn into a failed login.
            if (this.linkPending) await this.external.linkPending();
            void this.router.navigateByUrl(this.returnUrl);
            return;
        }
        // The server's own words when it could not reach the installation that holds the identity;
        // the generic "wrong email or password" only when that is actually what happened.
        this.error.set(result.error ?? this.t().login.failed);
    }
}