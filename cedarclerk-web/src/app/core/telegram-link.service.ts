import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface TelegramAuthData {
    id: number;
    first_name?: string;
    last_name?: string;
    username?: string;
    photo_url?: string;
    auth_date: number;
    hash: string;
}

declare global {
    interface Window {
        Telegram?: { Login: { auth(options: { bot_id: string; request_access?: string; lang?: string }, cb: (data: TelegramAuthData | false) => void): void } };
    }
}

const WIDGET_SRC = 'https://telegram.org/js/telegram-widget.js?22';

@Injectable({ providedIn: 'root' })
export class TelegramLinkService {
    private http = inject(HttpClient);
    private scriptPromise: Promise<void> | null = null;

    prepare(): Promise<void> {
        if (window.Telegram?.Login) return Promise.resolve();
        if (this.scriptPromise) return this.scriptPromise;

        this.scriptPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = WIDGET_SRC;
            script.async = true;
            const fail = () => {
                clearTimeout(timeout);
                script.remove();
                this.scriptPromise = null;
                reject(new Error('Failed to load Telegram widget script'));
            };
            const timeout = setTimeout(fail, 15000);
            script.onload = () => {
                if (!window.Telegram?.Login) { fail(); return; }
                clearTimeout(timeout);
                resolve();
            };
            script.onerror = fail;
            document.head.appendChild(script);
        });
        return this.scriptPromise;
    }

    authorizeLogin(botId: number, lang: string, callback: (data: TelegramAuthData | false) => void): boolean {
        if (!window.Telegram?.Login) throw new Error('Telegram widget unavailable');
        // Reuse the SDK's named window so blocked popups can be reported before its silent retry loop.
        const popup = window.open('', `telegram_oauth_bot${botId}`, 'width=550,height=470');
        if (!popup) return false;
        try {
            window.Telegram.Login.auth({ bot_id: String(botId), lang }, callback);
        } catch (error) {
            popup.close();
            throw error;
        }
        return true;
    }

    getConfig() {
        return firstValueFrom(this.http.get<{ botUsername: string; botId: number }>('/api/auth/telegram/config'));
    }

    botStatus() {
        return firstValueFrom(this.http.get<{ reachable: boolean; botUsername: string | null }>('/api/auth/telegram/status'));
    }

    // Opens Telegram's own login popup; resolves once the user confirms or cancels. Requires the
    // bot's domain to be registered via @BotFather /setdomain, matching the page's origin exactly —
    // will not work on localhost, only on the deployed domain.
    async link(): Promise<void> {
        const config = await this.getConfig();
        await this.prepare();

        const authData = await new Promise<TelegramAuthData | false>((resolve, reject) => {
            if (!window.Telegram?.Login) { reject(new Error('Telegram widget unavailable')); return; }
            window.Telegram.Login.auth({ bot_id: String(config.botId), request_access: 'write' }, resolve);
        });

        if (!authData) return; // user cancelled

        await firstValueFrom(this.http.post('/api/auth/telegram/link', authData));
    }

    async unlink(): Promise<void> {
        await firstValueFrom(this.http.post('/api/auth/telegram/unlink', {}));
    }
}
