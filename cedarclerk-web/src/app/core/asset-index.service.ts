import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IconName } from '../shared/icon-data.generated';
import { SortDirection } from './collection-query';

// T-122 (ADR-107) — the index of a project's local files. **Paths, metadata and small previews; the
// asset's own bytes never move.** Deliberately not the same thing as `assets.service.ts`, which
// uploads post media into CEDAR_DATA_DIR under the plan's storage quota.
//
// **Reading works everywhere; indexing needs the desktop (ADR-117).** The index is pushed up from the
// machine holding the files — see `asset-sync.service.ts` — so whether a folder can be scanned is a
// fact about *this client*: it has `window.cedarDesktop` or it does not. There is no server flag to
// ask any more, because no server reads a disk.

export type AssetKind = 'image' | 'model' | 'audio' | 'video' | 'font' | 'text' | 'other';
export type AssetSort = 'path' | 'type' | 'size' | 'modified' | 'status';
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

const ARCHIVE_EXTENSIONS = new Set(['zip', 'rar', '7z', 'tar', 'gz', 'unitypackage']);
const CODE_EXTENSIONS = new Set([
    'cs', 'js', 'ts', 'py', 'gd', 'lua', 'cpp', 'c', 'h', 'hpp', 'rs', 'java', 'kt', 'swift',
    'json', 'xml', 'yaml', 'yml', 'toml', 'shader', 'hlsl', 'glsl', 'usf', 'ush',
]);

/** The picture a file gets when it has no preview: by format where one says more than the kind. */
export function assetIcon(asset: { kind: AssetKind; extension: string }): IconName {
    const extension = asset.extension.replace(/^\./, '').toLowerCase();
    if (ARCHIVE_EXTENSIONS.has(extension)) return 'file-zip';
    if (CODE_EXTENSIONS.has(extension)) return 'file-code';
    return ASSET_KIND_ICONS[asset.kind];
}

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
    // T-140 — what the file's own header said. Null where the kind has nothing to say (an image
    // has no duration) or the header could not be read. Only WAV reports a duration at all.
    width: number | null;
    height: number | null;
    durationMs: number | null;
    sampleRate: number | null;
    /**
     * A preview is stored **right now** — so an `<img>` pointed at it will not 404. Since ADR-117 this
     * means "the agent uploaded one", not "the server could make one on request".
     */
    hasThumbnail: boolean;
    /**
     * A preview is *possible* for this file, but has not arrived yet. A different fact from the one
     * above, and the screen needs both: "waiting for a preview" is honest, while the same placeholder
     * on a PSD — which nothing here can decode — would be a promise no pass will ever keep.
     */
    canHaveThumbnail: boolean;
}

/** Which machine holds a project's files. Null on projects indexed before ADR-117. */
export interface AssetSourceMachine {
    id: string;
    name: string | null;
}

/** One file as the desktop agent described it, on its way to the cloud. */
export interface ScannedFile {
    relativePath: string;
    fileName: string;
    extension: string;
    kind: AssetKind;
    sizeBytes: number;
    modifiedAt: string;
    width: number | null;
    height: number | null;
    durationMs: number | null;
    sampleRate: number | null;
}

/** A document the author has linked to an asset (T-141) — stated, never discovered. */
export interface LinkedDocument {
    id: string;
    title: string;
    documentType: string;
}

export interface AssetDetail extends AssetEntry {
    /** Only meaningful on the machine that indexed it — what "Reveal in file manager" needs. */
    fullPath: string | null;
    sourceMachine: AssetSourceMachine | null;
}

export interface AssetPage {
    rootPath: string | null;
    /**
     * The machine that indexed this folder. Compared against `cedarDesktop.machine()` to decide
     * whether the screen is showing files or fingerprints of files — a browser has no machine, so it
     * always shows fingerprints, which is the truth rather than a fallback.
     */
    sourceMachine: AssetSourceMachine | null;
    indexedAt: string | null;
    /** Rows matching the current filter. */
    total: number;
    /** Rows in the project, whatever the filter. */
    totalIndexed: number;
    missingCount: number;
    /** Previewable files still without a stored preview — what the pass owes. */
    thumbnailsPending: number;
    byKind: Partial<Record<AssetKind, number>>;
    items: AssetEntry[];
}

