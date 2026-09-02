import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { AuthService } from '../core/auth.service';
import { LocaleService, UiLang } from '../core/i18n/locale.service';
import { BillingService, BillingStatus, CreditsStatus, PlanId } from '../core/billing.service';
import { groupCreditLedger } from '../core/credit-ledger';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES } from '../core/languages';
import { TelegramLinkService } from '../core/telegram-link.service';
import { ChannelsService, Channel, KnownChat } from '../core/channels.service';
import { PublishService, PublishAccount } from '../core/publish.service';
import { AssetsService } from '../core/assets.service';
import { httpErrorMessage } from '../core/http-error.util';
import { pseudoProgress } from '../core/pseudo-progress.util';
import { IconComponent } from '../shared/icon.component';
import { BrandIconComponent } from '../shared/brand-icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';
import { SpecRowComponent } from '../bench/worktop/spec-row.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';
import { PlanLockComponent } from '../shared/plan-lock.component';
import { LanguageMenuComponent, LanguageMenuItem } from '../shared/language-menu.component';
import { LocationInputComponent } from '../shared/location-input.component';
import { HintDotComponent } from '../shared/hint-dot.component';

type PayMethod = 'stripe' | 'paypal' | 'stars';
export type SettingsTab = 'profile' | 'account' | 'integrations' | 'billing';

@Component({
    selector: 'app-settings',
    imports: [
        IconComponent, FormsModule, ZonedDatePipe, BrandIconComponent,
        ButtonComponent, IndexTabsComponent, LeafTagComponent,
        SpecRowComponent, StampBadgeComponent, PlanLockComponent, LocationInputComponent, HintDotComponent,
        LanguageMenuComponent, PageHeaderComponent, EmptyStateComponent,
    ],
    templateUrl: 'settings.component.html',
    styleUrls: ['settings.component.css']
})
export class SettingsComponent implements OnInit {
    auth = inject(AuthService);
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
    // FI5 — the signature text (not the URL, which isn't language-dependent) can differ per
    // content language, same "hold every language locally, one field on screen at a time" shape
    // as the cross-link texts below.
    signatureLanguage = signal<string>(DEFAULT_PRIMARY_LANGUAGE);
    private signatureDrafts: Record<string, string> = {};

    authorDisplayNameText = '';
    profileUrlText = '';
    profileLocationText = '';
    // I15 — blank means the built-in wording.
    avatarBusy = signal(false);
    avatarError = signal<string | null>(null);
    blogLinkText = '';
    telegramLinkText = '';
    // Which language's cross-link wording the two fields above are editing. Switching reloads
    // them from whichever map holds that language.
    linkTextLanguage = signal<string>(DEFAULT_PRIMARY_LANGUAGE);
    private linkTextDrafts: Record<string, { blog: string; telegram: string }> = {};
    readonly contentLanguages = CONTENT_LANGUAGES;
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
    socialTelegramUrlText = '';
    socialThreadsUrlText = '';
    socialBlueskyUrlText = '';
    socialRedditUrlText = '';
    socialSteamUrlText = '';
    socialItchUrlText = '';

    languageError = signal<string | null>(null);

    // I12 split profile from the machinery; T-348 splits the machinery again: everything about
    // integrations and social networks is one tab, everything paid is another, and "account"
    // keeps what is neither (the UI language). The account menu still deep-links to profile.
    tab = signal<SettingsTab>('profile');
    tabItems = computed<IndexTabItem[]>(() => [
        { id: 'profile', label: this.t().settings.tabs.profile },
        { id: 'account', label: this.t().settings.tabs.account },
        { id: 'integrations', label: this.t().settings.tabs.integrations },
        { id: 'billing', label: this.t().settings.tabs.billing },
    ]);

