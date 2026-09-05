import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import {
    ACCENT_MIN_CONTRAST,
    ACCENT_PRESETS,
    AppearanceService,
    DEFAULT_APPEARANCE,
    PAPER_SURFACE,
    SHEET_WIDTH_PX,
    SIDEBAR_MODE_STORAGE_KEY,
    accentContrast,
    accentInk,
    contrastRatio,
    resolveAccent,
    storedAccent,
} from './appearance.service';
import { Theme, ThemeService, THEME_STORAGE_KEY } from './theme.service';

describe('AppearanceService', () => {
    const storedJson = signal<string | null>(null);
    const userEmail = signal<string | null>('appearance@test.local');
    const themeValue = signal<Theme>('dark');
    const saveAppearancePrefs = vi.fn(async (json: string) => { storedJson.set(json); });
    const theme = {
        theme: themeValue,
        set: vi.fn((value: Theme) => {
            themeValue.set(value);
            localStorage.setItem(THEME_STORAGE_KEY, value);
            document.documentElement.dataset['theme'] = value;
        }),
    };

    beforeEach(() => {
        storedJson.set(null);
        userEmail.set('appearance@test.local');
        themeValue.set('dark');
        saveAppearancePrefs.mockClear();
        theme.set.mockClear();
        localStorage.clear();
        localStorage.setItem(SIDEBAR_MODE_STORAGE_KEY, 'rail');
        TestBed.configureTestingModule({
            providers: [
                { provide: AuthService, useValue: { userEmail, appearancePrefsJson: storedJson, saveAppearancePrefs } },
                { provide: ThemeService, useValue: theme },
            ],
        });
    });

    afterEach(() => {
        document.getElementById('__appearance-accent')?.remove();
        delete document.documentElement.dataset['theme'];
        localStorage.clear();
    });

    it('fills new fields from local bootstrap when legacy JSON does not contain them', () => {
        storedJson.set(JSON.stringify({ sheetWidth: 'wide' }));
        const service = TestBed.inject(AppearanceService);

        service.loadFromAuth();

        expect(service.prefs()).toMatchObject({ theme: 'dark', sidebarMode: 'rail', sheetWidth: 'wide' });
        expect(theme.set).toHaveBeenLastCalledWith('dark');
    });

    it('persists theme and sidebar mode in the complete backward-compatible payload', async () => {
        const service = TestBed.inject(AppearanceService);

        service.preview({ theme: 'light', sidebarMode: 'full' });
        await service.commit();

        expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
        expect(localStorage.getItem(SIDEBAR_MODE_STORAGE_KEY)).toBe('full');
        expect(JSON.parse(saveAppearancePrefs.mock.calls[0][0])).toEqual({
            ...DEFAULT_APPEARANCE,
            theme: 'light',
            sidebarMode: 'full',
        });
    });

    it('lets account preferences override the local bootstrap and refreshes that fallback', () => {
        storedJson.set(JSON.stringify({ theme: 'light', sidebarMode: 'full' }));
        const service = TestBed.inject(AppearanceService);

        service.loadFromAuth();

        expect(service.prefs()).toMatchObject({ theme: 'light', sidebarMode: 'full' });
        expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
        expect(localStorage.getItem(SIDEBAR_MODE_STORAGE_KEY)).toBe('full');
    });

    it('does not replace an unsaved preview when a route guard loads the same account again', () => {
        storedJson.set(JSON.stringify({ theme: 'dark', sheetWidth: 'normal' }));
        const service = TestBed.inject(AppearanceService);
        service.loadFromAuth();

        service.preview({ theme: 'light', sheetWidth: 'full' });
        service.loadFromAuth();

        expect(service.prefs()).toMatchObject({ theme: 'light', sheetWidth: 'full' });
        expect(service.dirty()).toBe(true);
    });

    it('keeps a newer preview dirty while an older account write finishes', async () => {
        let finishFirst!: () => void;
        saveAppearancePrefs.mockImplementationOnce((json: string) => new Promise<void>(resolve => {
            finishFirst = () => {
                storedJson.set(json);
                resolve();
            };
        }));
        const service = TestBed.inject(AppearanceService);
        service.preview({ sheetWidth: 'wide' });
        const first = service.commit();
        await Promise.resolve();

        service.preview({ sheetWidth: 'full' });
        finishFirst();
        await first;

        expect(service.prefs().sheetWidth).toBe('full');
        expect(service.dirty()).toBe(true);
        await service.commit();
        expect(JSON.parse(saveAppearancePrefs.mock.calls[1][0]).sheetWidth).toBe('full');
        expect(service.dirty()).toBe(false);
    });

    it('serializes account writes while preserving the payload captured by each commit', async () => {
        let finishFirst!: () => void;
        let finishSecond!: () => void;
        saveAppearancePrefs
            .mockImplementationOnce((json: string) => new Promise<void>(resolve => {
                finishFirst = () => {
                    storedJson.set(json);
                    resolve();
                };
            }))
            .mockImplementationOnce((json: string) => new Promise<void>(resolve => {
                finishSecond = () => {
                    storedJson.set(json);
                    resolve();
                };
            }));
        const service = TestBed.inject(AppearanceService);

        service.preview({ sheetWidth: 'wide' });
        const first = service.commit();
        service.preview({ sheetWidth: 'full' });
        const second = service.commit();

        expect(saveAppearancePrefs).toHaveBeenCalledTimes(1);
        expect(JSON.parse(saveAppearancePrefs.mock.calls[0][0]).sheetWidth).toBe('wide');

        finishFirst();
        await first;
        await Promise.resolve();

        expect(saveAppearancePrefs).toHaveBeenCalledTimes(2);
        expect(JSON.parse(saveAppearancePrefs.mock.calls[1][0]).sheetWidth).toBe('full');
        expect(service.dirty()).toBe(true);

        finishSecond();
        await second;
        expect(service.dirty()).toBe(false);
    });

    it('scores a colour against the paper and picks the ink from its luminance', () => {
        expect(contrastRatio('#000000', '#FFFFFF')).toBeCloseTo(21, 5);
        expect(contrastRatio('#FFFFFF', '#000000')).toBeCloseTo(21, 5);
        expect(accentContrast('#F1EADA', 'light')).toBeCloseTo(1, 5);
        expect(accentContrast('not a colour', 'light')).toBe(0);
        for (const preset of ACCENT_PRESETS) {
            expect(accentContrast(preset.hex, 'light')).toBeGreaterThanOrEqual(4.5);
            expect(accentContrast(preset.night, 'dark')).toBeGreaterThanOrEqual(4.5);
            expect(accentInk(preset.hex)).toBe('light');
        }
        expect(accentInk(PAPER_SURFACE.light)).toBe('dark');
        expect(accentInk('#7E7E7E')).toBe('dark');
        expect(accentContrast('#7E7E7E', 'light')).toBeGreaterThanOrEqual(ACCENT_MIN_CONTRAST);
    });

    it('keeps a custom accent that clears the floor and drops one that does not', () => {
        expect(storedAccent('#204060', 'light')).toBe('#204060');
        expect(storedAccent('#204060', 'light')).toBe(resolveAccent('#204060', 'light'));
        expect(storedAccent('#e0c060', 'light')).toBe(ACCENT_PRESETS[0].hex);
        expect(storedAccent('teal', 'dark')).toBe(ACCENT_PRESETS[0].hex);
        expect(storedAccent(ACCENT_PRESETS[2].hex.toLowerCase(), 'dark')).toBe(ACCENT_PRESETS[2].hex);
        expect(resolveAccent(ACCENT_PRESETS[2].hex, 'dark')).toBe(ACCENT_PRESETS[2].night);
        // Night paper is deeper than day paper, so the same colour can pass one and fail the other.
        expect(accentContrast('#7E7E7E', 'dark')).toBeLessThan(ACCENT_MIN_CONTRAST);
        expect(storedAccent('#7E7E7E', 'dark')).toBe(ACCENT_PRESETS[0].hex);
    });

    it('paints a custom accent on :root with the ink its luminance calls for', () => {
        storedJson.set(JSON.stringify({ theme: 'light', accentLight: '#204060', accentDark: '#e0c060' }));
        const service = TestBed.inject(AppearanceService);
        service.loadFromAuth();

        expect(service.prefs()).toMatchObject({ accentLight: '#204060', accentDark: ACCENT_PRESETS[0].hex });
        const injected = () => document.getElementById('__appearance-accent')?.textContent ?? '';
        expect(injected()).toContain(':root{--accent:#204060;--accent-ink:var(--sheet)}');
        expect(injected()).toContain(`:root[data-theme="dark"]{--accent:${ACCENT_PRESETS[0].night};--accent-ink:var(--sheet)}`);

        service.preview({ accentLight: '#7E7E7E' });
        expect(injected()).toContain(':root{--accent:#7E7E7E;--accent-ink:var(--text)}');

        service.preview({ accentLight: ACCENT_PRESETS[0].hex });
        expect(injected()).toBe('');
    });

    it('maps the four sheet measures to 640, 760, 960 and fluid', () => {
        expect(SHEET_WIDTH_PX).toEqual({ narrow: 640, normal: 760, wide: 960, full: null });
    });
});
