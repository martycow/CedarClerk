import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { httpErrorMessage } from './http-error.util';
import { LocaleService, UiLang } from './i18n/locale.service';

interface MeResponse {
    // Phase 13 — which optional modules this installation runs (ADR-101). Optional in the type
    // because an older server simply omits it, and an absent module must read as "off".
    modules?: { indieDev?: boolean };
    email: string; createdAt: string | null; emailConfirmed?: boolean; isAdmin: boolean; planTier: string | null; planExpiresAt: string | null; trialUsed: boolean;
    telegramLinked: boolean; telegramUsername: string | null; telegramLinkedAt: string | null;
    notifyOnEngagement: boolean;
    postSignature: string | null; postSignatureUrl: string | null; postSignatureTexts?: Record<string, string>;
    authorDisplayName: string | null; profileUrl: string | null; profileLocation: string | null;
    headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
    socialTwitterUrl: string | null; socialInstagramUrl: string | null; socialFacebookUrl: string | null;
    socialYoutubeUrl: string | null; socialGithubUrl: string | null;
    socialTelegramUrl: string | null; socialThreadsUrl: string | null; socialBlueskyUrl: string | null; socialRedditUrl: string | null; socialSteamUrl: string | null; socialItchUrl: string | null;
    toolbarLayoutJson: string | null; appearancePrefsJson: string | null; newDraftDefaultsJson: string | null;
    uiLanguage: string | null;
    avatarUrl: string | null;
    blogLinkText: string | null; telegramLinkText: string | null;
    blogLinkTexts?: Record<string, string>; telegramLinkTexts?: Record<string, string>;
}

// 'unavailable' means the server didn't answer, NOT that nobody is signed in — see refresh().
export type RefreshOutcome = 'ok' | 'unauthenticated' | 'unavailable';

// Backoff between /me attempts; the sum is how long a guard waits before giving up (T-062).
const MeRetryDelaysMs = [400, 1200, 3000];

@Injectable({ providedIn: 'root' })
export class AuthService {
    private http = inject(HttpClient);
    private router = inject(Router);
    private locale = inject(LocaleService);

    readonly userEmail = signal<string | null>(null);
    readonly createdAt = signal<string | null>(null);
    // IF2 — hides the /admin entry point. The real gate is server-side on /api/admin.
    readonly isAdmin = signal(false);
    // ADR-101 — the indie-gamedev module. Same kind of flag as isAdmin: it hides the nav entry and
    // the routes, while the real answer is that the server never maps those endpoints when it is off.
    readonly indieDev = signal(false);
    readonly planTier = signal<string | null>(null);
    readonly planExpiresAt = signal<string | null>(null);
    readonly trialUsed = signal(false);
    readonly telegramLinked = signal(false);
    readonly telegramUsername = signal<string | null>(null);
    readonly telegramLinkedAt = signal<string | null>(null);
    readonly notifyOnEngagement = signal(false);
    readonly postSignature = signal<string | null>(null);
    readonly postSignatureUrl = signal<string | null>(null);
    // FI5 — the same signature in the other content languages, keyed by language code; a
    // signature is read at the bottom of whichever language's post it is, same reasoning as the
    // cross-link labels below.
    /** T-002 — false until the address has been confirmed from the mail. */
    readonly emailConfirmed = signal(true);
    readonly postSignatureTexts = signal<Record<string, string>>({});
    readonly authorDisplayName = signal<string | null>(null);
    readonly profileUrl = signal<string | null>(null);
    readonly profileLocation = signal<string | null>(null);
    readonly headerSlot1Type = signal<string | null>(null);
    readonly headerSlot2Type = signal<string | null>(null);
    readonly headerSlot3Type = signal<string | null>(null);
    readonly socialTwitterUrl = signal<string | null>(null);
    readonly socialInstagramUrl = signal<string | null>(null);
    readonly socialFacebookUrl = signal<string | null>(null);
    readonly socialYoutubeUrl = signal<string | null>(null);
    readonly socialGithubUrl = signal<string | null>(null);
    readonly socialTelegramUrl = signal<string | null>(null);
    readonly socialThreadsUrl = signal<string | null>(null);
    readonly socialBlueskyUrl = signal<string | null>(null);
    readonly socialRedditUrl = signal<string | null>(null);
    readonly socialSteamUrl = signal<string | null>(null);
    readonly socialItchUrl = signal<string | null>(null);
    readonly appearancePrefsJson = signal<string | null>(null);
    readonly newDraftDefaultsJson = signal<string | null>(null);
    readonly uiLanguage = signal<string | null>(null);
    // I15 — cross-link wording; null falls back to the built-in text.
    // IF1 — a /media/... path, or null for the initial-letter placeholder.
    readonly avatarUrl = signal<string | null>(null);
    readonly blogLinkText = signal<string | null>(null);
    readonly telegramLinkText = signal<string | null>(null);
    // The same two labels for the non-primary languages, keyed by language code — a cross-link is
    // read by whoever reads that language's version of the post.
    readonly blogLinkTexts = signal<Record<string, string>>({});
    readonly telegramLinkTexts = signal<Record<string, string>>({});
    // True after refresh() exhausted its retries without an answer — the session is unknown, not
    // over. The login page uses this to offer a retry instead of a "wrong password"-shaped dead end.
    readonly serverUnreachable = signal(false);

