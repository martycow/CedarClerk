import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IconComponent } from '../shared/icon.component';
import { InputComponent } from '../bench/forms/input.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';

@Component({
    selector: 'app-login',
    imports: [
        RouterLink, CedarLogoComponent, LangSwitchComponent,
        ButtonComponent, InputComponent, PaperCardComponent, IconComponent,
    ],
    templateUrl: 'login.component.html',
    styleUrls: ['login.component.css']
})
export class LoginComponent {
    auth = inject(AuthService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    theme = inject(ThemeService);
    t = inject(LocaleService).t;

    /** Where authGuard or the expiry interceptor was heading; the hub when nobody said. */
    private get returnUrl(): string {
        const url = this.route.snapshot.queryParamMap.get('returnUrl');
        // Same-origin paths only: a returnUrl is a query parameter, and anything a stranger can put
        // in one must not become somewhere this app navigates to.
        return url && url.startsWith('/') && !url.startsWith('//') ? url : '/projects';
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

    async submit() {
        this.busy.set(true);
        this.error.set('');
        const result = await this.auth.login(this.email, this.password);
        this.busy.set(false);
        if (result.ok) {
            void this.router.navigateByUrl(this.returnUrl);
            return;
        }
        // The server's own words when it could not reach the installation that holds the identity;
        // the generic "wrong email or password" only when that is actually what happened.
        this.error.set(result.error ?? this.t().login.failed);
    }
}