import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LocaleService, UiLang } from './i18n/locale.service';

@Injectable({ providedIn: 'root' })
export class AuthService {
    private http = inject(HttpClient);
    private router = inject(Router);
    private locale = inject(LocaleService);

    readonly userEmail = signal<string | null>(null);
    readonly createdAt = signal<string | null>(null);
    // IF2 — hides the /admin entry point. The real gate is server-side on /api/admin.
    readonly isAdmin = signal(false);
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
    readonly toolbarLayoutJson = signal<string | null>(null);
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

    async login(email: string, password: string): Promise<boolean> {
        try {
            await firstValueFrom(this.http.post('/api/auth/login', { email, password }));
            await this.refresh();
            return this.userEmail() !== null;
        } catch {
            return false;
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

    async refresh(): Promise<void> {
        try {
            const me = await firstValueFrom(this.http.get<{
                email: string; createdAt: string | null; isAdmin: boolean; planTier: string | null; planExpiresAt: string | null; trialUsed: boolean;
                telegramLinked: boolean; telegramUsername: string | null; telegramLinkedAt: string | null;
                notifyOnEngagement: boolean;
                postSignature: string | null; postSignatureUrl: string | null; postSignatureTexts?: Record<string, string>;
                authorDisplayName: string | null; profileUrl: string | null; profileLocation: string | null;
                headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
                socialTwitterUrl: string | null; socialInstagramUrl: string | null; socialFacebookUrl: string | null;
                socialYoutubeUrl: string | null; socialGithubUrl: string | null;
                toolbarLayoutJson: string | null; appearancePrefsJson: string | null; newDraftDefaultsJson: string | null;
                uiLanguage: string | null;
                avatarUrl: string | null;
                blogLinkText: string | null; telegramLinkText: string | null;
                blogLinkTexts?: Record<string, string>; telegramLinkTexts?: Record<string, string>;
            }>('/api/auth/me'));
            this.userEmail.set(me.email);
            this.createdAt.set(me.createdAt);
            this.isAdmin.set(me.isAdmin);
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
            this.toolbarLayoutJson.set(me.toolbarLayoutJson);
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
        } catch {
            this.userEmail.set(null);
            this.createdAt.set(null);
            this.isAdmin.set(false);
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
            this.toolbarLayoutJson.set(null);
            this.appearancePrefsJson.set(null);
            this.newDraftDefaultsJson.set(null);
            this.uiLanguage.set(null);
            this.avatarUrl.set(null);
            this.blogLinkText.set(null);
            this.telegramLinkText.set(null);
            this.blogLinkTexts.set({});
            this.telegramLinkTexts.set({});
        }
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
        blogLinkText?: string; telegramLinkText?: string;
        // The other languages, whole — one Save sends every language it edited.
        blogLinkTexts?: Record<string, string>; telegramLinkTexts?: Record<string, string>;
    }): Promise<void> {
        const res = await firstValueFrom(this.http.post<{
            authorDisplayName: string | null; profileUrl: string | null; profileLocation: string | null;
            headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
            socialTwitterUrl: string | null; socialInstagramUrl: string | null; socialFacebookUrl: string | null;
            socialYoutubeUrl: string | null; socialGithubUrl: string | null;
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
    }

    async saveNotificationPrefs(notifyOnEngagement: boolean): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ notifyOnEngagement: boolean }>(
            '/api/auth/notifications', { notifyOnEngagement }));
        this.notifyOnEngagement.set(res.notifyOnEngagement);
    }

    async saveToolbarLayout(layoutJson: string | null): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ toolbarLayoutJson: string | null }>(
            '/api/auth/toolbar-layout', { layoutJson }));
        this.toolbarLayoutJson.set(res.toolbarLayoutJson);
    }

    async saveAppearancePrefs(prefsJson: string | null): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ appearancePrefsJson: string | null }>(
            '/api/auth/appearance', { prefsJson }));
        this.appearancePrefsJson.set(res.appearancePrefsJson);
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
        this.userEmail.set(null);
        this.router.navigateByUrl('/login');
    }
}