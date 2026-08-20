// A stable, seeded fill per identity rather than a random one, so an avatar does not shuffle on
// reload. The colours themselves are --avatar-1..N in styles.scss: bound through
// [style.background] they never pass through a stylesheet, so a hex here would be a painted
// colour check-contrast.mjs cannot see.
export const AVATAR_FILL_COUNT = 6;

export function avatarFill(seed: string | null): string {
    let hash = 0;
    for (let i = 0; i < (seed?.length ?? 0); i++) hash = (hash * 31 + seed!.charCodeAt(i)) >>> 0;
    return `var(--avatar-${(hash % AVATAR_FILL_COUNT) + 1})`;
}

export function avatarInitial(seed: string | null): string {
    return (seed?.[0] ?? '?').toUpperCase();
}
