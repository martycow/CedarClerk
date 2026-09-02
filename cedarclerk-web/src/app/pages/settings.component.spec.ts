import { signal, type WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { AssetsService } from '../core/assets.service';
import { AuthService } from '../core/auth.service';
import { BillingService, type BillingStatus, type CreditsStatus } from '../core/billing.service';
import { type Channel, ChannelsService } from '../core/channels.service';
import { LocaleService, type UiLang } from '../core/i18n/locale.service';
import { PublishService } from '../core/publish.service';
import { TelegramLinkService } from '../core/telegram-link.service';
import { SettingsComponent } from './settings.component';

describe('settings', () => {
    let fixture: ComponentFixture<SettingsComponent>;
    let locale: LocaleService;
    let finishLanguageSave: () => void;
    let planTier: WritableSignal<string>;
    let telegramLinked: WritableSignal<boolean>;

    beforeEach(async () => {
        localStorage.setItem('cedar-ui-lang', 'en');
        locale = new LocaleService();
        planTier = signal('Free');
        telegramLinked = signal(false);
        const empty = signal<Record<string, string>>({});
        const auth = {
            userEmail: signal<string | null>('author@example.com'),
            emailConfirmed: signal(true),
            createdAt: signal<string | null>(null),
            avatarUrl: signal<string | null>(null),
            planTier,
            planExpiresAt: signal<string | null>(null),
            telegramLinked,
            telegramUsername: signal<string | null>(null),
            telegramLinkedAt: signal<string | null>(null),
            notifyOnEngagement: signal(false),
            postSignature: signal<string | null>(null),
            postSignatureUrl: signal<string | null>(null),
            postSignatureTexts: empty,
            authorDisplayName: signal<string | null>(null),
            profileUrl: signal<string | null>(null),
            profileLocation: signal<string | null>(null),
            timeZoneId: signal('America/Los_Angeles'),
            discoveryOptIn: signal(false),
            blogLinkText: signal<string | null>(null),
            telegramLinkText: signal<string | null>(null),
            blogLinkTexts: empty,
            telegramLinkTexts: empty,
            headerSlot1Type: signal<string | null>(null),
            headerSlot2Type: signal<string | null>(null),
            headerSlot3Type: signal<string | null>(null),
            socialTwitterUrl: signal<string | null>(null),
            socialInstagramUrl: signal<string | null>(null),
            socialFacebookUrl: signal<string | null>(null),
            socialYoutubeUrl: signal<string | null>(null),
            socialGithubUrl: signal<string | null>(null),
            socialTelegramUrl: signal<string | null>(null),
            socialThreadsUrl: signal<string | null>(null),
            socialBlueskyUrl: signal<string | null>(null),
            socialRedditUrl: signal<string | null>(null),
            socialSteamUrl: signal<string | null>(null),
            socialItchUrl: signal<string | null>(null),
            hasAiPlan: () => false,
            hasContentLanguage: () => true,
            saveUiLanguage: vi.fn((language: UiLang) => {
                locale.set(language);
                return new Promise<void>(resolve => { finishLanguageSave = resolve; });
            }),
        };

        TestBed.configureTestingModule({
            providers: [
                { provide: LocaleService, useValue: locale },
                { provide: AuthService, useValue: auth },
                { provide: AssetsService, useValue: {} },
                { provide: BillingService, useValue: { status: async () => null, credits: async () => null } },
                { provide: TelegramLinkService, useValue: { botStatus: async () => null } },
                { provide: ChannelsService, useValue: { list: async () => [], listKnown: async () => [] } },
                { provide: PublishService, useValue: { networks: async () => [] } },
                {
                    provide: ActivatedRoute,
                    useValue: { snapshot: { queryParamMap: convertToParamMap({ tab: 'account' }) } },
                },
            ],
        });
        fixture = TestBed.createComponent(SettingsComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    });

    afterEach(() => localStorage.removeItem('cedar-ui-lang'));

    it('left-anchors the operational page and keeps a single-section account tab without a side index', () => {
        const root = fixture.nativeElement as HTMLElement;
        expect(root.querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        expect(root.querySelector('.settings-strip')).not.toBeNull();
        expect(root.querySelector('.settings-body')?.classList.contains('has-index')).toBe(false);
        expect(root.querySelector('.section-index.is-side')).toBeNull();
    });

    it('announces language persistence after the immediate interface switch', async () => {
        const component = fixture.componentInstance;
        const saving = component.setUiLanguage('ru');
        fixture.detectChanges();

        expect(locale.uiLang()).toBe('ru');
        expect(component.languageBusy()).toBe(true);
        expect(component.languageSaved()).toBe(false);
        expect((fixture.nativeElement as HTMLElement).querySelector('[role="status"]')?.textContent)
            .toContain(locale.t().common.saving);

        finishLanguageSave();
        await saving;
        fixture.detectChanges();

        expect(component.languageBusy()).toBe(false);
        expect(component.languageSaved()).toBe(true);
        expect((fixture.nativeElement as HTMLElement).querySelector('[role="status"]')?.textContent)
            .toContain(locale.t().common.saved);
    });

    it('exposes the current section in both desktop and mobile indexes', () => {
        const component = fixture.componentInstance;
        component.tab.set('profile');
        component.activeSection.set('sec-discovery');
        fixture.detectChanges();

        const root = fixture.nativeElement as HTMLElement;
        const side = root.querySelector<HTMLButtonElement>('.section-index.is-side [aria-current="true"]');
        const strip = root.querySelector<HTMLButtonElement>('.section-index.is-strip [aria-current="true"]');
        expect(side?.textContent?.trim()).toBe(locale.t().settings.discovery.nav);
        expect(strip?.textContent?.trim()).toBe(locale.t().settings.discovery.nav);
    });

    it('announces both payment-method selections as radio groups', () => {
        const component = fixture.componentInstance;
        const billing: BillingStatus = {
            planTier: 'Free', planExpiresAt: null, trialUsed: false, stripeCustomerLinked: false,
            providers: { stripe: true, paypal: true, telegramStars: true },
            prices: { proUsd: 3, proPlusUsd: 6, trialUsd: 1, proStars: 100, proPlusStars: 200, trialStars: 30 },
        };
        const credits: CreditsStatus = {
            balance: 0, xPostCost: 1, packs: [{ id: 'ten', credits: 10, priceUsdCents: 400, priceStars: 200 }],
            unitPriceUsdCents: 40, unitPriceStars: 20, minCustomCredits: 5, maxCustomCredits: 1000,
            providers: { stripe: true, telegramStars: true }, ledger: [],
        };
        telegramLinked.set(true);
        component.billing.set(billing);
        component.credits.set(credits);
        component.selectedPlan = 'pro';
        component.selectedPackId = 'ten';
        component.tab.set('billing');
        fixture.detectChanges();

        const root = fixture.nativeElement as HTMLElement;
        const groups = [...root.querySelectorAll<HTMLElement>('.pay-methods[role="radiogroup"]')];
        expect(groups.length).toBe(2);
        expect(groups.every(group => !!group.getAttribute('aria-labelledby'))).toBe(true);

        const subscriptionMethods = [...groups[0].querySelectorAll<HTMLButtonElement>('[role="radio"]')];
        expect(subscriptionMethods.map(button => button.getAttribute('aria-checked'))).toEqual(['true', 'false', 'false']);
        subscriptionMethods[1].click();
        fixture.detectChanges();
        expect(component.payMethod).toBe('paypal');
        expect(subscriptionMethods[1].getAttribute('aria-checked')).toBe('true');

        const creditMethods = [...groups[1].querySelectorAll<HTMLButtonElement>('[role="radio"]')];
        expect(creditMethods.map(button => button.getAttribute('aria-checked'))).toEqual(['true', 'false']);
    });

    it('associates visible labels with signature and integration credentials', () => {
        const component = fixture.componentInstance;
        const root = () => fixture.nativeElement as HTMLElement;
        const labelled = (id: string) => ({
            label: root().querySelector<HTMLLabelElement>(`label[for="${id}"]`),
            input: root().querySelector<HTMLInputElement>(`#${id}`),
        });

        planTier.set('Pro');
        component.tab.set('profile');
        fixture.detectChanges();
        expect(labelled('cc-sig-url').label?.textContent?.trim()).toBe(locale.t().settings.profile.signatureUrl);
        expect(labelled('cc-sig-url').input).not.toBeNull();

        const channel: Channel = {
            id: 'c1', title: 'Devlog', telegramChatId: -1001, username: 'devlog', avatarUrl: null,
            postSignature: '— Cedar', postSignatureUrl: 'https://example.com', postSignatureTranslationsJson: null,
        };
        component.channels.set([channel]);
        component.signatureOpenId.set(channel.id);
        component.manualChannelOpen.set(true);
        component.tab.set('integrations');
        fixture.detectChanges();

        expect(labelled('cc-channel-signature-c1').label?.textContent?.trim())
            .toBe(locale.t().settings.integrations.signatureTextLabel);
        expect(labelled('cc-channel-signature-url-c1').label?.textContent?.trim())
            .toBe(locale.t().settings.integrations.signatureUrlLabel);
        expect(labelled('cc-manual-channel').label?.textContent?.trim()).toBe(locale.t().settings.integrations.manualLabel);
        expect(labelled('cc-bluesky-handle').label?.textContent?.trim()).toBe(locale.t().settings.integrations.blueskyHandleLabel);
        expect(labelled('cc-bluesky-app-password').label?.textContent?.trim()).toBe(locale.t().settings.integrations.blueskyAppPassword);
        expect(labelled('cc-discord-webhook').label?.textContent?.trim()).toBe(locale.t().settings.integrations.discordWebhookLabel);
    });

    it('leaves channel targets in the shell scroll flow', () => {
        const component = fixture.componentInstance;
        component.channels.set([{
            id: 'c1', title: 'Devlog', telegramChatId: -1001, username: null, avatarUrl: null,
        }]);
        component.tab.set('integrations');
        fixture.detectChanges();

        const list = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.target-list')!;
        const style = getComputedStyle(list);
        expect(style.maxHeight).not.toMatch(/\d/);
        expect(['auto', 'scroll']).not.toContain(style.overflowY);
    });
});
