import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { en } from '@localization/en';
import { DesktopInstaller, DesktopPlatform, DownloadComponent, detectDesktopPlatform } from './download.component';

const ALL: DesktopInstaller[] = [
    { platform: 'windows', version: '0.25.1', url: '/downloads/latest/windows' },
    { platform: 'mac', version: '0.26.0', url: '/downloads/latest/mac' },
    { platform: 'linux', version: '0.26.0', url: '/downloads/latest/linux' },
];

describe('desktop platform detection', () => {
    it('reads the platform hint first and the user agent after it', () => {
        expect(detectDesktopPlatform({ userAgentData: { platform: 'Windows' } })).toBe('windows');
        expect(detectDesktopPlatform({ platform: 'MacIntel', maxTouchPoints: 0 })).toBe('mac');
        expect(detectDesktopPlatform({ platform: 'Linux x86_64' })).toBe('linux');
        expect(detectDesktopPlatform({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' })).toBe('windows');
        expect(detectDesktopPlatform({ userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)' })).toBe('mac');
        expect(detectDesktopPlatform({ userAgent: 'Mozilla/5.0 (X11; Linux x86_64)' })).toBe('linux');
    });

    it('offers no desktop platform to a phone or a tablet', () => {
        expect(detectDesktopPlatform({ platform: 'Linux armv8l', userAgent: 'Mozilla/5.0 (Linux; Android 14)' })).toBeNull();
        expect(detectDesktopPlatform({ platform: 'iPhone', userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)' })).toBeNull();
        expect(detectDesktopPlatform({ platform: 'MacIntel', maxTouchPoints: 5, userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)' })).toBeNull();
        expect(detectDesktopPlatform({})).toBeNull();
    });
});

describe('download page', () => {
    let fixture: ComponentFixture<DownloadComponent>;
    const t = en.download;
    const el = () => fixture.nativeElement as HTMLElement;
    const primary = () => el().querySelector<HTMLAnchorElement>('app-button.dl-primary a');
    const others = () => [...el().querySelectorAll<HTMLAnchorElement>('app-button.dl-other a')];

    async function create(detected: DesktopPlatform | null, published: DesktopInstaller[] | 'fail') {
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
        fixture = TestBed.createComponent(DownloadComponent);
        fixture.componentInstance.detected.set(detected);
        fixture.detectChanges();
        const request = TestBed.inject(HttpTestingController).expectOne('/downloads/platforms');
        if (published === 'fail') request.flush('', { status: 500, statusText: 'Server Error' });
        else request.flush({ platforms: published });
        await fixture.whenStable();
        fixture.detectChanges();
    }

    it('leads with the installer for the visitor\'s system and keeps the others reachable', async () => {
        await create('mac', ALL);

        expect(el().querySelector('.dl-detected')?.textContent?.trim()).toBe(t.detected('macOS'));
        expect(primary()?.getAttribute('href')).toBe('/downloads/latest/mac');
        expect(primary()?.textContent).toContain(t.getFor('macOS'));
        expect(el().querySelector('.dl-version')?.textContent?.trim()).toBe(t.version('0.26.0'));
        expect(others().map(a => a.getAttribute('href'))).toEqual(['/downloads/latest/windows', '/downloads/latest/linux']);
        expect(el().querySelector('.dl-others .label')?.textContent?.trim()).toBe(t.otherPlatforms);
    });

    it('picks a different lead for a different system', async () => {
        await create('windows', ALL);
        expect(primary()?.getAttribute('href')).toBe('/downloads/latest/windows');
        expect(others().map(a => a.textContent?.trim())).toEqual(['macOS', 'Linux']);
    });

    it('says so when the visitor\'s system has no installer, and offers what exists', async () => {
        await create('linux', [ALL[0]]);

        expect(primary()).toBeNull();
        expect(el().querySelector('.dl-missing')?.textContent?.trim()).toBe(t.notPublishedFor('Linux'));
        expect(others().map(a => a.getAttribute('href'))).toEqual(['/downloads/latest/windows']);
        expect(el().querySelector('.dl-others .label')?.textContent?.trim()).toBe(t.choosePlatform);
    });

    it('lists every installer without a lead when the system is not a desktop one', async () => {
        await create(null, ALL);
        expect(primary()).toBeNull();
        expect(el().querySelector('.dl-detected')).toBeNull();
        expect(others().length).toBe(3);
    });

    it('invents no link when nothing is published or the list cannot be read', async () => {
        await create('mac', []);
        expect(el().querySelector('.dl-missing')?.textContent?.trim()).toBe(t.nonePublished);
        expect(el().querySelectorAll('a[href^="/downloads"]').length).toBe(0);

        await create('mac', 'fail');
        expect(el().querySelector('.dl-missing')?.textContent?.trim()).toBe(t.nonePublished);
        expect(el().querySelectorAll('a[href^="/downloads"]').length).toBe(0);
    });
});
