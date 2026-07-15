import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { BillingService, BillingStatus, PlanId } from '../core/billing.service';
import { TelegramLinkService } from '../core/telegram-link.service';
import { ChannelsService, Channel } from '../core/channels.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import {
    LucideArrowLeft as ArrowLeft, LucideCheck as Check, LucideSend as Send,
} from '@lucide/angular';

type PayMethod = 'stripe' | 'paypal' | 'stars';

@Component({
    selector: 'app-settings',
    imports: [FormsModule, DatePipe, RouterLink, CedarLogoComponent, ArrowLeft, Check, Send],
    templateUrl: 'settings.component.html',
    styleUrls: ['settings.component.css']
})
export class SettingsComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    private billingApi = inject(BillingService);
    private telegramLink = inject(TelegramLinkService);
    private channelsApi = inject(ChannelsService);

    signatureText = '';
    signatureBusy = signal(false);
    signatureSaved = signal(false);

    billing = signal<BillingStatus | null>(null);
    billingBusy = signal(false);
    billingMessage = signal<string | null>(null);
    selectedPlan: PlanId | null = null;
    payMethod: PayMethod = 'stripe';

    telegramBusy = signal(false);
    telegramError = signal<string | null>(null);
    askUnlinkTelegram = signal(false);

    botStatus = signal<{ reachable: boolean; botUsername: string | null } | null>(null);
    channels = signal<Channel[]>([]);

    async ngOnInit() {
        this.signatureText = this.auth.postSignature() ?? '';
        try { this.billing.set(await this.billingApi.status()); } catch { /* non-critical */ }
        try { this.botStatus.set(await this.telegramLink.botStatus()); } catch { /* non-critical */ }
        try { this.channels.set(await this.channelsApi.list()); } catch { /* non-critical */ }
    }

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }

    channelsSummary(): string {
        return this.channels().map(c => c.title).join(', ');
    }

    jump(id: string) {
        document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    async saveSignature() {
        this.signatureBusy.set(true);
        this.signatureSaved.set(false);
        try {
            await this.auth.saveSignature(this.signatureText);
            this.signatureText = this.auth.postSignature() ?? '';
            this.signatureSaved.set(true);
            setTimeout(() => this.signatureSaved.set(false), 2500);
        } finally {
            this.signatureBusy.set(false);
        }
    }

    pickPlan(plan: PlanId) {
        this.selectedPlan = plan;
        this.billingMessage.set(null);
    }

    priceFor(plan: PlanId): number {
        const b = this.billing();
        if (!b) return 0;
        return plan === 'pro' ? b.prices.proUsd : plan === 'proplus' ? b.prices.proPlusUsd : b.prices.trialUsd;
    }

    async confirmUpgrade() {
        const plan = this.selectedPlan;
        if (!plan) return;

        this.billingBusy.set(true);
        this.billingMessage.set(null);
        try {
            if (this.payMethod === 'stripe') {
                const res = await this.billingApi.stripeCheckout(plan);
                window.location.href = res.url; // Stripe hosted checkout page
            } else if (this.payMethod === 'paypal') {
                const res = await this.billingApi.paypalCheckout(plan);
                window.location.href = res.url; // PayPal approval page
            } else {
                await this.billingApi.starsInvoice(plan);
                this.billingMessage.set('✓ Invoice sent to your Telegram — open the bot chat and confirm the payment there.');
                this.selectedPlan = null;
            }
        } catch (e) {
            this.billingMessage.set(httpErrorMessage(e, 'Checkout failed'));
        } finally {
            this.billingBusy.set(false);
        }
    }

    async manageStripeBilling() {
        this.billingBusy.set(true);
        this.billingMessage.set(null);
        try {
            const res = await this.billingApi.stripePortal();
            window.location.href = res.url; // Stripe-hosted subscription management page
        } catch (e) {
            this.billingMessage.set(httpErrorMessage(e, 'Could not open the billing portal'));
            this.billingBusy.set(false);
        }
    }

    async linkTelegram() {
        this.telegramBusy.set(true);
        this.telegramError.set(null);
        try {
            await this.telegramLink.link();
            await this.auth.refresh();
        } catch (e: any) {
            this.telegramError.set(e?.error?.error ?? e?.message ?? 'Failed to link Telegram account');
        } finally {
            this.telegramBusy.set(false);
        }
    }

    async unlinkTelegram() {
        this.telegramBusy.set(true);
        this.telegramError.set(null);
        try {
            await this.telegramLink.unlink();
            await this.auth.refresh();
            this.askUnlinkTelegram.set(false);
        } catch {
            this.telegramError.set('Failed to unlink Telegram account');
        } finally {
            this.telegramBusy.set(false);
        }
    }

    logout() {
        this.auth.logout();
    }
}
