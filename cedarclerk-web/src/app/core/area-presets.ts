import { AppearancePrefs } from './appearance.service';

export type AreaPresetId = 'telegram' | 'iphone' | 'ipad' | 'blog';

export type AreaPresetPatch = Pick<AppearancePrefs, 'sheetWidth' | 'fontSize' | 'lineHeight'>;

// A preset is a macro over the sheet controls that already exist, not a fifth setting: it sets
// the measure, text size and line height together so the sheet reads roughly like the surface
// the post is headed for. Density is a page mode (ADR-071) and the typeface is the author's
// (ADR-073), so neither is touched. Blog is the sheet at --fs-read/--lh-read on the post column.
export const AREA_PRESETS: readonly { id: AreaPresetId; patch: AreaPresetPatch }[] = [
    { id: 'telegram', patch: { sheetWidth: 'narrow', fontSize: 15, lineHeight: 1.45 } },
    { id: 'iphone', patch: { sheetWidth: 'narrow', fontSize: 17, lineHeight: 1.5 } },
    { id: 'ipad', patch: { sheetWidth: 'normal', fontSize: 17, lineHeight: 1.6 } },
    { id: 'blog', patch: { sheetWidth: 'wide', fontSize: 17, lineHeight: 1.75 } },
];

// Departure Mono only renders at its two pixel sizes, so a preset leaves that size alone.
export function areaPresetPatch(id: AreaPresetId, prefs: AppearancePrefs): Partial<AppearancePrefs> {
    const preset = AREA_PRESETS.find(p => p.id === id);
    if (!preset) return {};
    const { fontSize, ...rest } = preset.patch;
    return prefs.typeface === 'departure' ? rest : { ...rest, fontSize };
}

export function matchAreaPreset(prefs: AppearancePrefs): AreaPresetId | null {
    const match = AREA_PRESETS.find(({ patch }) =>
        patch.sheetWidth === prefs.sheetWidth
        && (prefs.typeface === 'departure' || patch.fontSize === prefs.fontSize)
        && Math.abs(patch.lineHeight - prefs.lineHeight) < 0.001);
    return match?.id ?? null;
}
