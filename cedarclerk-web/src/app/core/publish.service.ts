import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** What a network accepts, as the server describes it (T-086). */
export interface PublishCapabilities {
    network: string;
    maxCharacters: number | null;
    maxMediaItems: number;
    maxImageBytes: number | null;
    supportsVideo: boolean;
    supportsAudio: boolean;
    supportsRichText: boolean;
    supportsHeadings: boolean;
    supportsLists: boolean;
    supportsTables: boolean;
    supportsCodeBlocks: boolean;
    supportsMath: boolean;
    supportsLinkPreview: boolean;
    supportsAltText: boolean;
    supportsThreads: boolean;
    postsHavePublicUrls: boolean;
}

export interface PublishAccount {
    id: string;
    network: string;
    displayName: string;
    remoteId: string;
    lastPublishedAt: string | null;
    lastError: string | null;
    /** ADR-299 — when the stored token stops working; only LinkedIn, whose token cannot be refreshed, answers this. */
    expiresAt?: string | null;
}

export interface PublishNetwork {
    network: string;
    capabilities: PublishCapabilities;
    accounts: PublishAccount[];
}

export type PublishJobStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Unknown';

export interface PublishJob {
    id: string;
    network: string;
    targetId: string;
    language?: string;
    status: PublishJobStatus;
    attempts?: number;
    /** T-106 — which message of a thread this job sends; 0 for a single-message publish. */
    partIndex?: number;
    partCount?: number;
    error?: string | null;
    remoteId?: string | null;
    publicUrl?: string | null;
}

/** A post that exists on a network, as the queue recorded it. */
export interface PublishedPost {
    draftId: string;
    network: string;
    language: string;
    targetId: string;
    publicUrl: string;
    remoteId: string | null;
    /** More than 1 means the link is the head of a thread of that many messages. */
    partCount: number;
    finishedAt: string | null;
}

/** One thing that went out: a Telegram send, a job on another network, or the blog publication. */
export interface PublishEvent {
    draftId: string;
    draftTitle: string;
    network: string;
    targetName: string | null;
    publishedAt: string;
    publicUrl: string | null;
    partCount: number;
    /** A Sent scheduled post already stands for this send. */
    scheduled: boolean;
}

/** One message of a thread, as the preview describes it (T-106). */
export interface ThreadPart {
    index: number;
    startsWith: string | null;
    characters: number;
    mediaCount: number;
    cutReason: 'heading' | 'size' | 'media' | 'end';
}

/** The author's own text for one network and language (T-087, ADR-077). */
export interface TargetText { network: string; language: string; text: string; }

@Injectable({ providedIn: 'root' })
export class PublishService {
    private http = inject(HttpClient);

    networks() {
        return firstValueFrom(this.http.get<PublishNetwork[]>('/api/publish/networks'));
    }

    /**
     * An app password, never the account password — Bluesky issues them per application and they
     * are revocable from its own settings, which is what makes storing one defensible.
     */
    connectBluesky(handle: string, appPassword: string, service?: string) {
        return firstValueFrom(this.http.post<{ handle: string; did: string }>(
            '/api/publish/bluesky/connect', { handle, appPassword, service }));
    }

    /**
     * T-110 — X connects over OAuth: the server answers with x.com's authorize URL and the browser
     * goes there; the callback lands the user back in the app with the target row created.
     */
    /** T-161 — a channel webhook pasted whole; the server verifies it against Discord before storing. */
    connectDiscord(webhookUrl: string) {
        return firstValueFrom(this.http.post<{ name: string }>(
            '/api/publish/discord/connect', { webhookUrl }));
    }

    connectX() {
        return firstValueFrom(this.http.post<{ url: string }>('/api/publish/x/connect', {}));
    }

    connectLinkedIn() {
        return firstValueFrom(this.http.post<{ url: string }>('/api/publish/linkedin/connect', {}));
    }

    disconnect(targetId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/publish/targets/${targetId}`));
    }

    texts(draftId: string) {
        return firstValueFrom(this.http.get<{ texts: TargetText[] }>(`/api/publish/texts/${draftId}`));
    }

    /** An empty text clears the override, and the network's own teaser takes over again. */
    saveText(draftId: string, network: string, language: string, text: string) {
        return firstValueFrom(this.http.put<void>(`/api/publish/texts/${draftId}`, { network, language, text }));
    }

    publishToTarget(draftId: string, targetId: string, language?: string) {
        return firstValueFrom(this.http.post<{ messageId: number | null }>(
            '/api/posts/publish-target', { draftId, targetId, language }));
    }

    /**
     * T-090 — queues a publication instead of waiting for one. The request returns as soon as the
     * rows exist; what the networks do afterwards is read from `jobs()`. This is what stopped a
     * heavy post from timing out at the proxy while Telegram downloaded 30MB from us (ADR-080/081).
     */
    queue(draftId: string, targetIds: string[], language?: string, confirmedFingerprint?: string, splitIntoThread = false,
          options: { silent?: boolean; pin?: boolean } = {}) {
        // silent/pin ride along for the Telegram jobs (Wave 2 item 11); a server that does not
        // bind them yet simply ignores the extra fields.
        return firstValueFrom(this.http.post<{ jobs: PublishJob[] }>(
            '/api/publish/jobs', {
                draftId, targetIds, language, confirmedFingerprint, splitIntoThread,
                silent: options.silent ?? false, pin: options.pin ?? false,
            }));
    }

    /**
     * Where this owner's posts went — one row per (draft, network, language, account), newest
     * first. Read by the Publishing Manager: before this existed the only place a published URL was
     * ever shown was the editor's progress checklist, and closing it lost the link for good.
     */
    published() {
        return firstValueFrom(this.http.get<{ posts: PublishedPost[] }>('/api/publish/published'));
    }

    /** Every publication, newest first; Telegram rows are the channel's own send records (ADR-317). */
    events(project?: string | null) {
        return firstValueFrom(this.http.get<{ events: PublishEvent[] }>(
            '/api/publish/events', { params: project ? { project } : {} }));
    }

    jobs(draftId: string) {
        return firstValueFrom(this.http.get<{ jobs: PublishJob[] }>(`/api/publish/jobs?draftId=${draftId}`));
    }

    /** T-106 — what the thread would look like, before anything is sent. */
    threadPreview(draftId: string, network: string, language?: string) {
        const lang = language ? `&language=${language}` : '';
        return firstValueFrom(this.http.get<{ parts: ThreadPart[] }>(
            `/api/publish/thread-preview?draftId=${draftId}&network=${network}${lang}`));
    }
}
