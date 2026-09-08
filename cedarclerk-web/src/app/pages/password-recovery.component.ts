import { Component, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/theme.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { IconComponent } from '../shared/icon.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';

@Component({
    selector: 'app-password-recovery',
    imports: [RouterLink, ButtonComponent, InputComponent, IconComponent, LangSwitchComponent],
    templateUrl: 'password-recovery.component.html',
    styleUrls: ['login.component.css'],
})
export class PasswordRecoveryComponent {
    private http = inject(HttpClient);
    private route = inject(ActivatedRoute);
    readonly t = inject(LocaleService).t;
    readonly theme = inject(ThemeService);
    readonly resetting = this.route.snapshot.data['reset'] === true;
    readonly busy = signal(false);
    readonly done = signal(false);
    readonly error = signal('');
    email = '';
    password = '';
    confirmPassword = '';
    private userId = '';
    private token = '';

    constructor() {
        if (this.resetting) {
            const values = new URLSearchParams(this.route.snapshot.fragment ?? '');
            this.userId = values.get('userId') ?? '';
            this.token = values.get('token') ?? '';
            history.replaceState(history.state, '', location.pathname + location.search);
        }
    }

    get validLink(): boolean { return !!this.userId && !!this.token; }

    async submit(): Promise<void> {
        if (this.busy() || this.done()) return;
        if (this.resetting ? !this.validLink || !this.password || !this.confirmPassword : !this.email.trim()) return;
        this.error.set('');
        if (this.resetting && this.password !== this.confirmPassword) {
            this.error.set(this.t().recovery.mismatch);
            return;
        }
        this.busy.set(true);
        try {
            if (this.resetting) {
                await firstValueFrom(this.http.post('/api/auth/reset-password', {
                    userId: this.userId, token: this.token, password: this.password,
                }));
                this.password = '';
                this.confirmPassword = '';
                this.token = '';
            } else {
                await firstValueFrom(this.http.post('/api/auth/forgot-password', { email: this.email.trim() }));
            }
            this.done.set(true);
        } catch (error) {
            const status = error instanceof HttpErrorResponse ? error.status : 0;
            this.error.set(status === 429 ? this.t().recovery.tooMany
                : status === 503 ? this.t().recovery.unavailable
                : this.resetting && status === 400 ? this.t().recovery.invalid
                : this.t().recovery.failed);
        } finally {
            this.busy.set(false);
        }
    }
}
