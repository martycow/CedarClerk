import { TestBed } from '@angular/core/testing';
import { en } from '@localization/en';
import { LocaleService } from './locale.service';

describe('LocaleService', () => {
    let originalLanguage: string | null;
    beforeEach(() => {
        originalLanguage = document.documentElement.getAttribute('lang');
        localStorage.removeItem('cedar-ui-lang');
        localStorage.removeItem('cedar-pseudo');
    });

    afterEach(() => {
        localStorage.removeItem('cedar-ui-lang');
        localStorage.removeItem('cedar-pseudo');
        if (originalLanguage === null) document.documentElement.removeAttribute('lang');
        else document.documentElement.setAttribute('lang', originalLanguage);
    });

    it('starts with English in memory and resolves ready() at once', async () => {
        const locale = TestBed.inject(LocaleService);
        expect(locale.uiLang()).toBe('en');
        expect(locale.t()).toBe(en);
        await locale.ready();
        expect(locale.t().common.save).toBe('Save');
    });

    it('falls back to English until the Russian chunk arrives, then swaps', async () => {
        const locale = TestBed.inject(LocaleService);
        locale.set('ru');
        expect(locale.uiLang()).toBe('ru');
        expect(document.documentElement.lang).toBe('ru');
        expect(localStorage.getItem('cedar-ui-lang')).toBe('ru');
        expect(locale.t().common.save).toBe('Save');
        await locale.ready();
        expect(locale.t().common.save).toBe('Сохранить');
        locale.set('en');
        expect(locale.t().common.save).toBe('Save');
        locale.set('ru');
        expect(locale.t().common.save).toBe('Сохранить');
    });

    it('boots straight into the cached language once its chunk is loaded', async () => {
        localStorage.setItem('cedar-ui-lang', 'ru');
        const locale = TestBed.inject(LocaleService);
        expect(locale.uiLang()).toBe('ru');
        await locale.ready();
        expect(locale.t().common.cancel).toBe('Отмена');
    });

    it('preloadAll() makes every other dictionary switch-ready', async () => {
        const locale = TestBed.inject(LocaleService);
        await locale.preloadAll();
        locale.set('ru');
        expect(locale.t().common.close).toBe('Закрыть');
    });

    it('wraps the loaded dictionary when the pseudo-locale is on', async () => {
        const locale = TestBed.inject(LocaleService);
        locale.setPseudo(true);
        expect(locale.t().common.save).toMatch(/^⟦.*⟧$/);
        locale.set('ru');
        await locale.ready();
        expect(locale.t().common.save).toContain('Сохранить');
        expect(locale.t().common.save).toMatch(/^⟦.*⟧$/);
    });
});
