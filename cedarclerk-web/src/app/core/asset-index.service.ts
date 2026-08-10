import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IconName } from '../shared/icon-data.generated';

// T-122 (ADR-107) — the index of a project's local files. **Paths and metadata only; the bytes
// never move.** Deliberately not the same thing as `assets.service.ts`, which uploads post media
// into CEDAR_DATA_DIR under the plan's storage quota.
//
// Scanning is available only where the server may read its own disk — the desktop shell sets
// `Cedar:AssetIndex:Enabled`, the hosted Pi does not, and `auth.assetIndex()` carries the answer.

export type AssetKind = 'image' | 'model' | 'audio' | 'video' | 'font' | 'text' | 'other';
export const ASSET_KINDS: AssetKind[] = ['image', 'model', 'audio', 'video', 'font', 'text', 'other'];

export const ASSET_KIND_ICONS: Record<AssetKind, IconName> = {
    image: 'image',
    model: 'cube',
    audio: 'waveform',
    video: 'film-slate',
    font: 'text-aa',
    text: 'file-text',
    other: 'file',
};

export interface AssetEntry {
    id: string;
    relativePath: string;
    fileName: string;
    extension: string;
    kind: AssetKind;
    sizeBytes: number;
    modifiedAt: string;
    indexedAt: string;
    /** Set means the last scan did not find the file. **Missing is not deleted.** */
    missingSince: string | null;
}

export interface AssetDetail extends AssetEntry {
    /** Only meaningful on the machine that indexed it — what "Reveal in file manager" needs. */
    fullPath: string | null;
}

export interface AssetPage {
    rootPath: string | null;
    indexedAt: string | null;
    /** Rows matching the current filter. */
    total: number;
    /** Rows in the project, whatever the filter. */
    totalIndexed: number;
    missingCount: number;
    byKind: Partial<Record<AssetKind, number>>;
    items: AssetEntry[];
}

export interface ScanState {
    available: boolean;
    running: boolean;
    status?: 'counting' | 'indexing' | 'completed' | 'failed' | 'cancelled';
    rootPath?: string;
    total?: number;
    processed?: number;
    indexed?: number;
    markedMissing?: number;
    /** Folders the walk could not open — reported rather than swallowed. */
    unreadable?: number;
    error?: string | null;
    startedAt?: string;
    finishedAt?: string | null;
}

export interface AssetQuery {
    kind?: AssetKind | null;
    search?: string;
    missing?: boolean;
    skip?: number;
    take?: number;
}

@Injectable({ providedIn: 'root' })
export class AssetIndexService {
    private http = inject(HttpClient);

    list(projectId: string, query: AssetQuery = {}) {
        const params = new URLSearchParams();
        if (query.kind) params.set('kind', query.kind);
        if (query.search) params.set('search', query.search);
        if (query.missing) params.set('missing', 'true');
        if (query.skip) params.set('skip', String(query.skip));
        if (query.take) params.set('take', String(query.take));
        const suffix = params.toString() ? `?${params}` : '';
        return firstValueFrom(this.http.get<AssetPage>(`/api/projects/${projectId}/assets${suffix}`));
    }

    get(projectId: string, assetId: string) {
        return firstValueFrom(this.http.get<AssetDetail>(`/api/projects/${projectId}/assets/${assetId}`));
    }

    /** Without a path, re-scans the folder already chosen. With one, replaces it and re-indexes. */
    startScan(projectId: string, path?: string) {
        return firstValueFrom(this.http.post<ScanState>(`/api/projects/${projectId}/assets/index`, { path: path ?? null }));
    }

    scanState(projectId: string) {
        return firstValueFrom(this.http.get<ScanState>(`/api/projects/${projectId}/assets/index`));
    }

    cancelScan(projectId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/projects/${projectId}/assets/index`));
    }

    reindexOne(projectId: string, assetId: string) {
        return firstValueFrom(this.http.post<Partial<AssetEntry>>(`/api/projects/${projectId}/assets/${assetId}/reindex`, {}));
    }
}

/** 1.2 MB, 640 KB — short enough for a grid tile's caption. */
export function formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    const units = ['KB', 'MB', 'GB', 'TB'];
    let value = bytes / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
        value /= 1024;
        unit++;
    }
    return `${value >= 10 ? Math.round(value) : value.toFixed(1)} ${units[unit]}`;
}

/**
 * The desktop shell's bridge, when running inside it. Undefined in a browser — which is exactly how
 * the screen knows to show a path field instead of a folder picker.
 */
export interface CedarDesktopBridge {
    isDesktop: true;
    pickFolder(): Promise<string | null>;
    reveal(path: string): Promise<void>;
}

export function desktopBridge(): CedarDesktopBridge | undefined {
    return (window as unknown as { cedarDesktop?: CedarDesktopBridge }).cedarDesktop;
}
