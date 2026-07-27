import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService, UiLang } from '../core/i18n/locale.service';
import { AppearanceService, ACCENT_PRESETS, AppearancePrefs } from '../core/appearance.service';
import { ToolbarLayoutService } from '../core/toolbar-layout.service';
import { TOOLBAR_GROUPS, ToolbarButtonId, ToolbarPreset, presetLayout } from '../core/toolbar-layout';
import { DragDropModule, CdkDragDrop, moveItemInArray, transferArrayItem } from '@angular/cdk/drag-drop';
import { BillingService, BillingStatus, PlanId } from '../core/billing.service';
import { TelegramLinkService } from '../core/telegram-link.service';
import { ChannelsService, Channel } from '../core/channels.service';
import { httpErrorMessage } from '../core/http-error.util';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import {
    LucideArrowLeft as ArrowLeft, LucideCheck as Check, LucideSend as Send,
    LucideAtSign as AtSign, LucideCamera as Camera, LucideThumbsUp as ThumbsUp,
    LucidePlaySquare as PlaySquare, LucideCode2 as Code2,
} from '@lucide/angular';

type PayMethod = 'stripe' | 'paypal' | 'stars';

@Component({
    selector: 'app-settings',
    imports: [FormsModule, DatePipe, RouterLink, DragDropModule, CedarLogoComponent, ArrowLeft, Check, Send, AtSign, Camera, ThumbsUp, PlaySquare, Code2],
    templateUrl: 'settings.component.html',
    styleUrls: ['settings.component.css']
})
export class SettingsComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    locale = inject(LocaleService);
    t = this.locale.t;
    appearance = inject(AppearanceService);
    toolbarLayout = inject(ToolbarLayoutService);
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

    readonly accentPresets = ACCENT_PRESETS;
    appearanceMode = signal<'light' | 'dark'>('light');
    appearanceError = signal<string | null>(null);
    languageError = signal<string | null>(null);

    readonly toolbarGroups = TOOLBAR_GROUPS;
    readonly movableToolbarGroups = TOOLBAR_GROUPS.filter(g => g.id !== 'ai');
    row1Groups = signal<string[]>([]);
    row2Groups = signal<string[]>([]);
    toolbarError = signal<string | null>(null);

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
        this.signatureText = this.auth.postSignature() ?? '';
        this.signatureUrlText = this.auth.postSignatureUrl() ?? '';
        this.authorDisplayNameText = this.auth.authorDisplayName() ?? '';
        this.profileUrlText = this.auth.profileUrl() ?? '';
        this.profileLocationText = this.auth.profileLocation() ?? '';
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
        this.initToolbarRows();
    }

    private initToolbarRows() {
        const row2 = this.toolbarLayout.layout().row2Groups;
        const ids = this.movableToolbarGroups.map(g => g.id);
        this.row2Groups.set(ids.filter(id => row2.includes(id)));
        this.row1Groups.set(ids.filter(id => !row2.includes(id)));
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

    // Appearance (ADR-035) — applies instantly and saves in the background, no Save button
    // (these are pure preference toggles that benefit from immediate visual feedback).
    activeAccentHex(): string {
        const p = this.appearance.prefs();
        return this.appearanceMode() === 'dark' ? p.accentDark : p.accentLight;
    }

    isActivePreset(hex: string): boolean {
        return this.activeAccentHex().toUpperCase() === hex.toUpperCase();
    }

    private async saveAppearance(patch: Partial<AppearancePrefs>) {
        this.appearanceError.set(null);
        try {
            await this.appearance.save(patch);
        } catch (e) {
            this.appearanceError.set(httpErrorMessage(e, this.t().settings.errors.appearance));
        }
    }

    pickAccentPreset(hex: string) {
        this.saveAppearance(this.appearanceMode() === 'dark' ? { accentDark: hex } : { accentLight: hex });
    }

    setSheetWidth(value: AppearancePrefs['sheetWidth']) {
        this.saveAppearance({ sheetWidth: value });
    }

    setTypeface(value: AppearancePrefs['typeface']) {
        this.saveAppearance({ typeface: value });
    }

    setFontSize(px: number) {
        this.saveAppearance({ fontSize: px });
    }

    setLineHeight(value: number) {
        this.saveAppearance({ lineHeight: value });
    }

    toggleAppearanceFlag(key: 'showRuler' | 'showParagraphNumbers' | 'showWordCount' | 'focusModeHideToolbar' | 'sheetFlush', ev: Event) {
        this.saveAppearance({ [key]: (ev.target as HTMLInputElement).checked });
    }

    // Toolbar customization (ADR-035) — presets set the whole layout; drag-and-drop moves whole
    // groups between rows (not individual buttons — see core/toolbar-layout.ts for why); the
    // checkbox catalog hides/shows individual buttons regardless of which row their group is in.
    async pickToolbarPreset(preset: ToolbarPreset) {
        this.toolbarError.set(null);
        try {
            await this.toolbarLayout.save(presetLayout(preset));
            this.initToolbarRows();
        } catch (e) {
            this.toolbarError.set(httpErrorMessage(e, this.t().settings.errors.toolbar));
        }
    }

    async dropToolbarGroup(event: CdkDragDrop<string[]>) {
        if (event.previousContainer === event.container) {
            moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
        } else {
            transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
        }
        this.toolbarError.set(null);
        try {
            await this.toolbarLayout.save({ ...this.toolbarLayout.layout(), preset: 'custom', row2Groups: [...this.row2Groups()] });
        } catch (e) {
            this.toolbarError.set(httpErrorMessage(e, this.t().settings.errors.toolbar));
        }
    }

    groupLabel(id: string): string {
        return this.toolbarGroups.find(g => g.id === id)?.label ?? id;
    }

    groupButtonIds(group: { buttons: { id: ToolbarButtonId }[] }): ToolbarButtonId[] {
        return group.buttons.map(b => b.id);
    }

    isButtonHidden(id: ToolbarButtonId): boolean {
        return this.toolbarLayout.layout().hiddenButtons.includes(id);
    }

    groupVisibleCount(buttonIds: ToolbarButtonId[]): number {
        return buttonIds.filter(id => !this.isButtonHidden(id)).length;
    }

    private async saveHiddenButtons(hiddenButtons: ToolbarButtonId[]) {
        this.toolbarError.set(null);
        try {
            await this.toolbarLayout.save({ ...this.toolbarLayout.layout(), preset: 'custom', hiddenButtons });
        } catch (e) {
            this.toolbarError.set(httpErrorMessage(e, this.t().settings.errors.toolbar));
        }
    }

    toggleButtonVisible(id: ToolbarButtonId, ev: Event) {
        const checked = (ev.target as HTMLInputElement).checked;
        const current = this.toolbarLayout.layout().hiddenButtons;
        this.saveHiddenButtons(checked ? current.filter(b => b !== id) : [...current, id]);
    }

    toggleGroupVisible(buttonIds: ToolbarButtonId[], ev: Event) {
        const checked = (ev.target as HTMLInputElement).checked;
        const current = this.toolbarLayout.layout().hiddenButtons;
        this.saveHiddenButtons(checked
            ? current.filter(id => !buttonIds.includes(id as ToolbarButtonId))
            : [...new Set([...current, ...buttonIds])]);
    }

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
            });
            this.authorDisplayNameText = this.auth.authorDisplayName() ?? '';
            this.profileUrlText = this.auth.profileUrl() ?? '';
            this.profileLocationText = this.auth.profileLocation() ?? '';
            this.headerSlot1 = this.auth.headerSlot1Type();
            this.headerSlot2 = this.auth.headerSlot2Type();
            this.headerSlot3 = this.auth.headerSlot3Type();
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
