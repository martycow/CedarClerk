import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
    private http = inject(HttpClient);
    private router = inject(Router);

    readonly userEmail = signal<string | null>(null);

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
            const me = await firstValueFrom(this.http.get<{ email: string }>('/api/auth/me'));
            this.userEmail.set(me.email);
        } catch {
            this.userEmail.set(null);
        }
    }

    async logout(): Promise<void> {
        try { await firstValueFrom(this.http.post('/api/auth/logout', {})); } catch { }
        this.userEmail.set(null);
        this.router.navigateByUrl('/login');
    }
}