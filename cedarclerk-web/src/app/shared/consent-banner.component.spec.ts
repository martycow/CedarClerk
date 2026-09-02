import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AnalyticsService } from '../core/analytics.service';
import { ConsentService } from '../core/consent.service';
import { LocaleService } from '../core/i18n/locale.service';
import { ConsentBannerComponent } from './consent-banner.component';

describe('consent banner', () => {
    let fixture: ComponentFixture<ConsentBannerComponent>;
    const state = signal<'unasked' | 'granted' | 'denied'>('unasked');
    const consent = {
        state,
        grant: vi.fn(() => state.set('granted')),
        deny: vi.fn(() => state.set('denied')),
    };
    const analytics = {
        isConfigured: vi.fn().mockResolvedValue(true),
        enableIfConsented: vi.fn().mockResolvedValue(undefined),
    };

    beforeEach(async () => {
        state.set('unasked');
        consent.grant.mockClear();
        consent.deny.mockClear();
        analytics.enableIfConsented.mockClear();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: ConsentService, useValue: consent },
                { provide: AnalyticsService, useValue: analytics },
            ],
        });
        TestBed.inject(LocaleService).uiLang.set('en');
        fixture = TestBed.createComponent(ConsentBannerComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    });

    it('keeps the same paper dialog, privacy route, and two native actions', () => {
        const root = fixture.nativeElement as HTMLElement;
        const dialog = root.querySelector<HTMLElement>('.consent[role="dialog"]')!;
        const buttons = [...dialog.querySelectorAll<HTMLButtonElement>('button')];

        expect(dialog.getAttribute('aria-modal')).toBe('false');
        expect(dialog.querySelector('app-paper-card')?.getAttribute('data-surface')).toBe('paper');
        expect(dialog.querySelector<HTMLAnchorElement>('a')?.getAttribute('href')).toBe('/privacy');
        expect(buttons.length).toBe(2);
        expect(buttons[0].classList.contains('sm')).toBe(true);
        expect(buttons[1].classList.contains('primary')).toBe(true);
        expect(dialog.querySelector('app-button')).toBeNull();
    });

    it('preserves decline and accept behavior', () => {
        let buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')];
        buttons[0].click();
        fixture.detectChanges();
        expect(consent.deny).toHaveBeenCalledOnce();

        state.set('unasked');
        fixture.detectChanges();
        buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button')];
        buttons[1].click();
        fixture.detectChanges();
        expect(consent.grant).toHaveBeenCalledOnce();
        expect(analytics.enableIfConsented).toHaveBeenCalledOnce();
    });
});