export interface AssetQuery {
    kind?: AssetKind | null;
    search?: string;
    missing?: boolean;
    sort?: AssetSort;
    direction?: SortDirection;
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
        if (query.sort) params.set('sort', query.sort);
        if (query.direction) params.set('direction', query.direction);
        if (query.skip) params.set('skip', String(query.skip));
        if (query.take) params.set('take', String(query.take));
        const suffix = params.toString() ? `?${params}` : '';
        return firstValueFrom(this.http.get<AssetPage>(`/api/projects/${projectId}/assets${suffix}`));
    }

    get(projectId: string, assetId: string) {
        return firstValueFrom(this.http.get<AssetDetail>(`/api/projects/${projectId}/assets/${assetId}`));
    }

    /** Uploaded by the desktop agent during indexing; 404 until one arrives (ADR-117). */
    thumbnailUrl(projectId: string, assetId: string) {
        return `/api/projects/${projectId}/assets/${assetId}/thumb`;
    }

    links(projectId: string, assetId: string) {
        return firstValueFrom(this.http.get<LinkedDocument[]>(`/api/projects/${projectId}/assets/${assetId}/links`));
    }

    addLink(projectId: string, assetId: string, draftId: string) {
        return firstValueFrom(this.http.post<void>(`/api/projects/${projectId}/assets/${assetId}/links/${draftId}`, {}));
    }

    removeLink(projectId: string, assetId: string, draftId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/projects/${projectId}/assets/${assetId}/links/${draftId}`));
    }

    /**
     * Pushes one file's current state, for the "Re-index file" button. The agent stats the file and
     * this sends the answer — the same batch endpoint a whole scan uses, with one row in it, rather
     * than a second way to update a row (which is how the two would drift apart).
     */
    pushOne(projectId: string, file: ScannedFile) {
        return firstValueFrom(this.http.post<{ ids: Record<string, string> }>(
            `/api/projects/${projectId}/assets/batch`, { files: [file] }));
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

/** m:ss — how a sound file's length is written everywhere else. Mirrors Core's WavHeader. */
export function formatDuration(ms: number): string {
    const total = Math.round(ms / 1000);
    return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

/** Progress of a walk in flight, as the agent reports it. */
export interface AgentScanState {
    scanId: string;
    status: 'counting' | 'scanning' | 'completed' | 'failed' | 'cancelled';
    running: boolean;
    rootPath: string;
    total: number;
    described: number;
    unreadable: number;
    error: string | null;
}

/**
 * The desktop shell's bridge, when running inside it. Undefined in a browser — which is exactly how
 * every screen knows it is looking at fingerprints rather than files (ADR-117).
 *
 * Nothing here writes, deletes or launches anything: the shell deliberately does not expose
 * `shell.openPath`, because the window now loads a remote origin and reading a folder is what the
 * feature needs while executing a file is what an attacker needs.
 */
export interface CedarDesktopBridge {
    isDesktop: true;
    /** This machine's stable id and its current hostname. */
    machine(): Promise<{ id: string; name: string }>;
    /** Opens the OS picker and grants the chosen folder to the agent. Null if cancelled. */
    pickFolder(): Promise<string | null>;
    scan(root: string): Promise<AgentScanState>;
    scanProgress(scanId: string): Promise<AgentScanState | null>;
    /** Readable while the walk is still running — that overlap is the point. */
    scanFiles(scanId: string, skip: number, take: number): Promise<{
        files: ScannedFile[];
        described: number;
        status: AgentScanState['status'];
        total: number;
    } | null>;
    scanCancel(scanId: string): Promise<boolean>;
    stat(root: string, relativePath: string): Promise<{ missing: boolean; file?: ScannedFile } | null>;
    /** One preview as base64 JPEG, or null when this file cannot have one. */
    thumb(fullPath: string): Promise<string | null>;
    /** Highlights the file in Explorer/Finder. Does not open it. */
    reveal(path: string): Promise<void>;
}

export function desktopBridge(): CedarDesktopBridge | undefined {
    return (window as unknown as { cedarDesktop?: CedarDesktopBridge }).cedarDesktop;
}