    /**
     * ADR-108 — the server distinguishes "wrong password" (401) from "could not ask" (503), and so
     * must this: sending somebody to change a password because their network was down is the worst
     * kind of sign-in error. The message comes back from the server, which knows which host it
     * failed to reach.
     */
    async login(email: string, password: string): Promise<{ ok: true } | { ok: false; error?: string }> {
        try {
            await firstValueFrom(this.http.post('/api/auth/login', { email, password }));
            await this.refresh();
            return this.userEmail() !== null ? { ok: true } : { ok: false };
        } catch (e) {
            const message = e instanceof HttpErrorResponse && e.status === 503
                ? httpErrorMessage(e, '')
                : undefined;
            return { ok: false, error: message || undefined };
        }
    }

    async register(email: string, password: string, inviteCode: string): Promise<{ ok: true } | { ok: false; error: string }> {
        try {
            await firstValueFrom(this.http.post('/api/auth/register', { email, password, inviteCode }));
            await this.refresh();
            return this.userEmail() !== null ? { ok: true } : { ok: false, error: 'Registration failed' };
        } catch (e) {
            return { ok: false, error: this.extractRegisterError(e) };
        }
    }

    // /api/auth/register returns either {error: string} (e.g. bad invite code) or
    // {errors: string[]} (ASP.NET Identity password/email validation) — surface whichever fired.
    private extractRegisterError(e: unknown): string {
        if (e instanceof HttpErrorResponse) {
            const body = e.error;
            if (typeof body?.error === 'string') return body.error;
            if (Array.isArray(body?.errors)) return body.errors.join(' ');
        }
        return 'Registration failed';
    }

    // A failed /api/auth/me is not proof of a logout (T-062): the auth cookie lives 30 days and
    // survives network blips, 5xx and the 502 Cloudflare answers with while the server restarts
    // mid-deploy. Only a 401 clears the session; anything else is retried and then reported as
    // 'unavailable' with the current state left untouched.
    async refresh(): Promise<RefreshOutcome> {
        for (let attempt = 0; ; attempt++) {
            try {
                const me = await firstValueFrom(this.http.get<MeResponse>('/api/auth/me'));
                this.applyMe(me);
                this.serverUnreachable.set(false);
                return 'ok';
            } catch (e) {
                if (e instanceof HttpErrorResponse && e.status === 401) {
                    this.clearSession();
                    this.serverUnreachable.set(false);
                    return 'unauthenticated';
                }
                if (attempt >= MeRetryDelaysMs.length) {
                    this.serverUnreachable.set(true);
                    return 'unavailable';
                }
                await new Promise(resolve => setTimeout(resolve, MeRetryDelaysMs[attempt]));
            }
        }
    }