    // The header's meta line: only what the page has already fetched, never a 0 standing in for
    // "not loaded yet" (ADR-239 clause 6).
    headerMeta = computed<HeaderMeta[]>(() => {
        const t = this.t().settings;
        const meta: HeaderMeta[] = [];
        const tier = this.auth.planTier();
        if (tier) {
            const word = tier === 'ProPlus' || tier === 'Forever' ? t.subscription.planProPlus
                : tier === 'Pro' ? t.subscription.planPro : tier;
            meta.push({ text: `${word} ${t.subscription.planSuffix}`, tag: true, tone: tier === 'Free' ? 'muted' : 'ok' });
        }
        const handle = this.auth.telegramUsername();
        if (this.auth.telegramLinked() && handle) meta.push({ text: `@${handle}`, title: t.integrations.telegramAccount });
        if (this.channels().length) meta.push({ text: t.meta.channels(this.channels().length) });
        const networks = [this.blueskyAccount(), this.xAccount(), this.discordAccount()].filter(Boolean).length;
        if (networks) meta.push({ text: t.meta.networks(networks) });
        const credits = this.credits();
        if (credits) meta.push({ text: t.meta.credits(credits.balance), title: t.credits.balanceLabel });
        if (this.profileSaved()) meta.push({ text: t.saved, tag: true, tone: 'ok' });
        return meta;
    });

    billing = signal<BillingStatus | null>(null);
    billingBusy = signal(false);
    billingMessage = signal<string | null>(null);
    selectedPlan: PlanId | null = null;
    payMethod: PayMethod = 'stripe';

    // ADR-092 — the credit wallet. Same shape as the plan purchase above: pick a pack, pick a
    // method, go. Stars has no redirect, so its confirmation is a message rather than a page.
    credits = signal<CreditsStatus | null>(null);
    readonly ledgerPageSize = 8;
    ledgerPage = signal(1);
    ledgerGroups = computed(() => groupCreditLedger(this.credits()?.ledger ?? []));
    ledgerPages = computed(() => Math.max(1, Math.ceil(this.ledgerGroups().length / this.ledgerPageSize)));
    ledgerPageNumber = computed(() => Math.min(this.ledgerPage(), this.ledgerPages()));
    ledgerRows = computed(() => {
        const start = (this.ledgerPageNumber() - 1) * this.ledgerPageSize;
        return this.ledgerGroups().slice(start, start + this.ledgerPageSize);
    });
    creditsBusy = signal(false);
    creditsMessage = signal<string | null>(null);
    creditsError = signal<string | null>(null);
    selectedPackId: string | null = null;
    /** ADR-189 — a bare number of credits. Set, it is what gets bought and no pack is picked. */
    customCredits: number | null = null;
    creditsPayMethod: 'stripe' | 'stars' = 'stripe';

    telegramBusy = signal(false);
    notifyBusy = signal(false);
    telegramError = signal<string | null>(null);
    askUnlinkTelegram = signal(false);

    botStatus = signal<{ reachable: boolean; botUsername: string | null } | null>(null);
    channels = signal<Channel[]>([]);

    // ─── ADR-095: every publishing account connects here ──────────────────────────────────────
    // The export window used to own all three connect flows. It now only picks between what this
    // section has connected, so everything below moved in from `editor.component.ts` unchanged in
    // behaviour — the same endpoints, the same errors, a different screen.
    private publishApi = inject(PublishService);
    knownChats = signal<KnownChat[]>([]);
    knownChatsRefreshing = signal(false);
    manualChannelOpen = signal(false);
    newChannelChatId = '';
    channelBusy = signal(false);
    channelError = signal<string | null>(null);

    blueskyAccount = signal<PublishAccount | null>(null);
    blueskyHandle = '';
    blueskyAppPassword = '';
    blueskyBusy = signal(false);
    blueskyError = signal<string | null>(null);

    xAccount = signal<PublishAccount | null>(null);
    xBusy = signal(false);
    xError = signal<string | null>(null);

    discordAccount = signal<PublishAccount | null>(null);
    discordWebhookUrl = '';
    discordBusy = signal(false);
    discordError = signal<string | null>(null);
    /** Set by the OAuth callback's `?x=` (ADR-095) — the only sign the round trip finished. */
    xNotice = signal<'connected' | 'error' | null>(null);

