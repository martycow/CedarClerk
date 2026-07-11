import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface BillingStatus {
    planTier: string;
    providers: { stripe: boolean; telegramStars: boolean; paypal: boolean };
    starsAmount: number;
}

@Injectable({ providedIn: 'root' })
export class BillingService {
    private http = inject(HttpClient);

    status() {
        return firstValueFrom(this.http.get<BillingStatus>('/api/billing/status'));
    }

    // Returns the Stripe hosted checkout URL to redirect to
    stripeCheckout() {
        return firstValueFrom(this.http.post<{ url: string }>('/api/billing/stripe/checkout', {}));
    }

    // Sends a Stars invoice to the user's linked Telegram account
    starsInvoice() {
        return firstValueFrom(this.http.post<{ sent: boolean }>('/api/billing/telegram-stars/invoice', {}));
    }
}