    private applyMe(me: MeResponse): void {
        this.userEmail.set(me.email);
        this.emailConfirmed.set(me.emailConfirmed ?? true);
        this.createdAt.set(me.createdAt);
        this.isAdmin.set(me.isAdmin);
        this.indieDev.set(me.modules?.indieDev ?? false);
        this.planTier.set(me.planTier);
        this.planExpiresAt.set(me.planExpiresAt);
        this.trialUsed.set(me.trialUsed);
        this.telegramLinked.set(me.telegramLinked);
        this.telegramUsername.set(me.telegramUsername);
        this.telegramLinkedAt.set(me.telegramLinkedAt);
        this.notifyOnEngagement.set(me.notifyOnEngagement);
        this.postSignature.set(me.postSignature);
        this.postSignatureUrl.set(me.postSignatureUrl);
        this.postSignatureTexts.set(me.postSignatureTexts ?? {});
        this.authorDisplayName.set(me.authorDisplayName);
        this.profileUrl.set(me.profileUrl);
        this.profileLocation.set(me.profileLocation);
        this.headerSlot1Type.set(me.headerSlot1Type);
        this.headerSlot2Type.set(me.headerSlot2Type);
        this.headerSlot3Type.set(me.headerSlot3Type);
        this.socialTwitterUrl.set(me.socialTwitterUrl);
        this.socialInstagramUrl.set(me.socialInstagramUrl);
        this.socialFacebookUrl.set(me.socialFacebookUrl);
        this.socialYoutubeUrl.set(me.socialYoutubeUrl);
        this.socialGithubUrl.set(me.socialGithubUrl);
        this.socialTelegramUrl.set(me.socialTelegramUrl);
        this.socialThreadsUrl.set(me.socialThreadsUrl);
        this.socialBlueskyUrl.set(me.socialBlueskyUrl);
        this.socialRedditUrl.set(me.socialRedditUrl);
        this.socialSteamUrl.set(me.socialSteamUrl);
        this.socialItchUrl.set(me.socialItchUrl);
        this.appearancePrefsJson.set(me.appearancePrefsJson);
        this.newDraftDefaultsJson.set(me.newDraftDefaultsJson);
        this.uiLanguage.set(me.uiLanguage);
        this.avatarUrl.set(me.avatarUrl);
        this.blogLinkText.set(me.blogLinkText);
        this.telegramLinkText.set(me.telegramLinkText);
        this.blogLinkTexts.set(me.blogLinkTexts ?? {});
        this.telegramLinkTexts.set(me.telegramLinkTexts ?? {});
        // The profile wins over the localStorage cache the service started from (ADR-044).
        this.locale.adoptProfileLanguage(me.uiLanguage);
    }

    private clearSession(): void {
        this.userEmail.set(null);
        this.createdAt.set(null);
        this.isAdmin.set(false);
        this.indieDev.set(false);
        this.planTier.set(null);
        this.planExpiresAt.set(null);
        this.trialUsed.set(false);
        this.telegramLinked.set(false);
        this.telegramUsername.set(null);
        this.telegramLinkedAt.set(null);
        this.notifyOnEngagement.set(false);
        this.postSignature.set(null);
        this.postSignatureUrl.set(null);
        this.postSignatureTexts.set({});
        this.authorDisplayName.set(null);
        this.profileUrl.set(null);
        this.profileLocation.set(null);
        this.headerSlot1Type.set(null);
        this.headerSlot2Type.set(null);
        this.headerSlot3Type.set(null);
        this.socialTwitterUrl.set(null);
        this.socialInstagramUrl.set(null);
        this.socialFacebookUrl.set(null);
        this.socialYoutubeUrl.set(null);
        this.socialGithubUrl.set(null);
        this.socialTelegramUrl.set(null);
        this.socialThreadsUrl.set(null);
        this.socialBlueskyUrl.set(null);
        this.socialRedditUrl.set(null);
        this.socialSteamUrl.set(null);
        this.socialItchUrl.set(null);
        this.appearancePrefsJson.set(null);
        this.newDraftDefaultsJson.set(null);
        this.uiLanguage.set(null);
        this.avatarUrl.set(null);
        this.blogLinkText.set(null);
        this.telegramLinkText.set(null);
        this.blogLinkTexts.set({});
        this.telegramLinkTexts.set({});
    }

    async saveSignature(signature: string, signatureUrl: string, signatureTexts?: Record<string, string>): Promise<void> {
        const res = await firstValueFrom(this.http.post<{
            postSignature: string | null; postSignatureUrl: string | null; postSignatureTexts?: Record<string, string>;
        }>('/api/auth/signature', { signature, signatureUrl, signatureTexts }));
        this.postSignature.set(res.postSignature);
        this.postSignatureUrl.set(res.postSignatureUrl);
        this.postSignatureTexts.set(res.postSignatureTexts ?? {});
    }

