import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { httpErrorMessage } from './http-error.util';
import { LocaleService } from './i18n/locale.service';

export interface TelegramWidgetUser {
    id: number;
    first_name?: string;
    last_name?: string;
    username?: string;
    photo_url?: string;
    auth_date: number;
    hash: string;
}

export interface ExternalLogins {
    hasPassword: boolean;
    logins: { provider: string; name: string | null }[];
}

// T-003 / ADR-237. Google leaves the SPA entirely — a full-page navigation, because an OAuth round
// trip cannot happen inside XHR — while Telegram is a signed payload the widget hands us and we
// post. The two look like one button each on screen, and are nothing alike underneath.
@Injectable({ providedIn: 'root' })
export class ExternalAuthService {
    private http = inject(HttpClient);
    private locale = inject(LocaleService);

    /** Leaves the app. Nothing after this line runs. */
    startDiscord(returnUrl: string): void {
        location.href = `/api/auth/external/discord?returnUrl=${encodeURIComponent(returnUrl)}`;
    }

    startGoogle(returnUrl: string): void {
        const query = returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : '';
        location.href = `/api/auth/external/google${query}`;
    }

    /** Signs in an account that already carries this Telegram; never creates one (ADR-237 clause 4). */
    async telegram(user: TelegramWidgetUser): Promise<{ ok: true } | { ok: false; error: string }> {
        try {
            await firstValueFrom(this.http.post('/api/auth/external/telegram', {
                id: user.id,
                firstName: user.first_name ?? null,
                lastName: user.last_name ?? null,
                username: user.username ?? null,
                photoUrl: user.photo_url ?? null,
                authDate: user.auth_date,
                hash: user.hash,
            }));
            return { ok: true };
        } catch (e) {
            return { ok: false, error: httpErrorMessage(e, this.locale.t().externalAuth.failed) };
        }
    }

    /** Finishes a new account the provider vouched for. The identity itself rides the server cookie. */
    async complete(inviteCode: string, username: string): Promise<{ ok: true } | { ok: false; error: string }> {
        try {
            await firstValueFrom(this.http.post('/api/auth/external/complete', { inviteCode, username }));
            return { ok: true };
        } catch (e) {
            return { ok: false, error: httpErrorMessage(e, this.locale.t().externalAuth.failed) };
        }
    }

    /**
     * Attaches the pending provider identity to the account whose password was just accepted. Best
     * effort by design: the sign-in already succeeded, and failing to add a convenience must not
     * turn into a failed login.
     */
    async linkPending(): Promise<boolean> {
        try {
            await firstValueFrom(this.http.post('/api/auth/external/link', {}));
            return true;
        } catch {
            return false;
        }
    }

    logins(): Promise<ExternalLogins> {
        return firstValueFrom(this.http.get<ExternalLogins>('/api/auth/external/logins'));
    }

    unlink(provider: string): Promise<unknown> {
        return firstValueFrom(this.http.delete(`/api/auth/external/logins/${provider}`));
    }
}