    // Not connectable yet, and named rather than hidden: "planned" is an answer, an empty screen
    // is not. Moved out of the export window, where six greyed-out rows sat beside live ones.
    readonly plannedNetworks = ['Threads', 'Facebook', 'Medium', 'Patreon', 'Notion', 'Google Docs'];

    // T-002
    confirmBusy = signal(false);
    confirmSent = signal(false);

    async resendConfirmation() {
        this.confirmBusy.set(true);
        try {
            await this.auth.resendConfirmation();
            this.confirmSent.set(true);
            setTimeout(() => this.confirmSent.set(false), 4000);
        } catch { /* the banner stays; there is nothing else to say */ }
        finally { this.confirmBusy.set(false); }
    }

    async ngOnInit() {
        // The account menu links to /settings?tab=profile (I12).
        const requested = this.route.snapshot.queryParamMap.get('tab');
        if (requested === 'profile' || requested === 'account' || requested === 'integrations' || requested === 'billing') this.tab.set(requested);

        // Coming back from the confirmation link: refresh so the banner disappears rather than
        // waiting for the next full load to notice.
        if (this.route.snapshot.queryParamMap.get('confirmed') === 'yes') await this.auth.refresh();

        this.signatureUrlText = this.auth.postSignatureUrl() ?? '';
        this.loadSignatureTexts();
        this.authorDisplayNameText = this.auth.authorDisplayName() ?? '';
        this.profileUrlText = this.auth.profileUrl() ?? '';
        this.profileLocationText = this.auth.profileLocation() ?? '';
        this.loadLinkTexts();
        this.headerSlot1 = this.auth.headerSlot1Type();
        this.headerSlot2 = this.auth.headerSlot2Type();
        this.headerSlot3 = this.auth.headerSlot3Type();
        this.socialTwitterUrlText = this.auth.socialTwitterUrl() ?? '';
        this.socialInstagramUrlText = this.auth.socialInstagramUrl() ?? '';
        this.socialFacebookUrlText = this.auth.socialFacebookUrl() ?? '';
        this.socialYoutubeUrlText = this.auth.socialYoutubeUrl() ?? '';
        this.socialGithubUrlText = this.auth.socialGithubUrl() ?? '';
        this.socialTelegramUrlText = this.auth.socialTelegramUrl() ?? '';
        this.socialThreadsUrlText = this.auth.socialThreadsUrl() ?? '';
        this.socialBlueskyUrlText = this.auth.socialBlueskyUrl() ?? '';
        this.socialRedditUrlText = this.auth.socialRedditUrl() ?? '';
        this.socialSteamUrlText = this.auth.socialSteamUrl() ?? '';
        this.socialItchUrlText = this.auth.socialItchUrl() ?? '';
        this.socialTelegramUrlText = this.auth.socialTelegramUrl() ?? '';
        this.socialThreadsUrlText = this.auth.socialThreadsUrl() ?? '';
        this.socialBlueskyUrlText = this.auth.socialBlueskyUrl() ?? '';
        this.socialRedditUrlText = this.auth.socialRedditUrl() ?? '';
        this.socialSteamUrlText = this.auth.socialSteamUrl() ?? '';
        this.socialItchUrlText = this.auth.socialItchUrl() ?? '';
        try { this.billing.set(await this.billingApi.status()); } catch { /* non-critical */ }
        try { this.credits.set(await this.billingApi.credits()); } catch { /* non-critical */ }
        try { this.botStatus.set(await this.telegramLink.botStatus()); } catch { /* non-critical */ }
        try { this.channels.set(await this.channelsApi.list()); } catch { /* non-critical */ }
        try { this.knownChats.set(await this.channelsApi.listKnown()); } catch { /* non-critical */ }
        await this.loadPublishAccounts();

        // X's OAuth callback lands here (ADR-095). Landing silently on the editor is what made the
        // previous round trip look like it had done nothing at all.
        const x = this.route.snapshot.queryParamMap.get('x');
        if (x === 'connected' || x === 'error') {
            this.tab.set('integrations');
            this.xNotice.set(x);
            setTimeout(() => this.jump('sec-integrations'));
        }
    }

