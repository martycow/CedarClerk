import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
    private http = inject(HttpClient);
    private router = inject(Router);

    readonly userEmail = signal<string | null>(null);
    readonly planTier = signal<string | null>(null);
    readonly planExpiresAt = signal<string | null>(null);
    readonly trialUsed = signal(false);
    readonly telegramLinked = signal(false);
    readonly telegramUsername = signal<string | null>(null);
    readonly postSignature = signal<string | null>(null);

    async login(email: string, password: string): Promise<boolean> {
        try {
            await firstValueFrom(this.http.post('/api/auth/login', { email, password }));
            await this.refresh();
            return this.userEmail() !== null;
        } catch {
            return false;
        }
    }

    async register(email: string, password: string, inviteCode: string): Promise<boolean> {
        try {
            await firstValueFrom(this.http.post('/api/auth/register', { email, password, inviteCode }));
            await this.refresh();
            return this.userEmail() !== null;
        } catch {
            return false;
        }
    }

    async refresh(): Promise<void> {
        try {
            const me = await firstValueFrom(this.http.get<{
                email: string; planTier: string | null; planExpiresAt: string | null; trialUsed: boolean;
                telegramLinked: boolean; telegramUsername: string | null;
                postSignature: string | null;
            }>('/api/auth/me'));
            this.userEmail.set(me.email);
            this.planTier.set(me.planTier);
            this.planExpiresAt.set(me.planExpiresAt);
            this.trialUsed.set(me.trialUsed);
            this.telegramLinked.set(me.telegramLinked);
            this.telegramUsername.set(me.telegramUsername);
            this.postSignature.set(me.postSignature);
        } catch {
            this.userEmail.set(null);
            this.planTier.set(null);
            this.planExpiresAt.set(null);
            this.trialUsed.set(false);
            this.telegramLinked.set(false);
            this.telegramUsername.set(null);
            this.postSignature.set(null);
        }
    }

    async saveSignature(signature: string): Promise<void> {
        const res = await firstValueFrom(this.http.post<{ postSignature: string | null }>(
            '/api/auth/signature', { signature }));
        this.postSignature.set(res.postSignature);
    }

    async logout(): Promise<void> {
        try { await firstValueFrom(this.http.post('/api/auth/logout', {})); } catch { }
        this.userEmail.set(null);
        this.router.navigateByUrl('/login');
    }
}