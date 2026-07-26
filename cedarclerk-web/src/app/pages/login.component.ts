import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';

@Component({
    selector: 'app-login',
    imports: [FormsModule, RouterLink, CedarLogoComponent],
    templateUrl: 'login.component.html',
    styleUrls: ['login.component.css']
})
export class LoginComponent {
    private auth = inject(AuthService);
    private router = inject(Router);
    theme = inject(ThemeService);
    t = inject(LocaleService).t;

    email = '';
    password = '';
    busy = signal(false);
    error = signal('');

    async submit() {
        this.busy.set(true);
        this.error.set('');
        const ok = await this.auth.login(this.email, this.password);
        this.busy.set(false);
        ok ? this.router.navigateByUrl('/drafts')
            : this.error.set(this.t().login.failed);
    }
}