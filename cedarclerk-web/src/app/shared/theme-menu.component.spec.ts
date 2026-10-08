import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ThemeMenuComponent } from './theme-menu.component';
import { AuthService } from '../core/auth.service';
import { AppearanceService } from '../core/appearance.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';

describe('theme menu', () => {
    function mount(signedIn: boolean) {
        TestBed.resetTestingModule();
        const appearance = { preview: vi.fn(), commit: vi.fn().mockResolvedValue(undefined) };
        TestBed.configureTestingModule({
            providers: [
                { provide: AuthService, useValue: { userEmail: signal(signedIn ? 'a@b.co' : null) } },
                { provide: AppearanceService, useValue: appearance },
            ],
        });
        TestBed.inject(LocaleService).uiLang.set('en');
        const theme = TestBed.inject(ThemeService);
        theme.set('light');
        const fixture = TestBed.createComponent(ThemeMenuComponent);
        fixture.detectChanges();
        (fixture.nativeElement.querySelector('.tm-trigger') as HTMLButtonElement).click();
        fixture.detectChanges();
        const options = () => [...fixture.nativeElement.querySelectorAll('.tm-option')] as HTMLButtonElement[];
        return { fixture, appearance, theme, options };
    }

    afterEach(() => localStorage.removeItem('cedar-theme'));

    it('offers Light, Dark and System as toggle buttons with the current one pressed', () => {
        const { options } = mount(false);
        expect(options().map(o => o.textContent?.trim())).toEqual(['Light', 'Dark', 'System']);
        expect(options().map(o => o.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false']);
    });

    it('signed out, stores System in the browser and keeps painting a real theme', () => {
        const { options, theme } = mount(false);
        options()[2].click();
        expect(theme.mode()).toBe('system');
        expect(localStorage.getItem('cedar-theme')).toBe('system');
        expect(['light', 'dark']).toContain(document.documentElement.dataset['theme']);
    });

    it('signed in, writes the choice to the account appearance', () => {
        const { options, appearance } = mount(true);
        options()[1].click();
        expect(appearance.preview).toHaveBeenCalledWith({ theme: 'dark' });
        expect(appearance.commit).toHaveBeenCalled();
    });
});
