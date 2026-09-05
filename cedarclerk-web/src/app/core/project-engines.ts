// Mirrors CedarClerk.Core.ProjectEngines / ProjectPlatforms — the closed vocabularies a project's
// toolchain and targets are stored as. The server refuses any key outside these lists, so the
// pickers offer exactly them; labels live in i18n under projects.engines / projects.platforms.
export type ProjectEngine =
    | 'unity' | 'unreal' | 'godot' | 'gamemaker' | 'construct' | 'renpy'
    | 'rpgmaker' | 'love2d' | 'defold' | 'monogame' | 'bevy' | 'custom';

export const PROJECT_ENGINES: readonly ProjectEngine[] = [
    'unity', 'unreal', 'godot', 'gamemaker', 'construct', 'renpy',
    'rpgmaker', 'love2d', 'defold', 'monogame', 'bevy', 'custom',
];

export type ProjectPlatform =
    | 'windows' | 'mac' | 'linux' | 'web' | 'ios' | 'android'
    | 'switch' | 'playstation' | 'xbox' | 'quest';

export const PROJECT_PLATFORMS: readonly ProjectPlatform[] = [
    'windows', 'mac', 'linux', 'web', 'ios', 'android', 'switch', 'playstation', 'xbox', 'quest',
];

/** The server's own order, unknowns and duplicates dropped — what ProjectPlatforms.Parse does. */
export function normalizePlatforms(keys: readonly string[]): ProjectPlatform[] {
    const set = new Set(keys);
    return PROJECT_PLATFORMS.filter(p => set.has(p));
}
