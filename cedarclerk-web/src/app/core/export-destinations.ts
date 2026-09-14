import { BrandIconName } from '../shared/brand-icon.component';

/**
 * T-331 — the destinations an export preset can name, mirroring CedarClerk.Core.ExportDestinations:
 * the blog plus every publish network. Deliberately not the Export modal's wider `ExportDestination`
 * union, which also carries the copy targets and the not-yet-supported cards — those are step-3
 * surfaces, not places a post is sent to.
 */
export type ExportDestinationId = 'blog' | 'telegram' | 'bluesky' | 'x' | 'discord' | 'linkedin';

export const EXPORT_DESTINATIONS: ExportDestinationId[] = ['blog', 'telegram', 'bluesky', 'x', 'discord', 'linkedin'];

/**
 * The blog is ours and takes the app's own icon; the networks take their brand marks, the same
 * pairing the Export modal's destination cards already draw (`newspaper` + `app-brand-icon`).
 */
export const EXPORT_DESTINATION_BRANDS: Record<ExportDestinationId, BrandIconName | null> = {
    blog: null,
    telegram: 'telegram',
    bluesky: 'bluesky',
    x: 'twitter',
    discord: 'discord',
    linkedin: 'linkedin',
};
