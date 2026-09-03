import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppearanceService, ACCENT_PRESETS, DEFAULT_APPEARANCE } from '../core/appearance.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AppearancePanelComponent } from './appearance-panel.component';

describe('AppearancePanelComponent', () => {
    it('labels the accent group and exposes selection through aria and a check marker in both locales', () => {
        const prefs = signal({ ...DEFAULT_APPEARANCE });
        const appearance = {
            prefs,
            dirty: signal(false),
            preview: vi.fn(),
            commit: vi.fn().mockResolvedValue(undefined),
        };
        TestBed.configureTestingModule({
            providers: [
                { provide: AppearanceService, useValue: appearance },
            ],
        });
        const locale = TestBed.inject(LocaleService);
        locale.uiLang.set('en');
        const fixture = TestBed.createComponent(AppearancePanelComponent);
        fixture.detectChanges();

        const root = fixture.nativeElement as HTMLElement;
        const group = root.querySelector('.ap-accents') as HTMLElement;
        const label = root.querySelector('.ap-field-label') as HTMLElement;
        const buttons = () => [...root.querySelectorAll<HTMLButtonElement>('.ap-accent')];
        const status = root.querySelector('.ap-save-state') as HTMLElement;
        expect(group.getAttribute('role')).toBe('group');
        expect(group.getAttribute('aria-label')).toBe(locale.t().settings.appearance.accentHint);
        expect(label.textContent?.trim()).toBe(locale.t().settings.appearance.accentHint);
        expect(buttons()[0].getAttribute('aria-label')).toContain(ACCENT_PRESETS[0].name);
        expect(buttons().map(button => button.getAttribute('aria-pressed')))
            .toEqual(['true', 'false', 'false', 'false', 'false']);
        expect(buttons()[0].querySelector('.ap-accent-check')).toBeTruthy();
        expect(buttons()[1].querySelector('.ap-accent-check')).toBeNull();
        expect(status.getAttribute('role')).toBe('status');
        expect(status.getAttribute('aria-live')).toBe('polite');

        prefs.update(current => ({ ...current, accentLight: ACCENT_PRESETS[1].hex }));
        locale.uiLang.set('ru');
        fixture.detectChanges();
        expect(group.getAttribute('aria-label')).toBe(locale.t().settings.appearance.accentHint);
        expect(buttons()[1].getAttribute('aria-label')).toContain(locale.t().settings.appearance.accentHint);
        expect(buttons().map(button => button.getAttribute('aria-pressed')))
            .toEqual(['false', 'true', 'false', 'false', 'false']);
        expect(buttons()[0].querySelector('.ap-accent-check')).toBeNull();
        expect(buttons()[1].querySelector('.ap-accent-check')).toBeTruthy();

        fixture.componentInstance.setTheme('dark');
        fixture.componentInstance.setSidebarMode('rail');
        expect(appearance.preview).toHaveBeenCalledWith({ theme: 'dark' });
        expect(appearance.preview).toHaveBeenCalledWith({ sidebarMode: 'rail' });
    });
});