    private async loadPublishAccounts() {
        try {
            const networks = await this.publishApi.networks();
            this.blueskyAccount.set(networks.find(n => n.network === 'bluesky')?.accounts[0] ?? null);
            this.xAccount.set(networks.find(n => n.network === 'x')?.accounts[0] ?? null);
            this.discordAccount.set(networks.find(n => n.network === 'discord')?.accounts[0] ?? null);
        } catch {
            this.blueskyAccount.set(null);
            this.xAccount.set(null);
            this.discordAccount.set(null);
        }
    }

    /** Called with an id from the "chats the bot is in" list, or without one for the manual field. */
    async connectChannel(chatId?: string) {
        const id = (chatId ?? this.newChannelChatId).trim();
        if (!id) return;
        this.channelBusy.set(true);
        this.channelError.set(null);
        try {
            await this.channelsApi.connect(id);
            this.channels.set(await this.channelsApi.list());
            this.newChannelChatId = '';
        } catch (e) {
            this.channelError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.channelBusy.set(false);
        }
    }

    // ─── Per-channel signature (Wave 2 item 11) ───────────────────────────────────────────────
    // Behind a per-row disclosure: a channel with its own signature overrides the profile-level
    // one on every send to it; empty fields clear the override and the profile signature returns.
    signatureOpenId = signal<string | null>(null);
    sigText = '';
    sigUrl = '';
    sigBusy = signal(false);
    sigError = signal<string | null>(null);
    sigSavedId = signal<string | null>(null);
    private sigSavedTimer?: ReturnType<typeof setTimeout>;

    toggleSignature(c: Channel) {
        if (this.signatureOpenId() === c.id) { this.signatureOpenId.set(null); return; }
        this.sigText = c.postSignature ?? '';
        this.sigUrl = c.postSignatureUrl ?? '';
        this.sigError.set(null);
        this.signatureOpenId.set(c.id);
    }

    async saveChannelSignature(c: Channel) {
        if (this.sigBusy()) return;
        this.sigBusy.set(true);
        this.sigError.set(null);
        try {
            // The PATCH replaces the whole trio: the stored translations blob rides along
            // unchanged (this editor has no translations field), or a null one would wipe it.
            // A blank text clears everything server-side, translations and URL included.
            const res = await this.channelsApi.setSignature(
                c.id, this.sigText.trim(), this.sigUrl.trim(), c.postSignatureTranslationsJson ?? null);
            this.channels.update(list => list.map(x => x.id === c.id
                ? {
                    ...x,
                    postSignature: res.postSignature,
                    postSignatureTranslationsJson: res.postSignatureTranslationsJson,
                    postSignatureUrl: res.postSignatureUrl,
                }
                : x));
            // The fields mirror what is now stored — after a clearing save the URL empties too.
            this.sigText = res.postSignature ?? '';
            this.sigUrl = res.postSignatureUrl ?? '';
            this.sigSavedId.set(c.id);
            clearTimeout(this.sigSavedTimer);
            this.sigSavedTimer = setTimeout(() => this.sigSavedId.set(null), 2000);
        } catch (e) {
            // 403 (free tier setting a signature) and 400 (oversized translations) both answer
            // { error } with the server's localized wording — surfaced verbatim.
            this.sigError.set(httpErrorMessage(e, this.t().settings.integrations.signatureFailed));
        } finally {
            this.sigBusy.set(false);
        }
    }

    // Disconnecting a channel does not touch anything already published to it — the posts stay,
    // the bot simply stops being able to send new ones from here.
    async removeChannel(id: string) {
        this.channelBusy.set(true);
        this.channelError.set(null);
        try {
            await this.channelsApi.remove(id);
            this.channels.set(await this.channelsApi.list());
        } catch (e) {
            this.channelError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.channelBusy.set(false);
        }
    }

    // There is no "list my chats" Bot API call — the cache is built from `my_chat_member` updates,
    // so this asks the server to re-read what the bot has been told (see .claude/rules).
    async refreshKnownChats() {
        this.knownChatsRefreshing.set(true);
        try {
            await this.channelsApi.refreshKnown();
            this.knownChats.set(await this.channelsApi.listKnown());
        } catch (e) {
            this.channelError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.knownChatsRefreshing.set(false);
        }
    }

