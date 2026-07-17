import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
    private http = inject(HttpClient);
    private router = inject(Router);

    readonly userEmail = signal<string | null>(null);
    readonly createdAt = signal<string | null>(null);
    readonly planTier = signal<string | null>(null);
    readonly planExpiresAt = signal<string | null>(null);
    readonly trialUsed = signal(false);
    readonly telegramLinked = signal(false);
    readonly telegramUsername = signal<string | null>(null);
    readonly telegramLinkedAt = signal<string | null>(null);
    readonly postSignature = signal<string | null>(null);
    readonly authorDisplayName = signal<string | null>(null);
    readonly profileUrl = signal<string | null>(null);
    readonly profileLocation = signal<string | null>(null);
    readonly headerSlot1Type = signal<string | null>(null);
    readonly headerSlot2Type = signal<string | null>(null);
    readonly headerSlot3Type = signal<string | null>(null);

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
                email: string; createdAt: string | null; planTier: string | null; planExpiresAt: string | null; trialUsed: boolean;
                telegramLinked: boolean; telegramUsername: string | null; telegramLinkedAt: string | null;
                postSignature: string | null;
                authorDisplayName: string | null; profileUrl: string | null; profileLocation: string | null;
                headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
            }>('/api/auth/me'));
            this.userEmail.set(me.email);
            this.createdAt.set(me.createdAt);
            this.planTier.set(me.planTier);
            this.planExpiresAt.set(me.planExpiresAt);
            this.trialUsed.set(me.trialUsed);
            this.telegramLinked.set(me.telegramLinked);
            this.telegramUsername.set(me.telegramUsername);
            this.telegramLinkedAt.set(me.telegramLinkedAt);
            this.postSignature.set(me.postSignature);
            this.authorDisplayName.set(me.authorDisplayName);
            this.profileUrl.set(me.profileUrl);
            this.profileLocation.set(me.profileLocation);
            this.headerSlot1Type.set(me.headerSlot1Type);
            this.headerSlot2Type.set(me.headerSlot2Type);
            this.headerSlot3Type.set(me.headerSlot3Type);
        } catch {
            this.userEmail.set(null);
            this.createdAt.set(null);
            this.planTier.set(null);
            this.planExpiresAt.set(null);
            this.trialUsed.set(false);
            this.telegramLinked.set(false);
            this.telegramUsername.set(null);
            this.telegramLinkedAt.set(null);
            this.postSignature.set(null);
            this.authorDisplayName.set(null);
            this.profileUrl.set(null);
            this.profileLocation.set(null);
            this.headerSlot1Type.set(null);
            this.headerSlot2Type.set(null);
            this.headerSlot3Type.set(null);
        }
    }

    async saveSignature(signature: string): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ postSignature: string | null }>(
            '/api/auth/signature', { signature }));
        this.postSignature.set(res.postSignature);
    }

    async saveProfile(profile: {
        authorDisplayName: string; profileUrl: string; profileLocation: string;
        headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
    }): Promise<void> {
        const res = await firstValueFrom(this.http.post<{
            authorDisplayName: string | null; profileUrl: string | null; profileLocation: string | null;
            headerSlot1Type: string | null; headerSlot2Type: string | null; headerSlot3Type: string | null;
        }>('/api/auth/profile', profile));
        this.authorDisplayName.set(res.authorDisplayName);
        this.profileUrl.set(res.profileUrl);
        this.profileLocation.set(res.profileLocation);
        this.headerSlot1Type.set(res.headerSlot1Type);
        this.headerSlot2Type.set(res.headerSlot2Type);
        this.headerSlot3Type.set(res.headerSlot3Type);
    }

    async logout(): Promise<void> {
        try { await firstValueFrom(this.http.post('/api/auth/logout', {})); } catch { }
        this.userEmail.set(null);
        this.router.navigateByUrl('/login');
    }
}