    // IF1 — records which uploaded image is the avatar; null clears it.
    async saveAvatar(avatarUrl: string | null): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ avatarUrl: string | null }>(
            '/api/auth/avatar', { avatarUrl }));
        this.avatarUrl.set(res.avatarUrl);
    }

    async saveProfile(profile: {
        authorDisplayName: string; profileUrl: string; profileLocation: string;
        headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
        socialTwitterUrl?: string; socialInstagramUrl?: string; socialFacebookUrl?: string;
        socialYoutubeUrl?: string; socialGithubUrl?: string;
        socialTelegramUrl?: string; socialThreadsUrl?: string; socialBlueskyUrl?: string; socialRedditUrl?: string; socialSteamUrl?: string; socialItchUrl?: string;
        blogLinkText?: string; telegramLinkText?: string;
        // The other languages, whole — one Save sends every language it edited.
        blogLinkTexts?: Record<string, string>; telegramLinkTexts?: Record<string, string>;
    }): Promise<void> {
        const res = await firstValueFrom(this.http.post<{
            authorDisplayName: string | null; profileUrl: string | null; profileLocation: string | null;
            headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
            socialTwitterUrl: string | null; socialInstagramUrl: string | null; socialFacebookUrl: string | null;
            socialYoutubeUrl: string | null; socialGithubUrl: string | null;
            socialTelegramUrl: string | null; socialThreadsUrl: string | null; socialBlueskyUrl: string | null; socialRedditUrl: string | null; socialSteamUrl: string | null; socialItchUrl: string | null;
            blogLinkText: string | null; telegramLinkText: string | null;
            blogLinkTexts?: Record<string, string>; telegramLinkTexts?: Record<string, string>;
        }>('/api/auth/profile', profile));
        this.authorDisplayName.set(res.authorDisplayName);
        this.profileUrl.set(res.profileUrl);
        this.profileLocation.set(res.profileLocation);
        this.blogLinkText.set(res.blogLinkText);
        this.blogLinkTexts.set(res.blogLinkTexts ?? {});
        this.telegramLinkTexts.set(res.telegramLinkTexts ?? {});
        this.telegramLinkText.set(res.telegramLinkText);
        this.headerSlot1Type.set(res.headerSlot1Type);
        this.headerSlot2Type.set(res.headerSlot2Type);
        this.headerSlot3Type.set(res.headerSlot3Type);
        this.socialTwitterUrl.set(res.socialTwitterUrl);
        this.socialInstagramUrl.set(res.socialInstagramUrl);
        this.socialFacebookUrl.set(res.socialFacebookUrl);
        this.socialYoutubeUrl.set(res.socialYoutubeUrl);
        this.socialGithubUrl.set(res.socialGithubUrl);
        this.socialTelegramUrl.set(res.socialTelegramUrl);
        this.socialThreadsUrl.set(res.socialThreadsUrl);
        this.socialBlueskyUrl.set(res.socialBlueskyUrl);
        this.socialRedditUrl.set(res.socialRedditUrl);
        this.socialSteamUrl.set(res.socialSteamUrl);
        this.socialItchUrl.set(res.socialItchUrl);
    }

    async saveNotificationPrefs(notifyOnEngagement: boolean): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ notifyOnEngagement: boolean }>(
            '/api/auth/notifications', { notifyOnEngagement }));
        this.notifyOnEngagement.set(res.notifyOnEngagement);
    }

    async saveAppearancePrefs(prefsJson: string | null): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ appearancePrefsJson: string | null }>(
            '/api/auth/appearance', { prefsJson }));
        this.appearancePrefsJson.set(res.appearancePrefsJson);
    }

    /**
     * Fills the signature and both cross-link texts for every chosen language from what is written
     * in the source one (01.08.2026). Pro Plus, and one AI call for the whole batch — the same
     * bargain the glossary's translate-all makes.
     */
    async translateProfileTexts(sourceLanguage: string, targetLanguages: string[]) {
        const res = await firstValueFrom(this.http.post<{
            postSignatureTexts: Record<string, string>;
            blogLinkTexts: Record<string, string>;
            telegramLinkTexts: Record<string, string>;
        }>('/api/auth/profile/translate-texts', { sourceLanguage, targetLanguages }));
        this.postSignatureTexts.set(res.postSignatureTexts);
        this.blogLinkTexts.set(res.blogLinkTexts);
        this.telegramLinkTexts.set(res.telegramLinkTexts);
        return res;
    }

    async saveNewDraftDefaults(defaultsJson: string | null): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ newDraftDefaultsJson: string | null }>(
            '/api/auth/new-draft-defaults', { defaultsJson }));
        this.newDraftDefaultsJson.set(res.newDraftDefaultsJson);
    }

    // Applied locally first so the UI switches on click, not after the round-trip (ADR-044).
    async saveUiLanguage(uiLanguage: UiLang): Promise<void> {
        this.locale.set(uiLanguage);
        const res = await firstValueFrom(this.http.post<{ uiLanguage: string | null }>(
            '/api/auth/ui-language', { uiLanguage }));
        this.uiLanguage.set(res.uiLanguage);
    }

    async logout(): Promise<void> {
        try { await firstValueFrom(this.http.post('/api/auth/logout', {})); } catch { }
        this.clearSession();
        this.serverUnreachable.set(false);
        this.router.navigateByUrl('/login');
    }

    /** T-002 — asks for another confirmation mail. Answers the same way whether one was needed. */
    async resendConfirmation() {
        await firstValueFrom(this.http.post<{ sent: boolean }>('/api/auth/resend-confirmation', {}));
    }
}
