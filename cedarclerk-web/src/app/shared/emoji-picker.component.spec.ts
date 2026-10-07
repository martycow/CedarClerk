import { describe, expect, it } from 'vitest';
import { EMOJI_DATA_URL, emojiDataLocale } from './emoji-picker.component';

describe('emoji picker data', () => {
    it('serves the Russian set to Russian and falls back to English for every other language', () => {
        expect(emojiDataLocale('ru')).toBe('ru');
        expect(emojiDataLocale('en')).toBe('en');
        expect(emojiDataLocale('de')).toBe('en');
    });

    it('points at the self-hosted files angular.json copies, never a CDN', () => {
        expect(EMOJI_DATA_URL('ru')).toBe('assets/emoji/ru/data.json');
        expect(EMOJI_DATA_URL('en')).not.toMatch(/^https?:/);
    });
});
