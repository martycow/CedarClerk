import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AnalyticsService } from '../core/analytics.service';
import { ConsentService } from '../core/consent.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';

// T-153 / ADR-236 — the app's half of the consent gate; the landing has its own copy in
// LandingEndpoints because it is server-rendered and must decide before any script is emitted.
// Both write the one cookie, so answering on either side settles it for both.
//
// Mounted in the root rather than in the bench shell: /login, /register, /terms and /privacy are
// outside the shell (ADR-139), and those are exactly the pages a first-time visitor lands on.
@Component({
    selector: 'app-consent-banner',
    imports: [RouterLink, ButtonComponent, PaperCardComponent],
    template: `
        @if (asking()) {
            <div class="consent" role="dialog" aria-modal="false" [attr.aria-label]="t().consent.title">
                <app-paper-card [deckled]="false">
                    <div class="consent-body">
                        <h2>{{ t().consent.title }}</h2>
                        <p>{{ t().consent.body }}</p>
                        <a routerLink="/privacy">{{ t().consent.privacy }}</a>
                    </div>
                    <div class="consent-actions">
                        <app-button variant="paper" size="sm" (clicked)="decline()">
                            {{ t().consent.decline }}
                        </app-button>
                        <app-button variant="pine" size="sm" (clicked)="accept()">
                            {{ t().consent.accept }}
                        </app-button>
                    </div>
                </app-paper-card>
            </div>
        }
    `,
    styles: [`
        .consent {
            position: fixed;
            /* Above the drawer's own chrome, below nothing: it is the one thing that must be
               answered, and the shell reserves no room for it. */
            z-index: 60;
            left: var(--space-4);
            bottom: var(--space-4);
            width: min(28rem, calc(100vw - var(--space-4) * 2));
        }

        .consent-body {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
            padding: var(--space-4) var(--space-4) var(--space-3);
        }

        h2 {
            margin: 0;
            font-family: var(--font-display);
            font-size: var(--fs-title);
        }

        p {
            margin: 0;
            font-size: var(--fs-body);
            line-height: var(--lh-read);
        }

        .consent-actions {
            display: flex;
            justify-content: flex-end;
            gap: var(--space-2);
            padding: 0 var(--space-4) var(--space-4);
        }
    `],
})
export class ConsentBannerComponent {
    private consent = inject(ConsentService);
    private analytics = inject(AnalyticsService);
    t = inject(LocaleService).t;

    // Starts closed and opens only once the server has confirmed there is a provider configured:
    // asking for consent to something that does not exist would be a lie in a dialog.
    protected readonly configured = signal(false);

    protected asking = () => this.configured() && this.consent.state() === 'unasked';

    constructor() {
        void this.analytics.isConfigured().then(yes => this.configured.set(yes));
    }

    protected accept(): void {
        this.consent.grant();
        void this.analytics.enableIfConsented();
    }

    protected decline(): void {
        this.consent.deny();
    }
}
