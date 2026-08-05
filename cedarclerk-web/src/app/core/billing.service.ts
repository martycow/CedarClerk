import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type PlanId = 'pro' | 'proplus' | 'trial';

export interface BillingStatus {
    planTier: string;
    planExpiresAt: string | null;
    trialUsed: boolean;
    stripeCustomerLinked: boolean;
    providers: { stripe: boolean; telegramStars: boolean; paypal: boolean };
    prices: {
        proUsd: number; proPlusUsd: number; trialUsd: number;
        proStars: number; proPlusStars: number; trialStars: number;
    };
}

// ADR-092 — the prepaid credit wallet
export interface CreditPack { id: string; credits: number; priceUsdCents: number; priceStars: number }
export interface CreditLedgerEntry { delta: number; reason: string; createdAt: string }
export interface CreditsStatus {
    balance: number;
    xPostCost: number;
    packs: CreditPack[];
    providers: { stripe: boolean; telegramStars: boolean };
    ledger: CreditLedgerEntry[];
}

@Injectable({ providedIn: 'root' })
export class BillingService {
    private http = inject(HttpClient);

    status() {
        return firstValueFrom(this.http.get<BillingStatus>('/api/billing/status'));
    }

    // Both return a URL to redirect the browser to (hosted checkout / PayPal approval page)
    stripeCheckout(plan: PlanId) {
        return firstValueFrom(this.http.post<{ url: string }>('/api/billing/stripe/checkout', { plan }));
    }

    paypalCheckout(plan: PlanId) {
        return firstValueFrom(this.http.post<{ url: string }>('/api/billing/paypal/checkout', { plan }));
    }

    // Sends a Stars invoice link to the user's linked Telegram account
    starsInvoice(plan: PlanId) {
        return firstValueFrom(this.http.post<{ sent: boolean }>('/api/billing/telegram-stars/invoice', { plan }));
    }

    // URL to redirect the browser to (Stripe-hosted subscription management)
    stripePortal() {
        return firstValueFrom(this.http.post<{ url: string }>('/api/billing/stripe/portal', {}));
    }

    // ADR-092 — credits
    credits() {
        return firstValueFrom(this.http.get<CreditsStatus>('/api/billing/credits'));
    }

    creditsStripeCheckout(packId: string) {
        return firstValueFrom(this.http.post<{ url: string }>('/api/billing/credits/stripe/checkout', { packId }));
    }

    creditsStarsInvoice(packId: string) {
        return firstValueFrom(this.http.post<{ sent: boolean }>('/api/billing/credits/telegram-stars/invoice', { packId }));
    }
}
