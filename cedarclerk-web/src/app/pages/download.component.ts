import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeMenuComponent } from '../shared/theme-menu.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { IconComponent } from '../shared/icon.component';

export type DesktopPlatform = 'windows' | 'mac' | 'linux';

export interface DesktopInstaller {
    platform: DesktopPlatform;
    version: string;
    url: string;
}

interface PlatformHints {
    userAgentData?: { platform?: string };
    platform?: string;
    userAgent?: string;
    maxTouchPoints?: number;
}

/** Null for a phone, a tablet or anything unrecognised: the app has no build for those. */
export function detectDesktopPlatform(nav: PlatformHints): DesktopPlatform | null {
    const agent = (nav.userAgent ?? '').toLowerCase();
    if (/android|iphone|ipad|ipod/.test(agent)) return null;
    const platform = (nav.userAgentData?.platform || nav.platform || '').toLowerCase();
    const hint = `${platform} ${agent}`;
    if (hint.includes('win')) return 'windows';
    // iPadOS answers as a Mac; a touch screen is what tells the two apart.
    if (hint.includes('mac')) return (nav.maxTouchPoints ?? 0) > 1 ? null : 'mac';
    if (hint.includes('linux') || hint.includes('x11')) return 'linux';
    return null;
}

// The desktop-app page. Outside the shell like the legal pages (ADR-166): a visitor reaches it
// from the landing before there is a session, a signed-in user from the Assets screen, so it
// declares its own surface and carries its own theme toggle. Installer links are full-page hrefs
// to the server's /downloads routes, not SPA routes.
@Component({
    selector: 'app-download',
    imports: [ThemeMenuComponent, RouterLink, ButtonComponent, CedarLogoComponent, IconComponent],
    templateUrl: 'download.component.html',
    styleUrl: 'download.component.css',
})
export class DownloadComponent {
    private readonly http = inject(HttpClient);
    protected readonly t = inject(LocaleService).t;

    readonly detected = signal<DesktopPlatform | null>(detectDesktopPlatform(navigator));
    readonly installers = signal<DesktopInstaller[]>([]);
    readonly loading = signal(true);

    readonly primary = computed(() => this.installers().find(i => i.platform === this.detected()) ?? null);
    readonly others = computed(() => this.installers().filter(i => i !== this.primary()));

    constructor() {
        void this.load();
    }

    private async load() {
        try {
            const answer = await firstValueFrom(this.http.get<{ platforms: DesktopInstaller[] }>('/downloads/platforms'));
            this.installers.set(answer.platforms ?? []);
        } catch {
            this.installers.set([]);
        } finally {
            this.loading.set(false);
        }
    }

    protected name(platform: DesktopPlatform) {
        return this.t().download.platforms[platform];
    }
}
