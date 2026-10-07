import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// ADR-239 clause 10 — the Preview tab's two projections, read from the server and never sent.
// JSON mirrors CedarClerk.Core/TelegramPreviewProjection.cs; absent facts are omitted, never 0.
export type TelegramPreviewKind =
    | 'paragraph' | 'heading' | 'list' | 'code' | 'quote' | 'divider' | 'table' | 'math' | 'details' | 'footer'
    | 'photo' | 'video' | 'audio' | 'slideshow' | 'collage';

/** One list item with its marker — the order for a numbered list, the checkbox state for a task list (ADR-313). */
export interface TelegramPreviewListItem {
    text: string;
    order: number | null;
    hasCheckbox: boolean;
    isChecked: boolean;
}

export interface TelegramPreviewBlock {
    kind: TelegramPreviewKind;
    text: string;
    /** Media only: one entry for photo/video/audio, one per image for slideshow/collage. Relative `/media/…`. */
    urls: string[];
    caption: string | null;
    /** Lists only; absent or null on every other block. */
    items?: TelegramPreviewListItem[] | null;
}

export interface TelegramPreviewMessage {
    index: number;
    blocks: TelegramPreviewBlock[];
    characters: number;
    mediaCount: number;
    cutReason: 'heading' | 'size' | 'media' | 'end';
    /** The first words of the part; empty when it opens with media. */
    startsWith: string;
}

export interface TelegramPreviewButton { text: string; url: string; }

export interface TelegramPreview {
    language: string;
    messageCount: number;
    characters: number;
    maxCharactersPerMessage: number;
    maxMediaPerMessage: number;
    messages: TelegramPreviewMessage[];
    /** The CTA row Telegram attaches to the last message. */
    buttons: TelegramPreviewButton[];
    /** Past this many characters the client hides a post behind "Show more" (ADR-086, ADR-313). */
    foldAfterCharacters?: number;
}

export type MicroNetwork = 'x' | 'bluesky' | 'discord' | 'linkedin';

/** One post the way the network's builder would send it. Mirrors CedarClerk.Core/MicroPreviewProjection.cs. */
export interface MicroPreviewPost {
    index: number;
    text: string;
    /** In the network's own units — X's weighted characters, Bluesky's graphemes, Discord's chars. */
    length: number;
    /** Relative `/media/…` pictures the network attaches; empty where it takes none. */
    imageUrls: string[];
    /** The blog link inside `text`, when the post carries one. */
    linkUrl: string | null;
}

export interface MicroPreview {
    network: MicroNetwork;
    language: string;
    maxLength: number;
    hasAuthorText: boolean;
    supportsThreads: boolean;
    blogUrl: string | null;
    /** The announcement: the author's own text or the teaser, plus the blog link. */
    single: MicroPreviewPost;
    /** The whole document as parts; empty where the network never threads. */
    thread: MicroPreviewPost[];
}

export type PreviewTheme = 'light' | 'dark';

export const MEDIA_KINDS: ReadonlySet<TelegramPreviewKind> = new Set(['photo', 'video', 'audio', 'slideshow', 'collage']);

@Injectable({ providedIn: 'root' })
export class PreviewService {
    private http = inject(HttpClient);

    /** `thread` mirrors the Export modal's "Publish as a thread": off, the whole post is one message (ADR-313). */
    telegram(draftId: string, lang: string, thread = false) {
        const params = new HttpParams().set('lang', lang).set('thread', thread);
        return firstValueFrom(this.http.get<TelegramPreview>(`/api/drafts/${draftId}/preview/telegram`, { params }));
    }

    micro(draftId: string, network: MicroNetwork, lang: string) {
        const params = new HttpParams().set('network', network).set('lang', lang);
        return firstValueFrom(this.http.get<MicroPreview>(`/api/drafts/${draftId}/preview/micro`, { params }));
    }

    blogUrl(draftId: string, lang: string, theme: PreviewTheme | null): string {
        const params = new HttpParams().set('lang', lang);
        return `/api/drafts/${draftId}/preview/blog?${theme ? params.set('theme', theme) : params}`;
    }

    /**
     * The blog page as text, for an iframe `srcdoc`. Fetched rather than framed by URL: a fully
     * sandboxed frame has an opaque origin, and the session cookie does not travel with a
     * cross-site navigation — a same-origin request from here carries it.
     */
    blogHtml(draftId: string, lang: string, theme: PreviewTheme | null) {
        return firstValueFrom(this.http.get(this.blogUrl(draftId, lang, theme), { responseType: 'text' }));
    }

    /** The existing share link without rotating it; null when the draft has none. */
    async previewLink(draftId: string): Promise<{ url: string } | null> {
        try {
            return await firstValueFrom(this.http.get<{ url: string }>(`/api/drafts/${draftId}/preview-link`));
        } catch (e) {
            if (e instanceof HttpErrorResponse && e.status === 404) return null;
            throw e;
        }
    }
}
