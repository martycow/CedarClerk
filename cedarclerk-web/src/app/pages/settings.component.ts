import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService, UiLang } from '../core/i18n/locale.service';
import { BillingService, BillingStatus, PlanId } from '../core/billing.service';
import { TelegramLinkService } from '../core/telegram-link.service';
import { ChannelsService, Channel } from '../core/channels.service';
import { AssetsService } from '../core/assets.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { AccountMenuComponent } from '../shared/account-menu.component';
import {
    LucideArrowLeft as ArrowLeft, LucideCheck as Check, LucideSend as Send,
    LucideAtSign as AtSign, LucideCamera as Camera, LucideThumbsUp as ThumbsUp,
    LucidePlaySquare as PlaySquare, LucideCode2 as Code2,
} from '@lucide/angular';

type PayMethod = 'stripe' | 'paypal' | 'stars';
export type SettingsTab = 'profile' | 'account';

@Component({
    selector: 'app-settings',
    imports: [FormsModule, DatePipe, RouterLink, CedarLogoComponent, AccountMenuComponent, ArrowLeft, Check, Send, AtSign, Camera, ThumbsUp, PlaySquare, Code2],
    templateUrl: 'settings.component.html',
    styleUrls: ['settings.component.css']
})
export class SettingsComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    locale = inject(LocaleService);
    t = this.locale.t;
    private route = inject(ActivatedRoute);
    private assets = inject(AssetsService);
    private billingApi = inject(BillingService);
    private telegramLink = inject(TelegramLinkService);
    private channelsApi = inject(ChannelsService);

    // Mirrors Consts.Signatures.FreeAttributionText (CedarClerk.Core) — shown so Free-tier users
    // know what's being appended in place of a custom signature.
    readonly freeAttributionText = 'Published with Cedar Clerk';

    signatureText = '';
    signatureUrlText = '';
    signatureBusy = signal(false);
    signatureSaved = signal(false);
    signatureError = signal<string | null>(null);

    authorDisplayNameText = '';
    profileUrlText = '';
    profileLocationText = '';
    // I15 — blank means the built-in wording.
    avatarBusy = signal(false);
    avatarError = signal<string | null>(null);
    blogLinkText = '';
    telegramLinkText = '';
    headerSlot1: string | null = null;
    headerSlot2: string | null = null;
    headerSlot3: string | null = null;
    profileBusy = signal(false);
    profileSaved = signal(false);
    profileError = signal<string | null>(null);

    socialTwitterUrlText = '';
    socialInstagramUrlText = '';
    socialFacebookUrlText = '';
    socialYoutubeUrlText = '';
    socialGithubUrlText = '';
    socialBusy = signal(false);
    socialSaved = signal(false);
    socialError = signal<string | null>(null);

    languageError = signal<string | null>(null);

    // I12 — two groups rather than one long scroll: "profile" is the author and what publishes
    // under their name, "account" is the machinery (language, plan, connected services). The
    // account menu deep-links to the profile half, which is what "opened by clicking the user"
    // meant; the topbar's Settings button still lands on the general page.
    tab = signal<SettingsTab>('profile');

    billing = signal<BillingStatus | null>(null);
    billingBusy = signal(false);
    billingMessage = signal<string | null>(null);
    selectedPlan: PlanId | null = null;
    payMethod: PayMethod = 'stripe';

    telegramBusy = signal(false);
    notifyBusy = signal(false);
    telegramError = signal<string | null>(null);
    askUnlinkTelegram = signal(false);

    botStatus = signal<{ reachable: boolean; botUsername: string | null } | null>(null);
    channels = signal<Channel[]>([]);

    async ngOnInit() {
        // The account menu links to /settings?tab=profile (I12).
        const requested = this.route.snapshot.queryParamMap.get('tab');
        if (requested === 'profile' || requested === 'account') this.tab.set(requested);

        this.signatureText = this.auth.postSignature() ?? '';
        this.signatureUrlText = this.auth.postSignatureUrl() ?? '';
        this.authorDisplayNameText = this.auth.authorDisplayName() ?? '';
        this.profileUrlText = this.auth.profileUrl() ?? '';
        this.profileLocationText = this.auth.profileLocation() ?? '';
        this.blogLinkText = this.auth.blogLinkText() ?? '';
        this.telegramLinkText = this.auth.telegramLinkText() ?? '';
        this.headerSlot1 = this.auth.headerSlot1Type();
        this.headerSlot2 = this.auth.headerSlot2Type();
        this.headerSlot3 = this.auth.headerSlot3Type();
        this.socialTwitterUrlText = this.auth.socialTwitterUrl() ?? '';
        this.socialInstagramUrlText = this.auth.socialInstagramUrl() ?? '';
        this.socialFacebookUrlText = this.auth.socialFacebookUrl() ?? '';
        this.socialYoutubeUrlText = this.auth.socialYoutubeUrl() ?? '';
        this.socialGithubUrlText = this.auth.socialGithubUrl() ?? '';
        try { this.billing.set(await this.billingApi.status()); } catch { /* non-critical */ }
        try { this.botStatus.set(await this.telegramLink.botStatus()); } catch { /* non-critical */ }
        try { this.channels.set(await this.channelsApi.list()); } catch { /* non-critical */ }
    }

    hasProHeaderSlot(): boolean {
        const t = this.auth.planTier();
        return t === 'Pro' || t === 'ProPlus' || t === 'Forever';
    }

    // Same Pro+ gate as hasProHeaderSlot() (PlanLimitations.HasCustomSignature server-side) — kept
    // as its own method since it reads as "can this user customize their signature", not slots.
    hasProSignature(): boolean {
        const t = this.auth.planTier();
        return t === 'Pro' || t === 'ProPlus' || t === 'Forever';
    }

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }

    channelsSummary(): string {
        return this.channels().map(c => c.title).join(', ');
    }

    // IF1 — the file goes through the ordinary asset upload first (type whitelist, storage
    // quota, public /media serving all reused), then the returned path is recorded as the avatar.
    async onAvatarPicked(ev: Event) {
        const input = ev.target as HTMLInputElement;
        const file = input.files?.[0];
        if (!file) return;
        this.avatarBusy.set(true);
        this.avatarError.set(null);
        try {
            const { url } = await this.assets.upload(file);
            await this.auth.saveAvatar(url);
        } catch (e) {
            this.avatarError.set(httpErrorMessage(e, this.t().settings.profile.avatarFailed));
        } finally {
            this.avatarBusy.set(false);
            // Lets the same file be re-picked after a failure.
            input.value = '';
        }
    }

    async clearAvatar() {
        this.avatarBusy.set(true);
        this.avatarError.set(null);
        try {
            await this.auth.saveAvatar(null);
        } catch (e) {
            this.avatarError.set(httpErrorMessage(e, this.t().settings.profile.avatarFailed));
        } finally {
            this.avatarBusy.set(false);
        }
    }

    setTab(tab: SettingsTab) {
        this.tab.set(tab);
    }

    jump(id: string) {
        document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    // Interface language (B26, ADR-044). Switches the UI immediately, then persists to the
    // profile; a failed save leaves the UI switched but says so.
    async setUiLanguage(lang: UiLang) {
        if (this.locale.uiLang() === lang) return;
        this.languageError.set(null);
        try {
            await this.auth.saveUiLanguage(lang);
        } catch (e) {
            this.languageError.set(httpErrorMessage(e, this.t().settings.language.failed));
        }
    }

    // Appearance and toolbar customization moved into AppearancePanelComponent, rendered beside
    // the writing sheet (I14/B15) — all of their state and handlers went with them.

    async saveSignature() {
        this.signatureBusy.set(true);
        this.signatureSaved.set(false);
        this.signatureError.set(null);
        try {
            await this.auth.saveSignature(this.signatureText, this.signatureUrlText);
            this.signatureText = this.auth.postSignature() ?? '';
            this.signatureUrlText = this.auth.postSignatureUrl() ?? '';
            this.signatureSaved.set(true);
            setTimeout(() => this.signatureSaved.set(false), 2500);
        } catch (e) {
            this.signatureError.set(httpErrorMessage(e, this.t().settings.errors.signature));
        } finally {
            this.signatureBusy.set(false);
        }
    }

    async saveProfile() {
        this.profileBusy.set(true);
        this.profileSaved.set(false);
        this.profileError.set(null);
        try {
            await this.auth.saveProfile({
                authorDisplayName: this.authorDisplayNameText,
                profileUrl: this.profileUrlText,
                profileLocation: this.profileLocationText,
                headerSlot1Type: this.headerSlot1,
                headerSlot2Type: this.headerSlot2,
                headerSlot3Type: this.headerSlot3,
                blogLinkText: this.blogLinkText,
                telegramLinkText: this.telegramLinkText,
            });
            this.authorDisplayNameText = this.auth.authorDisplayName() ?? '';
            this.profileUrlText = this.auth.profileUrl() ?? '';
            this.profileLocationText = this.auth.profileLocation() ?? '';
            this.headerSlot1 = this.auth.headerSlot1Type();
            this.headerSlot2 = this.auth.headerSlot2Type();
            this.headerSlot3 = this.auth.headerSlot3Type();
            this.blogLinkText = this.auth.blogLinkText() ?? '';
            this.telegramLinkText = this.auth.telegramLinkText() ?? '';
            this.profileSaved.set(true);
            setTimeout(() => this.profileSaved.set(false), 2500);
        } catch (e) {
            this.profileError.set(httpErrorMessage(e, this.t().settings.errors.headerSlots));
        } finally {
            this.profileBusy.set(false);
        }
    }

    async saveSocialLinks() {
        this.socialBusy.set(true);
        this.socialSaved.set(false);
        this.socialError.set(null);
        try {
            await this.auth.saveProfile({
                authorDisplayName: this.authorDisplayNameText,
                profileUrl: this.profileUrlText,
                profileLocation: this.profileLocationText,
                headerSlot1Type: this.headerSlot1,
                headerSlot2Type: this.headerSlot2,
                headerSlot3Type: this.headerSlot3,
                socialTwitterUrl: this.socialTwitterUrlText,
                socialInstagramUrl: this.socialInstagramUrlText,
                socialFacebookUrl: this.socialFacebookUrlText,
                socialYoutubeUrl: this.socialYoutubeUrlText,
                socialGithubUrl: this.socialGithubUrlText,
            });
            this.socialTwitterUrlText = this.auth.socialTwitterUrl() ?? '';
            this.socialInstagramUrlText = this.auth.socialInstagramUrl() ?? '';
            this.socialFacebookUrlText = this.auth.socialFacebookUrl() ?? '';
            this.socialYoutubeUrlText = this.auth.socialYoutubeUrl() ?? '';
            this.socialGithubUrlText = this.auth.socialGithubUrl() ?? '';
            this.socialSaved.set(true);
            setTimeout(() => this.socialSaved.set(false), 2500);
        } catch (e) {
            this.socialError.set(httpErrorMessage(e, this.t().settings.errors.social));
        } finally {
            this.socialBusy.set(false);
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
            this.billingMessage.set(httpErrorMessage(e, this.t().settings.errors.checkout));
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
            this.billingMessage.set(httpErrorMessage(e, this.t().settings.errors.portal));
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
            this.telegramError.set(e?.error?.error ?? e?.message ?? this.t().settings.errors.linkTelegram);
        } finally {
            this.telegramBusy.set(false);
        }
    }

    async toggleNotifyOnEngagement() {
        this.notifyBusy.set(true);
        try {
            await this.auth.saveNotificationPrefs(!this.auth.notifyOnEngagement());
        } finally {
            this.notifyBusy.set(false);
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
            this.telegramError.set(this.t().settings.errors.unlinkTelegram);
        } finally {
            this.telegramBusy.set(false);
        }
    }

    logout() {
        this.auth.logout();
    }
}
