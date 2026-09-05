import { DEFAULT_APPEARANCE } from './appearance.service';
import { AREA_PRESETS, areaPresetPatch, matchAreaPreset } from './area-presets';

describe('area presets', () => {
    it('names four platforms and a default sheet matches none of them', () => {
        expect(AREA_PRESETS.map(p => p.id)).toEqual(['telegram', 'iphone', 'ipad', 'blog']);
        expect(matchAreaPreset(DEFAULT_APPEARANCE)).toBeNull();
    });

    it('is a macro over the sheet measure, text size and line height and nothing else', () => {
        for (const { id, patch } of AREA_PRESETS) {
            const applied = areaPresetPatch(id, DEFAULT_APPEARANCE);
            expect(Object.keys(applied).sort()).toEqual(['fontSize', 'lineHeight', 'sheetWidth']);
            expect(applied).toEqual(patch);
            expect(matchAreaPreset({ ...DEFAULT_APPEARANCE, ...applied })).toBe(id);
        }
        expect(areaPresetPatch('blog', DEFAULT_APPEARANCE)).toEqual({ sheetWidth: 'wide', fontSize: 17, lineHeight: 1.75 });
    });

    it('leaves Departure Mono at its own pixel size', () => {
        const departure = { ...DEFAULT_APPEARANCE, typeface: 'departure' as const, fontSize: 22 };
        const applied = areaPresetPatch('telegram', departure);
        expect(applied).toEqual({ sheetWidth: 'narrow', lineHeight: 1.45 });
        expect(matchAreaPreset({ ...departure, ...applied })).toBe('telegram');
    });
});
