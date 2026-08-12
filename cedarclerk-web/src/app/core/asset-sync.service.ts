import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CedarDesktopBridge, ScannedFile, desktopBridge } from './asset-index.service';
import { LocaleService } from './i18n/locale.service';
import { httpErrorMessage } from './http-error.util';

// ADR-117 — indexing a folder, from this machine into the cloud.
//
// ## Who does what, and why it is split this way
//
// The **agent** reads the disk: it walks the folder, stats every file, reads headers and renders
// previews. It is the only process that can — the files are here and the database is not.
//
// This **page** decides and uploads. It already holds a session cookie for the cloud, so the agent
// needs no credentials of its own and the shell does no authentication at all. The side effect is
// worth more than the saving: the work happens inside an ordinary screen with a progress bar and a
// cancel button, instead of in a background process nobody can ask about.
//
// ## Two phases, and the second one is resumable
//
//   1. **Index** — walk, upload in pages of 500, then sweep. Fast: rows are small.
//   2. **Previews** — ask the cloud what it still lacks, render each, upload in batches of 20.
//      Slow, and that is inherent: every image has to be decoded once.
//
// Phase 2 is driven by the server's own answer (`thumbs/pending`) rather than by a list built here,
// which is what makes it restartable. Interrupt it at the eight-thousandth file, close the app, come
// back next week — it continues from what is missing instead of redoing what is done. That property is
// the whole reason Marty's "every preview, no limit" is affordable at all.

const BATCH_SIZE = 500;
const THUMB_BATCH = 20;
const THUMB_PENDING_PAGE = 200;

export type SyncPhase = 'idle' | 'counting' | 'indexing' | 'sweeping' | 'previews' | 'done' | 'cancelled' | 'failed';

export interface SyncProgress {
    phase: SyncPhase;
    /** Files the counting pass found. 0 while still counting. */
    total: number;
    /** Files described and uploaded so far. */
    indexed: number;
    /** Previews uploaded this run. */
    thumbsDone: number;
    /** Previews still outstanding, as the server last reported. */
    thumbsRemaining: number;
    /** Bytes of preview uploaded this run — the number that makes "no limit" concrete. */
    thumbBytes: number;
    /** Folders the walk could not open. Reported, never swallowed. */
    unreadable: number;
    markedMissing: number;
    error: string | null;
}

const IDLE: SyncProgress = {
    phase: 'idle', total: 0, indexed: 0, thumbsDone: 0, thumbsRemaining: 0,
    thumbBytes: 0, unreadable: 0, markedMissing: 0, error: null,
};

/**
 * Marks an error as "the local agent could not do that".
 *
 * The agent answers in English only, and deliberately: its sole client is the shell's main process,
 * which is English by decision (ADR-116). So its prose must never reach the screen — the page cannot
 * translate a sentence the agent invented. This class of failure is carried as a *kind*, and the
 * screen supplies its own wording.
 */
class AgentUnavailable extends Error {
    constructor(readonly detail: string) { super(detail); }
}

@Injectable({ providedIn: 'root' })
export class AssetSyncService {
    private http = inject(HttpClient);
    private t = inject(LocaleService).t;

    private state = signal<SyncProgress>(IDLE);
    readonly progress = this.state.asReadonly();

    readonly running = computed(() => {
        const phase = this.state().phase;
        return phase === 'counting' || phase === 'indexing' || phase === 'sweeping' || phase === 'previews';
    });

    /** 0–100 across the index phase. The preview phase reports its own counts instead: the two have
     *  wildly different per-item costs, and one bar spanning both would crawl and then leap. */
    readonly indexPercent = computed(() => {
        const s = this.state();
        if (!s.total) return 0;
        return Math.min(100, Math.round((s.indexed / s.total) * 100));
    });

    private cancelled = false;
    private activeScanId: string | null = null;

    /** Whether this client can index at all — that is, whether it is the desktop shell. */
    get bridge(): CedarDesktopBridge | undefined {
        return desktopBridge();
    }

    cancel() {
        this.cancelled = true;
        if (this.activeScanId) void this.bridge?.scanCancel(this.activeScanId);
    }

    reset() {
        this.state.set(IDLE);
    }

    /**
     * Indexes `root` into `projectId`, then fills in previews.
     *
     * Everything already uploaded survives a cancel or a failure — a half-finished index is still an
     * index, which is the same promise the local scanner made before ADR-117.
     */
    async run(projectId: string, root: string) {
        const bridge = this.bridge;
        if (!bridge) {
            this.patch({ phase: 'failed', error: this.t().projects.assets.desktopRequired });
            return;
        }

        this.cancelled = false;
        this.state.set({ ...IDLE, phase: 'counting' });

        try {
            const machine = await bridge.machine();
            // Declares the folder and the machine, and hands back the instant the scan began. That
            // instant is what the sweep uses to decide which rows the walk did not reach.
            const { scanStartedAt } = await firstValueFrom(this.http.put<{ scanStartedAt: string; replaced: boolean }>(
                `/api/projects/${projectId}/assets/source`,
                { machineId: machine.id, machineName: machine.name, rootPath: root }));

            // Wrapped, because everything the shell rejects here arrives as its own English prose —
            // an ungranted folder, a missing agent, a path that has since been deleted.
            const started = await bridge.scan(root)
                .catch(e => { throw new AgentUnavailable(e instanceof Error ? e.message : String(e)); });
            this.activeScanId = started.scanId;

            await this.indexPhase(projectId, started.scanId, bridge);
            if (this.cancelled) { this.finish('cancelled'); return; }

            this.patch({ phase: 'sweeping' });
            const swept = await firstValueFrom(this.http.post<{ markedMissing: number }>(
                `/api/projects/${projectId}/assets/sweep`,
                { scanStartedAt, unreadable: this.state().unreadable }));
            this.patch({ markedMissing: swept.markedMissing });

            await this.previewPhase(projectId, root, bridge);
            this.finish(this.cancelled ? 'cancelled' : 'done');
        } catch (e) {
            // Two sources, two treatments. The cloud's refusals are already localised (ErrorMessages),
            // so they are shown as they came. The agent's are not and never will be, so they are
            // replaced with our own wording and the original goes to the console for diagnosis.
            if (e instanceof AgentUnavailable) {
                console.warn('[cedar] local file agent:', e.detail);
                this.patch({ phase: 'failed', error: this.t().projects.assets.agentUnavailable });
            } else {
                this.patch({ phase: 'failed', error: httpErrorMessage(e, this.t().projects.assets.loadFailed) });
            }
        } finally {
            this.activeScanId = null;
        }
    }

