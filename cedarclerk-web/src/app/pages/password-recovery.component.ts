import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Location } from '@angular/common';
import { LocaleService } from '../core/i18n/locale.service';
import { AuthLayoutComponent } from '../shared/auth-layout.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';
import { InputComponent } from '../bench/forms/input.component';
import { ButtonComponent } from '../bench/forms/button.component';

@Component({
    selector: 'app-password-recovery',
    imports: [AuthLayoutComponent, LangSwitchComponent, InputComponent, ButtonComponent, RouterLink],
    styleUrls: ['auth-form.css'],
    template: `
        <app-auth-layout>
            <section class="card auth-card" data-surface="paper">
                <div class="auth-header">
                    <h1 class="auth-title">{{ reset ? t().passwordRecovery.resetTitle : t().passwordRecovery.title }}</h1>
                    <p class="auth-tagline">{{ reset ? t().passwordRecovery.resetDescription : t().passwordRecovery.description }}</p>
                </div>
                @if (done()) {
                    <p class="success-note" role="status">{{ reset ? t().passwordRecovery.resetDone : t().passwordRecovery.sent }}</p>
                } @else if (reset && !hasToken) {
                    <p class="error-box" role="alert">{{ t().passwordRecovery.invalid }}</p>
                    <a routerLink="/forgot-password">{{ t().passwordRecovery.newLink }}</a>
                } @else {
                    <form (submit)="$event.preventDefault(); submit()">
                        @if (reset) {
                            <app-input inputId="cc-reset-password" type="password" autocomplete="new-password" [maxlength]="128"
                                [label]="t().passwordRecovery.newPassword" [hint]="t().passwordRecovery.requirements"
                                [value]="password" (valueChange)="password = $any($event) ?? ''" />
                            <app-input inputId="cc-reset-confirm" type="password" autocomplete="new-password" [maxlength]="128"
                                [label]="t().passwordRecovery.confirmPassword" [value]="confirm" (valueChange)="confirm = $any($event) ?? ''" />
                        } @else {
                            <app-input inputId="cc-recovery-email" type="email" autocomplete="email" [maxlength]="254"
                                [label]="t().login.email" placeholder="you@example.com" [value]="email" (valueChange)="email = $any($event) ?? ''" />
                        }
                        @if (error()) { <p class="error-box" role="alert">{{ error() }}</p> }
                        <app-button class="primary-btn" type="submit" variant="pine" [disabled]="busy()">
                            {{ busy() ? t().passwordRecovery.sending : reset ? t().passwordRecovery.save : t().passwordRecovery.send }}
                        </app-button>
                    </form>
                    @if (reset) { <p class="auth-footer"><a routerLink="/forgot-password">{{ t().passwordRecovery.newLink }}</a></p> }
                }
                <p class="auth-footer"><a routerLink="/login">{{ t().passwordRecovery.back }}</a></p>
                <app-lang-switch />
            </section>
        </app-auth-layout>
    `,
})
export class PasswordRecoveryComponent {
    private readonly http = inject(HttpClient);
    private readonly route = inject(ActivatedRoute);
    readonly t = inject(LocaleService).t;
    readonly reset = this.route.snapshot.data['reset'] === true;
    private readonly parameters = new URLSearchParams(this.route.snapshot.fragment ?? '');
    private readonly userId = this.parameters.get('userId');
    private readonly token = this.parameters.get('token');
    readonly hasToken = !!this.userId && !!this.token;
    readonly busy = signal(false);
    readonly done = signal(false);
    readonly error = signal('');
    email = '';
    password = '';
    confirm = '';

    constructor() {
        // Keep recovery credentials out of referrers and subsequent analytics events.
        if (this.reset) inject(Location).replaceState('/reset-password');
    }

    async submit(): Promise<void> {
        if (this.busy() || this.done()) return;
        this.error.set('');
        if (this.reset && this.password !== this.confirm) {
            this.error.set(this.t().passwordRecovery.mismatch);
            return;
        }
        if (this.reset ? this.password.length < 8 || !this.hasToken : !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email.trim())) {
            this.error.set(this.reset ? this.t().passwordRecovery.requirements : this.t().passwordRecovery.emailRequired);
            return;
        }
        this.busy.set(true);
        try {
            await firstValueFrom(this.http.post(`/api/auth/${this.reset ? 'reset-password' : 'forgot-password'}`,
                this.reset ? { userId: this.userId, token: this.token, password: this.password } : { email: this.email.trim() }));
            this.done.set(true);
            this.password = this.confirm = '';
        } catch (error: any) {
            this.error.set(error.status === 429 ? this.t().passwordRecovery.rateLimited
                : error.status === 400 && this.reset ? this.t().passwordRecovery.invalid
                : this.t().passwordRecovery.unavailable);
        } finally {
            this.busy.set(false);
        }
    }
}
