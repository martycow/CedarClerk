import { HttpClient } from '@angular/common/http';
import { Injectable, effect, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { ConsentService } from './consent.service';

interface AnalyticsConfig { key: string; host: string; }

/**
 * The SPA's half of T-153 (ADR-236). The server owns the eight events that happen behind an
 * account; this owns the two things a browser is the only witness to — where the visitor came from,
 * and the steps of the signup funnel that happen before an account exists.
 *
 * Nothing loads until consent is granted. The library is imported dynamically for that reason and
 * not only for bundle size: an unasked visitor must not have the provider's script running at all,
 * which is stronger than initialising it opted-out.
 */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
    private http = inject(HttpClient);
    private auth = inject(AuthService);
    private consent = inject(ConsentService);

    private config: AnalyticsConfig | null = null;
    private posthog: typeof import('posthog-js').default | null = null;
    private loading: Promise<void> | null = null;

    constructor() {
        // Identity follows the session rather than being read once: the funnel's whole point is that
        // the anonymous visitor and the account that appears later are the same person, and identify()
        // is what joins them. Signing out resets rather than re-identifies, or the next person at this
        // browser inherits the previous one's events.
        effect(() => {
            const id = this.auth.userId();
            const client = this.posthog;
            if (!client) return;
            if (id) client.identify(id);
            else client.reset();
        });
    }

    /** True once the server has said analytics is configured at all — the banner asks nothing otherwise. */
    async isConfigured(): Promise<boolean> {
        await this.readConfig();
        return this.config !== null;
    }

    async enableIfConsented(): Promise<void> {
        if (location.pathname === '/reset-password') return;
        if (this.consent.state() !== 'granted') return;
        await this.readConfig();
        if (!this.config || this.posthog) return;

        this.loading ??= this.load(this.config);
        await this.loading;
    }

    capture(event: string, properties?: Record<string, unknown>): void {
        if (location.pathname === '/reset-password') return;
        this.posthog?.capture(event, properties);
    }

    private async load(config: AnalyticsConfig): Promise<void> {
        const { default: posthog } = await import('posthog-js');
        posthog.init(config.key, {
            api_host: config.host,
            // The page view on load would otherwise fire before the router has resolved the first
            // route, recording every entry as the bare origin.
            capture_pageview: false,
            defaults: '2025-05-24',
        });
        this.posthog = posthog;
        const id = this.auth.userId();
        if (id) posthog.identify(id);
    }

    private async readConfig(): Promise<void> {
        if (this.config) return;
        try {
            const health = await firstValueFrom(
                this.http.get<{ analytics?: AnalyticsConfig | null }>('/api/health'));
            this.config = health.analytics ?? null;
        } catch {
            // A server that cannot be reached is not a reason to surface anything: the banner stays
            // away and no script loads, which is the same state as "not configured".
            this.config = null;
        }
    }
}
