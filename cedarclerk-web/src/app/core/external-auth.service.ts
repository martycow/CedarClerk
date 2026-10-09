import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { desktopBridge } from './asset-index.service';
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

    /**
     * In a browser this leaves the app and nothing after it runs. In the desktop shell the round
     * trip happens in the system browser instead (ADR-327) and the page stays; the answer says which.
     */
    startDiscord(returnUrl: string): 'left' | 'browser' { return this.start('discord', returnUrl); }

    startGoogle(returnUrl: string): 'left' | 'browser' { return this.start('google', returnUrl); }

    private start(provider: 'google' | 'discord', returnUrl: string): 'left' | 'browser' {
        const desktop = desktopBridge();
        if (desktop?.signIn) {
            void desktop.signIn(provider, returnUrl);
            return 'browser';
        }
        const query = returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : '';
        location.href = `/api/auth/external/${provider}${query}`;
        return 'left';
    }

    /** A one-time code the desktop shell trades for this account's session (ADR-327). */
    async desktopCode(challenge: string): Promise<string | null> {
        try {
            const answer = await firstValueFrom(
                this.http.post<{ code: string }>('/api/auth/desktop/code', { challenge }));
            return answer.code;
        } catch {
            return null;
        }
    }

    /** Hands the code to the installed app. The browser asks before it opens anything. */
    openDesktopApp(code: string): void {
        location.href = `cedarclerk://auth?code=${encodeURIComponent(code)}`;
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
