import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppearanceService, ACCENT_PRESETS, DEFAULT_APPEARANCE } from '../core/appearance.service';
import { LocaleService } from '../core/i18n/locale.service';
import { AppearancePanelComponent } from './appearance-panel.component';

describe('AppearancePanelComponent', () => {
    it('labels the accent group and exposes selection through aria and a check marker in both locales', () => {
        const prefs = signal({ ...DEFAULT_APPEARANCE });
        const appearance = {
            prefs,
            paintedTheme: computed(() => prefs().theme === 'dark' ? 'dark' : 'light'),
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
        const buttons = () => [...root.querySelectorAll<HTMLButtonElement>('button.ap-accent')];
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

        fixture.componentInstance.setSidebarMode('rail');
        expect(appearance.preview).toHaveBeenCalledWith({ sidebarMode: 'rail' });
    });

    function mount(overrides: Partial<typeof DEFAULT_APPEARANCE> = {}) {
        const prefs = signal({ ...DEFAULT_APPEARANCE, ...overrides });
        const appearance = {
            prefs,
            paintedTheme: computed(() => prefs().theme === 'dark' ? 'dark' : 'light'),
            dirty: signal(false),
            preview: vi.fn((patch: Partial<typeof DEFAULT_APPEARANCE>) => prefs.update(p => ({ ...p, ...patch }))),
            commit: vi.fn().mockResolvedValue(undefined),
        };
        TestBed.configureTestingModule({ providers: [{ provide: AppearanceService, useValue: appearance }] });
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(AppearancePanelComponent);
        fixture.detectChanges();
        return { fixture, appearance, root: fixture.nativeElement as HTMLElement };
    }

    it('refuses a custom accent under 3:1 on the paper with an inline message and paints one that clears it', () => {
        const { fixture, appearance, root } = mount();
        const input = root.querySelector<HTMLInputElement>('.ap-swatch-input')!;
        expect(input.getAttribute('aria-label')).toBe('Your own colour');

        fixture.componentInstance.setCustomAccent('#e0c060');
        fixture.detectChanges();
        expect(appearance.preview).not.toHaveBeenCalled();
        const refused = root.querySelector('.ap-accent-refused') as HTMLElement;
        expect(refused.getAttribute('role')).toBe('alert');
        expect(refused.textContent).toContain('3:1');
        expect(input.getAttribute('aria-invalid')).toBe('true');
        expect(input.getAttribute('aria-describedby')).toBe(refused.id);

        fixture.componentInstance.setCustomAccent('#204060');
        fixture.detectChanges();
        expect(appearance.preview).toHaveBeenCalledWith({ accentLight: '#204060' });
        expect(root.querySelector('.ap-accent-refused')).toBeNull();
        expect(root.querySelector('.ap-accent-custom')?.classList.contains('on')).toBe(true);
        expect(root.querySelectorAll('button.ap-accent[aria-pressed="true"]')).toHaveLength(0);

        appearance.prefs.update(p => ({ ...p, theme: 'dark' }));
        fixture.componentInstance.setCustomAccent('#7E7E7E');
        expect(appearance.preview).not.toHaveBeenCalledWith({ accentDark: '#7E7E7E' });
        expect(fixture.componentInstance.accentRefused()).toContain('3:1');
    });

    it('offers the area presets as one select that sets the three sheet controls together', () => {
        const { fixture, appearance, root } = mount();
        const select = root.querySelector<HTMLSelectElement>('#appearance-area-preset')!;
        expect([...select.options].map(o => o.textContent?.trim())).toEqual(['Custom', 'Telegram', 'iPhone', 'iPad', 'Blog']);
        expect(select.selectedIndex).toBe(0);

        fixture.componentInstance.pickAreaPreset('telegram');
        fixture.detectChanges();
        expect(appearance.preview).toHaveBeenCalledWith({ sheetWidth: 'narrow', fontSize: 15, lineHeight: 1.45 });
        expect(fixture.componentInstance.areaPreset()).toBe('telegram');
        expect(select.selectedIndex).toBe(1);

        fixture.componentInstance.setFontSize(16);
        fixture.detectChanges();
        expect(fixture.componentInstance.areaPreset()).toBe('custom');
        expect(select.selectedIndex).toBe(0);

        appearance.preview.mockClear();
        fixture.componentInstance.pickAreaPreset('custom');
        expect(appearance.preview).not.toHaveBeenCalled();
    });
});