    /** Chats the bot knows about that are not already connected — the rest would be duplicates. */
    connectableChats(): KnownChat[] {
        const taken = new Set(this.channels().map(c => c.telegramChatId));
        return this.knownChats().filter(k => !taken.has(k.telegramChatId));
    }

    async connectBluesky() {
        this.blueskyBusy.set(true);
        this.blueskyError.set(null);
        try {
            await this.publishApi.connectBluesky(this.blueskyHandle.trim(), this.blueskyAppPassword.trim());
            // Cleared on success only: a wrong password should stay in the field to be corrected.
            this.blueskyAppPassword = '';
            await this.loadPublishAccounts();
        } catch (e) {
            this.blueskyError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.blueskyBusy.set(false);
        }
    }

    async disconnectBluesky(targetId: string) {
        this.blueskyBusy.set(true);
        try {
            await this.publishApi.disconnect(targetId);
            this.blueskyAccount.set(null);
        } catch (e) {
            this.blueskyError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.blueskyBusy.set(false);
        }
    }

    async connectDiscord() {
        this.discordBusy.set(true);
        this.discordError.set(null);
        try {
            await this.publishApi.connectDiscord(this.discordWebhookUrl.trim());
            // Cleared on success only, same as the Bluesky password: a mistyped URL stays editable.
            this.discordWebhookUrl = '';
            await this.loadPublishAccounts();
        } catch (e) {
            this.discordError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.discordBusy.set(false);
        }
    }

    async disconnectDiscord(targetId: string) {
        this.discordBusy.set(true);
        try {
            await this.publishApi.disconnect(targetId);
            this.discordAccount.set(null);
        } catch (e) {
            this.discordError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.discordBusy.set(false);
        }
    }

    // OAuth, not credentials: the browser leaves for x.com and comes back through the server's
    // callback — so busy is deliberately not reset on success, the page is navigating away.
    async connectX() {
        this.xBusy.set(true);
        this.xError.set(null);
        this.xNotice.set(null);
        try {
            const { url } = await this.publishApi.connectX();
            window.location.href = url;
        } catch (e) {
            this.xError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
            this.xBusy.set(false);
        }
    }

    async disconnectX(targetId: string) {
        this.xBusy.set(true);
        try {
            await this.publishApi.disconnect(targetId);
            this.xAccount.set(null);
            this.xNotice.set(null);
        } catch (e) {
            this.xError.set(httpErrorMessage(e, this.t().settings.errors.connectChannel));
        } finally {
            this.xBusy.set(false);
        }
    }

    hasProHeaderSlot(): boolean {
        const t = this.auth.planTier();
        return t === 'Pro' || t === 'ProPlus' || t === 'Forever';
    }

    // Same Pro gate as hasProHeaderSlot() (PlanLimitations.HasCustomSignature server-side) — kept
    // as its own method since it reads as "can this user customize their signature", not slots.
    hasProSignature(): boolean {
        const t = this.auth.planTier();
        return t === 'Pro' || t === 'ProPlus' || t === 'Forever';
    }

