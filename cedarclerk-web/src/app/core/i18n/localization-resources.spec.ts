import { DICTIONARY_LOADERS, initialInterfaceLanguage } from '@localization/dictionaries';
import { CONTENT_LANGUAGES, INTERFACE_DICTIONARIES, endonymOf } from '@localization/languages';
import { en } from '@localization/en';
import { fmt } from '@localization/format';

describe('shared localization resources', () => {
    it('can load every interface dictionary advertised by the shared catalog', async () => {
        expect([...INTERFACE_DICTIONARIES].sort()).toEqual(['en', ...Object.keys(DICTIONARY_LOADERS)].sort());
        for (const loader of Object.values(DICTIONARY_LOADERS)) {
            const dict = await loader();
            expect(dict.common.save).toBe('Сохранить');
            expect(Object.keys(dict).sort()).toEqual(Object.keys(en).sort());
        }
    });

    it('keeps content languages separate from the available interface translations', () => {
        expect(CONTENT_LANGUAGES).toHaveLength(9);
        expect(endonymOf('ka')).toBe('ქართული');
        expect(endonymOf('xx')).toBe('XX');
        expect(initialInterfaceLanguage('de', 'en-US')).toBe('en');
        expect(initialInterfaceLanguage('ru', 'en-US')).toBe('ru');
        expect(initialInterfaceLanguage(null, 'ru-RU')).toBe('ru');
    });

    it('preserves zero values and leaves unknown interpolation fields visible', () => {
        expect(fmt('{n}: {name} {unknown}', { n: 0, name: 'Test' })).toBe('0: Test {unknown}');
    });
});