    /**
     * Uploads described files while the walk is still walking.
     *
     * It follows the walk rather than waiting for it: the agent appends to its list as it goes, and a
     * page of 500 goes up the moment it exists. On a big folder that overlaps the two costs — disk
     * traversal and network — instead of paying them one after the other.
     */
    private async indexPhase(projectId: string, scanId: string, bridge: CedarDesktopBridge) {
        let uploaded = 0;

        for (;;) {
            if (this.cancelled) return;

            const page = await bridge.scanFiles(scanId, uploaded, BATCH_SIZE);
            if (!page) throw new AgentUnavailable('scanFiles returned nothing — the agent stopped answering.');

            const progress = await bridge.scanProgress(scanId);
            if (progress) {
                this.patch({
                    phase: progress.status === 'counting' ? 'counting' : 'indexing',
                    total: progress.total,
                    unreadable: progress.unreadable,
                });
                if (progress.status === 'failed') throw new AgentUnavailable(progress.error ?? 'the walk failed');
            }

            if (page.files.length > 0) {
                await firstValueFrom(this.http.post(`/api/projects/${projectId}/assets/batch`, { files: page.files }));
                uploaded += page.files.length;
                this.patch({ indexed: uploaded });
                // Straight on to the next page: there is more waiting already.
                continue;
            }

            // Nothing new to send. If the walk has stopped, so have we; otherwise it is still deeper in
            // the tree than we are, and a short wait is cheaper than spinning on it.
            const status = page.status ?? progress?.status;
            if (status && status !== 'counting' && status !== 'scanning') return;
            await new Promise(r => setTimeout(r, 200));
        }
    }

    /**
     * Renders and uploads every preview the cloud is missing.
     *
     * Driven entirely by `thumbs/pending`, which is what makes it idempotent: running it twice does not
     * re-upload anything, and running it after an interruption picks up the remainder. A file that
     * cannot produce a preview is skipped and **not** retried in this run, or an undecodable PSD would
     * be asked for again on every loop forever.
     */
    private async previewPhase(projectId: string, root: string, bridge: CedarDesktopBridge) {
        this.patch({ phase: 'previews' });
        const hopeless = new Set<string>();

        for (;;) {
            if (this.cancelled) return;

            const pending = await firstValueFrom(this.http.get<{ items: { id: string; relativePath: string }[]; remaining: number }>(
                `/api/projects/${projectId}/assets/thumbs/pending?take=${THUMB_PENDING_PAGE}`));
            this.patch({ thumbsRemaining: pending.remaining });

            const todo = pending.items.filter(item => !hopeless.has(item.id));
            if (todo.length === 0) return;

            for (let i = 0; i < todo.length; i += THUMB_BATCH) {
                if (this.cancelled) return;

                const slice = todo.slice(i, i + THUMB_BATCH);
                const form = new FormData();
                let bytes = 0;
                let attached = 0;

                for (const item of slice) {
                    const base64 = await bridge.thumb(this.absolute(root, item.relativePath));
                    if (!base64) { hopeless.add(item.id); continue; }
                    const blob = base64ToBlob(base64);
                    bytes += blob.size;
                    // The part's name is the asset id — that is how the server knows which row each
                    // preview belongs to without a parallel list to keep in step.
                    form.append(item.id, blob, `${item.id}.jpg`);
                    attached++;
                }

                if (attached === 0) continue;

                const result = await firstValueFrom(this.http.put<{ stored: number }>(
                    `/api/projects/${projectId}/assets/thumbs`, form));
                this.patch({
                    thumbsDone: this.state().thumbsDone + result.stored,
                    thumbBytes: this.state().thumbBytes + bytes,
                });
            }
        }
    }

    /** The path on the machine holding the files. Windows separators, because that is where it goes. */
    private absolute(root: string, relativePath: string) {
        const separator = root.includes('\\') ? '\\' : '/';
        return `${root}${root.endsWith(separator) ? '' : separator}${relativePath.split('/').join(separator)}`;
    }

    private patch(part: Partial<SyncProgress>) {
        this.state.update(s => ({ ...s, ...part }));
    }

    private finish(phase: SyncPhase) {
        this.patch({ phase });
    }
}

/**
 * The agent hands previews over as base64 — inert data by construction, rather than something the page
 * could mistake for executable. Turned back into bytes here so the upload is a real multipart file
 * instead of a JSON string a third the size again.
 */
function base64ToBlob(base64: string): Blob {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return new Blob([bytes], { type: 'image/jpeg' });
}

export type { ScannedFile };
