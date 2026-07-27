import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';

@Component({
    selector: 'app-register',
    imports: [FormsModule, RouterLink, CedarLogoComponent, LangSwitchComponent],
    templateUrl: 'register.component.html',
    styleUrls: ['register.component.css']
})
export class RegisterComponent {
    private auth = inject(AuthService);
    private router = inject(Router);
    theme = inject(ThemeService);
    private locale = inject(LocaleService);
    t = this.locale.t;

    email = '';
    password = '';
    inviteCode = '';
    busy = signal(false);
    error = signal('');

    async submit() {
        this.busy.set(true);
        this.error.set('');
        const result = await this.auth.register(this.email, this.password, this.inviteCode);
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