    // The AI gate (PlanLimitations.HasAiFeatures server-side) — narrower than the Pro one: the
    // translate buttons used to take hasProSignature() and let a Pro click into a server refusal.
    hasAiPlan(): boolean {
        return this.auth.hasAiPlan();
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

    /** T-348 — the X credits note lives on the Integrations tab; the wallet lives on Billing. */
    goToCredits() {
        this.tab.set('billing');
        setTimeout(() => this.jump('sec-credits'));
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

    // Auto-translate for the per-language profile texts (Marty's ask). Everything else that
    // is per-language in this product could already be translated in one press; these three could
    // not, and they are the ones an author sees in every post.
    translatingTexts = signal(false);
    translateTextsError = signal('');
    translateTextsDone = signal(false);
    // Same asymptotic pseudo-progress every other AI operation in the app uses (ADR-038): neither
    // provider streams, so there is no real progress to report, and a curve that slows down reads
    // as alive where a spinner reads as stuck. Capped at 90 until the response actually lands.
    translateProgress = signal(0);
    translateElapsed = signal(0);
    private translateTimer: ReturnType<typeof setInterval> | null = null;

    /**
     * @param targets which languages to fill. Defaults to every other content language; the
     * per-language button passes exactly one, which is the common case when one translation came
     * back wrong and only it needs redoing.
     */
    async translateProfileTexts(targets?: string[], sourceOverride?: string) {
        if (this.translatingTexts()) return;
        const source = sourceOverride ?? this.signatureLanguage();
        const chosen = (targets ?? this.contentLanguages.filter(l => l !== source)).filter(l => l !== source);
        if (chosen.length === 0) return;

        this.translatingTexts.set(true);
        this.translateTextsError.set('');
        this.translateTextsDone.set(false);
        this.translateProgress.set(0);
        this.translateElapsed.set(0);
        const startedAt = Date.now();
        this.translateTimer = setInterval(() => {
            const elapsed = (Date.now() - startedAt) / 1000;
            this.translateElapsed.set(Math.round(elapsed));
            this.translateProgress.set(pseudoProgress(elapsed));
        }, 250);

        try {
            await this.auth.translateProfileTexts(source, chosen);
            this.translateProgress.set(100);
            // Reload both maps from the signals the service just refreshed. The previous version
            // called setSignatureLanguage(source), which returns immediately when the language has
            // not changed — so the request succeeded and the fields never moved.
            this.loadSignatureTexts();
            this.loadLinkTexts();
            this.translateTextsDone.set(true);
            setTimeout(() => this.translateTextsDone.set(false), 2500);
        } catch (e) {
            this.translateTextsError.set(httpErrorMessage(e, this.t().settings.profile.translateFailed));
        } finally {
            if (this.translateTimer) clearInterval(this.translateTimer);
            this.translateTimer = null;
            this.translatingTexts.set(false);
        }
    }

    /**
     * "Translate into the language I am looking at", always FROM the primary one. Without a fixed
     * source this button would have to translate the selected language into itself, and picking a
     * source silently would be the kind of guess that produces a wrong translation nobody ordered.
     * Hidden while the primary language is selected: there is nothing to translate into it.
     */
    canTranslateCurrent(): boolean {
        return this.signatureLanguage() !== DEFAULT_PRIMARY_LANGUAGE;
    }

    translateCurrentLanguage() {
        return this.translateProfileTexts([this.signatureLanguage()], DEFAULT_PRIMARY_LANGUAGE);
    }

    async saveSignature() {
        this.stashSignatureText();
        this.signatureBusy.set(true);
        this.signatureSaved.set(false);
        this.signatureError.set(null);
        try {
            await this.auth.saveSignature(
                this.signatureDrafts[DEFAULT_PRIMARY_LANGUAGE] ?? '', this.signatureUrlText, this.signatureTextMap());
            this.signatureUrlText = this.auth.postSignatureUrl() ?? '';
            this.loadSignatureTexts();
            this.signatureSaved.set(true);
            setTimeout(() => this.signatureSaved.set(false), 2500);
        } catch (e) {
            this.signatureError.set(httpErrorMessage(e, this.t().settings.errors.signature));
        } finally {
            this.signatureBusy.set(false);
        }
    }

    private loadSignatureTexts() {
        this.signatureDrafts = {};
        for (const lang of CONTENT_LANGUAGES) {
            this.signatureDrafts[lang] = lang === DEFAULT_PRIMARY_LANGUAGE
                ? this.auth.postSignature() ?? ''
                : this.auth.postSignatureTexts()[lang] ?? '';
        }
        this.signatureText = this.signatureDrafts[this.signatureLanguage()] ?? '';
    }

    private stashSignatureText() {
        this.signatureDrafts[this.signatureLanguage()] = this.signatureText;
    }

    setSignatureLanguage(lang: string) {
        if (lang === this.signatureLanguage()) return;
        this.stashSignatureText();
        this.signatureLanguage.set(lang);
        this.signatureText = this.signatureDrafts[lang] ?? '';
    }

    // T-350 — the language menu draws a dot on the languages that already carry text, which is
    // read off the local drafts rather than the server: a language typed into but not yet saved
    // is one the author expects to see marked.
    signatureLanguageItems(): LanguageMenuItem[] {
        return CONTENT_LANGUAGES.map(code => ({
            code,
            hasContent: (this.signatureDrafts[code] ?? '').trim().length > 0,
        }));
    }

    linkTextLanguageItems(): LanguageMenuItem[] {
        return CONTENT_LANGUAGES.map(code => {
            const draft = this.linkTextDrafts[code];
            return {
                code,
                hasContent: !!(draft?.blog.trim() || draft?.telegram.trim()),
            };
        });
    }

    private signatureTextMap(): Record<string, string> {
        const map: Record<string, string> = {};
        for (const lang of CONTENT_LANGUAGES) {
            if (lang === DEFAULT_PRIMARY_LANGUAGE) continue;
            const value = this.signatureDrafts[lang]?.trim();
            if (value) map[lang] = value;
        }
        return map;
    }

    // Every language is held locally; the two visible fields are just whichever one is selected.
    // (An earlier version of this method called itself on the primary-language branch — infinite
    // recursion, which is what made clicking a language look like it did nothing at all.)
    private loadLinkTexts() {
        this.linkTextDrafts = {};
        for (const lang of CONTENT_LANGUAGES) {
            this.linkTextDrafts[lang] = lang === DEFAULT_PRIMARY_LANGUAGE
                ? { blog: this.auth.blogLinkText() ?? '', telegram: this.auth.telegramLinkText() ?? '' }
                : { blog: this.auth.blogLinkTexts()[lang] ?? '', telegram: this.auth.telegramLinkTexts()[lang] ?? '' };
        }
        this.showLinkTexts(this.linkTextLanguage());
    }

    private showLinkTexts(lang: string) {
        const draft = this.linkTextDrafts[lang] ?? { blog: '', telegram: '' };
        this.blogLinkText = draft.blog;
        this.telegramLinkText = draft.telegram;
    }

    private stashLinkTexts() {
        this.linkTextDrafts[this.linkTextLanguage()] = {
            blog: this.blogLinkText,
            telegram: this.telegramLinkText,
        };
    }

    // Purely local: every language goes to the server together when Save is pressed, so clicking
    // through the languages never fires a request and can never half-save.
    setLinkTextLanguage(lang: string) {
        if (lang === this.linkTextLanguage()) return;
        this.stashLinkTexts();
        this.linkTextLanguage.set(lang);
        this.showLinkTexts(lang);
    }

    private linkTextMap(which: 'blog' | 'telegram'): Record<string, string> {
        const map: Record<string, string> = {};
        for (const lang of CONTENT_LANGUAGES) {
            if (lang === DEFAULT_PRIMARY_LANGUAGE) continue;
            const value = this.linkTextDrafts[lang]?.[which]?.trim();
            if (value) map[lang] = value;
        }
        return map;
    }

    // ONE save for the whole Profile tab.
    //
    // /api/auth/profile takes the entire profile in a single request, but this page used to send
    // it from two buttons with different subsets of the fields: the header-slots button omitted
    // the social URLs, and the social button omitted the cross-link wording. Each therefore wrote
    // null over whatever the other one owned, so saving one section silently wiped the other.
    // That was true before the per-language cross-links existed; making the language switcher
    // save on every click just turned an occasional loss into a constant one.
    async saveProfile() {
        // What is on screen belongs to the selected language and has to join the rest before the
        // request is built.
        this.stashLinkTexts();
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
                socialTwitterUrl: this.socialTwitterUrlText,
                socialInstagramUrl: this.socialInstagramUrlText,
                socialFacebookUrl: this.socialFacebookUrlText,
                socialYoutubeUrl: this.socialYoutubeUrlText,
                socialGithubUrl: this.socialGithubUrlText,
                socialTelegramUrl: this.socialTelegramUrlText,
                socialThreadsUrl: this.socialThreadsUrlText,
                socialBlueskyUrl: this.socialBlueskyUrlText,
                socialRedditUrl: this.socialRedditUrlText,
                socialSteamUrl: this.socialSteamUrlText,
                socialItchUrl: this.socialItchUrlText,
                blogLinkText: this.linkTextDrafts[DEFAULT_PRIMARY_LANGUAGE]?.blog ?? '',
                telegramLinkText: this.linkTextDrafts[DEFAULT_PRIMARY_LANGUAGE]?.telegram ?? '',
                blogLinkTexts: this.linkTextMap('blog'),
                telegramLinkTexts: this.linkTextMap('telegram'),
            });
            this.readBackProfile();
            this.profileSaved.set(true);
            setTimeout(() => this.profileSaved.set(false), 2500);
        } catch (e) {
            // Was `errors.headerSlots` — the fallback of the button that happened to send the
            // request, which mislabelled every failure as a header-slot problem.
            this.profileError.set(httpErrorMessage(e, this.t().settings.errors.profile));
        } finally {
            this.profileBusy.set(false);
        }
    }

    private readBackProfile() {
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
        this.loadLinkTexts();
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
                this.billingMessage.set('Invoice sent to your Telegram — open the bot chat and confirm the payment there.');
                this.selectedPlan = null;
            }
        } catch (e) {
            this.billingMessage.set(httpErrorMessage(e, this.t().settings.errors.checkout));
        } finally {
            this.billingBusy.set(false);
        }
    }

    pickPack(id: string) {
        this.selectedPackId = id;
        this.customCredits = null;
        this.creditsMessage.set(null);
        this.creditsError.set(null);
    }

    /** Picking an amount unpicks the pack: one order at a time, and the price says which. */
    pickCustomCredits(value: number | null) {
        this.customCredits = value && value > 0 ? Math.floor(value) : null;
        if (this.customCredits) this.selectedPackId = null;
        this.creditsMessage.set(null);
        this.creditsError.set(null);
    }

    /** Whether what is typed is something the server will sell. */
    customCreditsValid(): boolean {
        const c = this.credits();
        const n = this.customCredits;
        return !!c && !!n && n >= c.minCustomCredits && n <= c.maxCustomCredits;
    }

    hasCreditOrder(): boolean {
        return !!this.selectedPackId || this.customCreditsValid();
    }

    /** What the order costs, in cents — the pack's price, or the amount at the list rate. */
    orderPriceCents(): number {
        const c = this.credits();
        if (!c) return 0;
        if (this.selectedPackId) return this.selectedPackPriceCents();
        return this.customCreditsValid() ? this.customCredits! * c.unitPriceUsdCents : 0;
    }

    packPriceUsd(cents: number): string {
        return `$${(cents / 100).toFixed(cents % 100 === 0 ? 0 : 2)}`;
    }

    selectedPackPriceCents(): number {
        return this.credits()?.packs.find(p => p.id === this.selectedPackId)?.priceUsdCents ?? 0;
    }

    async buyCredits() {
        if (!this.hasCreditOrder()) return;
        const order = { packId: this.selectedPackId, credits: this.selectedPackId ? null : this.customCredits };

        this.creditsBusy.set(true);
        this.creditsMessage.set(null);
        this.creditsError.set(null);
        try {
            if (this.creditsPayMethod === 'stripe') {
                const res = await this.billingApi.creditsStripeCheckout(order);
                window.location.href = res.url; // Stripe hosted checkout page
            } else {
                await this.billingApi.creditsStarsInvoice(order);
                this.creditsMessage.set(this.t().settings.credits.invoiceSent);
                this.selectedPackId = null;
                this.customCredits = null;
            }
        } catch (e) {
            this.creditsError.set(httpErrorMessage(e, this.t().settings.errors.checkout));
        } finally {
            this.creditsBusy.set(false);
        }
    }

    creditReasonLabel(reason: string): string {
        return this.t().settings.credits.reasons[reason] ?? reason;
    }

    setLedgerPage(page: number) {
        this.ledgerPage.set(Math.max(1, Math.min(page, this.ledgerPages())));
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
}
