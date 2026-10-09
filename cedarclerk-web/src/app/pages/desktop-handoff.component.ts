import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ExternalAuthService } from '../core/external-auth.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthLayoutComponent } from '../shared/auth-layout.component';
import { ButtonComponent } from '../bench/forms/button.component';

// ADR-327 — the browser end of a sign-in started in the desktop app. The click is consent: a code
// minted on arrival would let any link to this page hand out the visitor's session.
@Component({
    selector: 'app-desktop-handoff',
    imports: [AuthLayoutComponent, ButtonComponent],
    templateUrl: 'desktop-handoff.component.html',
    styleUrls: ['auth-form.css'],
})
export class DesktopHandoffComponent {
    private external = inject(ExternalAuthService);
    readonly email = inject(AuthService).userEmail;
    readonly t = inject(LocaleService).t;

    /** The shell sends an unpadded base64url SHA-256; anything else did not come from it. */
    private readonly challenge = inject(ActivatedRoute).snapshot.queryParamMap.get('challenge') ?? '';
    readonly valid = /^[A-Za-z0-9_-]{43}$/.test(this.challenge);

    readonly busy = signal(false);
    readonly opened = signal(false);
    readonly failed = signal(!this.valid);

    async open(): Promise<void> {
        if (this.busy() || !this.valid) return;
        this.busy.set(true);
        const code = await this.external.desktopCode(this.challenge);
        this.busy.set(false);
        if (!code) {
            this.failed.set(true);
            return;
        }
        this.failed.set(false);
        this.opened.set(true);
        this.external.openDesktopApp(code);
    }
}
