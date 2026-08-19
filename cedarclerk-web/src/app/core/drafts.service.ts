import { HttpClient, HttpErrorResponse, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom, timeout } from 'rxjs';
import { DEFAULT_PRIMARY_LANGUAGE } from './languages';

// Phase 8 Step 8, docs/tasks/ROADMAP.md — neither AI provider streams, so there's no way to signal
// real progress; this is purely a "don't let it look stuck forever" ceiling — how long the client
// keeps polling GET /api/ai-jobs/{jobId} (ADR-058-follow-up) before giving up and reporting a
// timeout. Matches the server-side Consts.Anthropic.RequestTimeout (600s = 10 min, the cap on a
// single Anthropic attempt) so the client doesn't give up before the backend job even could.
// Marty, 29.07.2026 — bumped from 3 minutes for a large document's translation.
// AI-edit (fix-errors/schizo) only — auto-translate has its own longer AUTO_TRANSLATE_TIMEOUT_MS.
export const AI_OPERATION_TIMEOUT_MS = 600_000;

// Marty, 29.07.2026 — auto-translate specifically bumped to 20 min. Matches the server-side
// Consts.Anthropic.AutoTranslateTimeout — see its comment for why translation gets a longer
// leash than AI-edit.
export const AUTO_TRANSLATE_TIMEOUT_MS = 1_200_000;

// A large Notion markdown export can legitimately take minutes to upload over a slow link
// connection — a fixed overall timeout would cut off a real-but-slow upload. `{ each }` instead
// resets on every progress tick, so only genuine silence (a dropped connection, not a slow one)
// times out. Previously there was no timeout at all, which made a slow-but-live upload and a truly
// hung one look identical (Marty, 28.07.2026 — reported as "spins/loads forever").
export const UPLOAD_STALL_TIMEOUT_MS = 60_000;

// Cloudflare's own edge enforces this ceiling in front of the tunnel on the current Free/Pro plan
// — confirmed empirically 28.07.2026 (a real 150MB upload got a bare 413 from Cloudflare after
// ~1MB, well before reaching Kestrel, whose own cap is MarkdownZipMaxBytes server-side/200MB).
// Not something this app can raise or detect authoritatively — checked client-side only, to fail
// fast with an honest message instead of letting a doomed upload run for a minute first.
export const CLOUDFLARE_UPLOAD_LIMIT_BYTES = 100 * 1024 * 1024;

// DB2.6 — matches the maxlength on the title inputs.
export const DRAFT_TITLE_MAX = 64;

export const EMPTY_DOC = '{"type":"doc","content":[{"type":"paragraph"}]}';

// Shared between the New Draft dialog (now on /drafts, 28.07.2026 — creation used to navigate to
// /editor first and open the dialog there, which meant the editor page existed mid-creation with
// nothing in it yet) and editor.component.ts's own silent fallback creation (empty draft list on
// load, deleting the last remaining draft), which still runs on the editor page directly.
export type NewDraftTemplate = 'blank' | 'devlog' | 'photodump';
export const NEW_DRAFT_TEMPLATES: Record<NewDraftTemplate, string> = {
    blank: EMPTY_DOC,
    devlog: JSON.stringify({
        type: 'doc',
        content: [
            { type: 'paragraph', content: [{ type: 'text', text: 'What happened this week…' }] },
            {
                type: 'bulletList',
                content: [
                    { type: 'listItem', content: [{ type: 'paragraph' }] },
                    { type: 'listItem', content: [{ type: 'paragraph' }] },
                ],
            },
            { type: 'paragraph', content: [{ type: 'text', text: "What's next." }] },
        ],
    }),
    photodump: JSON.stringify({
        type: 'doc',
        content: [
            { type: 'paragraph', content: [{ type: 'text', text: 'A few photos from…' }] },
            { type: 'paragraph' },
        ],
    }),
};

export interface ScheduledInfo { scheduledAtUtc: string; chatId: string; status: string; error: string | null; }
// One stored version of one language of a draft. `kind` is how it came to be: an edit ("save"),
// or the content actually sent to a destination ("telegram"/"blog") — the latter are the
// baselines the publish guard diffs against, and are never pruned.
export interface DraftRevision {
    id: string;
    kind: RevisionKind;
    destination: string | null;
    createdAt: string;
    title: string;
    fingerprint: string;
    lines: number;
}

// "restore" marks the point an author rewound to — see T-016.
export type RevisionKind = 'save' | 'telegram' | 'blog' | 'restore';

// The same row with its stored document, plus what restoring it would change.
export interface DraftRevisionDetail {
    id: string;
    kind: RevisionKind;
    destination: string | null;
    createdAt: string;
    title: string;
    cedarJson: string;
    diffToCurrent: RevisionDiff | null;
    isCurrent: boolean;
}

export interface RevisionDiff {
    beforeLines: number;
    afterLines: number;
    addedLines: number[];
    removedLines: number[];
    changedLines: number;
    totalChanged: number;
}

export interface DraftMeta {
    id: string;
    title: string;
    createdAt: string;
    updatedAt: string;
    primaryLanguage: string;
    blogSlug: string | null;
    isBlogPublished: boolean;
    blogPublishedAt: string | null;
    languages: string[]; // translation languages that exist ("en"), primary (RU) is implicit
    tags: string; // comma-separated lowercase tags, shared across language versions
    isArchived: boolean;
    lastTelegramMessageId: number | null;
    lastTelegramUsername: string | null;
    staleLanguages: string[]; // subset of `languages` whose translation predates the last RU edit
    scheduled: ScheduledInfo | null; // most recent Pending/Failed ScheduledPost row, if any
    folderId: string | null; // at most one folder per draft — see the ADR following ADR-038
    seriesId: string | null; // at most one series per draft — ADR-125
    parentDraftId: string | null; // ADR-128 — the document tree; null = root
    siblingOrder: number;
    isPrivate: boolean; // blog page gated behind PostInvite tokens — see ADR-041
    isTemplate: boolean; // NF1 — a template, filtered into its own /drafts tab, never published
    disableCopy: boolean; // blocks selection/copy/context menu on the blog page; private posts only
    // T-039 — an informational post nobody is invited to react to. Two flags, because a post can
    // reasonably take likes but not a discussion, and the reverse is just as reasonable.
    disableReactions: boolean;
    disableComments: boolean;
    // Blog activity (B23). Totals are all-time; new* is what accumulated since the previous
    // session (server-side baseline, see DraftStatSeen) and is 0 the first time a draft is listed.
    viewCount: number;
    reactionCount: number; // likes + dislikes — the split stays on the blog post page
    newViewCount: number;
    newReactionCount: number;
}
export interface TranslationMeta { language: string; title: string; updatedAt: string; }
export interface TranslationFull extends TranslationMeta { cedarJson: string; sourceSnapshotJson: string | null; }
export interface DraftFull extends DraftMeta {
    cedarJson: string;
    translations: TranslationMeta[];
    registrationFormJson: string | null;
    registrationFormTranslationsJson: string | null;
    // Idea #4 - the reader-facing headline when it differs from the draft's name; null means
    // they are the same.
    articleTitle: string | null;
    // A private post that is still listed on the blog index, with a lock on its card.
    isListedWhilePrivate: boolean;
    // FI4.1 — language codes this post actually has a registration form for, primary first.
    formLanguages: string[];
    watermarkText: string | null;
}

// Mirrors Consts.Watermark.MaxLength (CedarClerk.Core) — the server rejects longer text, so the
// input caps at the same number rather than letting a save fail (I7).
export const WATERMARK_MAX_LENGTH = 60;
export type AiEditKind = 'fix-errors' | 'schizo';
export interface AiEditResult { title: string; cedarJson: string; updatedAt: string; }
export interface AiJobPoll<T> { status: 'pending' | 'running' | 'completed' | 'failed'; result: T | null; error: string | null; }
export interface FolderMeta { id: string; name: string; count: number; }
export interface SeriesMeta { id: string; name: string; slug: string; description: string | null; count: number; }
export interface PostInvite { id: string; email: string; createdAt: string; url: string; }

// Registration form shown to uninvited visitors of a private post (B3). The JSON shape is
// owned by the client — the server only length-checks the blob.
// 'multi' (N10) answers arrive as a JSON array inside the same string-valued answers map the
// other types use — see MultiAnswer in CedarClerk.Core for why it isn't a wider type.
// 'longtext' (T-032) answers the same way 'text' does, in a taller box. 'static' (T-031) is not
// a question at all: a block of text and/or an image the reader only reads, never required and
// never present in the answers map.
export type RegistrationQuestionType = 'text' | 'longtext' | 'choice' | 'multi' | 'consent' | 'static';

export const KNOWN_QUESTION_TYPES: RegistrationQuestionType[] =
    ['text', 'longtext', 'choice', 'multi', 'consent', 'static'];
// ADR-060 — an option's stored answer value is its stable id, not its label, so the same choice
// picked from different language versions of the form aggregates as one answer. v1 blobs parse
// with id === label, which is also exactly what their stored answers hold.
export interface RegistrationOptionView { id: string; label: string; }
export interface RegistrationQuestion { id: string; label: string; type: RegistrationQuestionType; options?: RegistrationOptionView[]; required?: boolean; imageUrl?: string | null; }
export interface RegistrationForm {
    intro?: string;
    requireName: boolean; requireNickname: boolean; requireEmail: boolean; requireSocial: boolean;
    questions: RegistrationQuestion[];
    // Languages the form carries text for (v2 blobs); a v1 blob reports none.
    languages: string[];
}
export interface PostRegistration {
    id: string; name: string | null; nickname: string | null; email: string | null;
    socialLink: string | null; answersJson: string | null; createdAt: string;
}

// One language's text out of a v1 plain string or a v2 per-language dictionary, with the same
// per-string fallback order the server resolves with (CedarClerk.Core/RegistrationFormSet.cs).
export function pickLangText(node: unknown, lang: string, languages: string[]): string {
    if (typeof node === 'string') return node;
    if (node && typeof node === 'object') {
        const map = node as Record<string, unknown>;
        const direct = map[lang];
        if (typeof direct === 'string' && direct.trim()) return direct;
        for (const l of languages) {
            const v = map[l];
            if (typeof v === 'string' && v.trim()) return v;
        }
    }
    return '';
}

// A corrupt/hand-edited blob must not break the editor — mirrors the server-side parser's
// "degrade, never throw" behaviour (CedarClerk.Core/RegistrationFormDefinition.cs). A v2
// multi-language blob (ADR-060) is projected to one language — the primary by default, since
// this view feeds the owner-facing charts and status strips.
export function parseRegistrationForm(json: string | null | undefined, lang = DEFAULT_PRIMARY_LANGUAGE): RegistrationForm | null {
    if (!json) return null;
    try {
        const raw = JSON.parse(json) as Record<string, unknown>;
        const isV2 = raw['v'] === 2;
        const languages = isV2 && Array.isArray(raw['languages'])
            ? (raw['languages'] as unknown[]).filter((l): l is string => typeof l === 'string')
            : [];

        const questions: RegistrationQuestion[] = [];
        for (const q of Array.isArray(raw['questions']) ? raw['questions'] as Record<string, unknown>[] : []) {
            if (!q || typeof q !== 'object') continue;
            const label = pickLangText(q['label'], lang, languages);
            if (!label.trim()) continue;
            const options: RegistrationOptionView[] = [];
            for (const o of Array.isArray(q['options']) ? q['options'] as unknown[] : []) {
                if (typeof o === 'string') {
                    if (o.trim()) options.push({ id: o, label: o });
                } else if (o && typeof o === 'object') {
                    const oo = o as Record<string, unknown>;
                    const optLabel = pickLangText(oo['label'], lang, languages);
                    if (optLabel.trim()) options.push({ id: typeof oo['id'] === 'string' && oo['id'] ? oo['id'] as string : optLabel, label: optLabel });
                }
            }
            questions.push({
                id: typeof q['id'] === 'string' && q['id'] ? q['id'] as string : `q${questions.length + 1}`,
                label,
                type: (KNOWN_QUESTION_TYPES.includes(q['type'] as RegistrationQuestionType) ? q['type'] : 'text') as RegistrationQuestionType,
                options,
                required: q['type'] === 'consent' || (q['type'] !== 'static' && !!q['required']),
                imageUrl: typeof q['imageUrl'] === 'string' ? q['imageUrl'] as string : null,
            });
        }

        return {
            intro: pickLangText(raw['intro'], lang, languages) || undefined,
            requireName: !!raw['requireName'],
            requireNickname: !!raw['requireNickname'],
            requireEmail: !!raw['requireEmail'],
            requireSocial: !!raw['requireSocial'],
            questions,
            languages,
        };
    } catch {
        return { requireName: true, requireNickname: false, requireEmail: true, requireSocial: false, questions: [], languages: [] };
    }
}

// T-018 — what a save may carry beyond the content itself. `expectedUpdatedAt` is the timestamp
// of the version being edited (the write 409s if the stored one moved since); `confirmShrink` is
// the author having agreed to a save that deletes most of the text.
export interface SaveGuards {
    expectedUpdatedAt?: string | null;
    confirmShrink?: boolean;
}

// The 409 bodies the two guards answer with.
export type SaveRefusal =
    | { code: 'shrink'; error: string; storedTextLength: number; incomingTextLength: number }
    | { code: 'stale'; error: string; currentUpdatedAt: string };

export function saveRefusalOf(e: unknown): SaveRefusal | null {
    const body = e instanceof HttpErrorResponse && e.status === 409 ? e.error : null;
    return body?.code === 'shrink' || body?.code === 'stale' ? body as SaveRefusal : null;
}

@Injectable({ providedIn: 'root' })
export class DraftsService {
    private http = inject(HttpClient);

    list() { 
        return firstValueFrom(this.http.get<DraftMeta[]>('/api/drafts')); 
    }

    get(id: string) { 
        return firstValueFrom(this.http.get<DraftFull>(`/api/drafts/${id}`)); 
    }

    create(title: string, cedarJson: string) {
        return firstValueFrom(this.http.post<{ id: string }>('/api/drafts', { title, cedarJson }));
    }

    // Returns the server's own updatedAt: the caller compares it against translation timestamps,
    // which are also server-issued, so a client clock must never get into that comparison (IB3).
    // `guards` carries the T-018 save guards — expectedUpdatedAt makes the write conditional,
    // confirmShrink is the answer to a 409 the author agreed to. Both optional server-side.
    update(id: string, title: string, cedarJson: string, guards: SaveGuards = {}) {
        return firstValueFrom(this.http.put<{ id: string; updatedAt: string }>(
            `/api/drafts/${id}`, { title, cedarJson, ...guards }));
    }

    remove(id: string) {
         return firstValueFrom(this.http.delete(`/api/drafts/${id}`));
    }

    archive(id: string) {
        return firstValueFrom(this.http.post<{ isArchived: boolean }>(`/api/drafts/${id}/archive`, {}));
    }

    unarchive(id: string) {
        return firstValueFrom(this.http.post<{ isArchived: boolean }>(`/api/drafts/${id}/unarchive`, {}));
    }

    updateTags(id: string, tags: string) {
        return firstValueFrom(this.http.put<{ tags: string }>(`/api/drafts/${id}/tags`, { tags }));
    }

    // Idea #3 - the tag *set*, not one draft's tags. Renaming rewrites every draft carrying the
    // old tag; the blog picks both up with no extra step, since it reads Draft.Tags directly.
    renameTag(from: string, to: string) {
        return firstValueFrom(this.http.put<{ renamed: number }>('/api/drafts/tags', { from, to }));
    }

    deleteTag(tag: string) {
        return firstValueFrom(this.http.delete<{ removed: number }>(`/api/drafts/tags/${encodeURIComponent(tag)}`));
    }

    listTagUsage() {
        return firstValueFrom(this.http.get<{ tag: string; count: number }[]>('/api/drafts/tags'));
    }

    setDraftFolder(id: string, folderId: string | null) {
        return firstValueFrom(this.http.put<{ folderId: string | null }>(`/api/drafts/${id}/folder`, { folderId }));
    }

    setDraftSeries(id: string, seriesId: string | null) {
        return firstValueFrom(this.http.put<{ seriesId: string | null }>(`/api/drafts/${id}/series`, { seriesId }));
    }

    // ADR-128 — who links to this document (derived from wikilink nodes on save).
    getBacklinks(id: string) {
        return firstValueFrom(this.http.get<{ id: string; title: string }[]>(`/api/drafts/${id}/backlinks`));
    }

    // ADR-128 — place a document in the tree; beforeId orders it among its new siblings.
    setDraftParent(id: string, parentId: string | null, beforeId?: string) {
        return firstValueFrom(this.http.put<{ parentDraftId: string | null; siblingOrder: number }>(
            `/api/drafts/${id}/parent`, { parentId, beforeId: beforeId ?? null }));
    }

    // Semi-public: listed and searchable on the blog, still gated behind the registration form.
    setDraftListed(id: string, isListedWhilePrivate: boolean) {
        return firstValueFrom(this.http.post<{ isListedWhilePrivate: boolean }>(
            `/api/drafts/${id}/listed`, { isListedWhilePrivate }));
    }

    setDraftPrivate(id: string, isPrivate: boolean) {
        return firstValueFrom(this.http.post<{ isPrivate: boolean }>(`/api/drafts/${id}/private`, { isPrivate }));
    }

    // Copy protection on the blog page of a private post — selection, copy/cut and the context
    // menu are blocked on the rendered post.
    setDraftDisableCopy(id: string, disableCopy: boolean) {
        return firstValueFrom(this.http.post<{ disableCopy: boolean }>(`/api/drafts/${id}/disable-copy`, { disableCopy }));
    }

    // NF1 — post templates.
    setDraftTemplate(id: string, isTemplate: boolean) {
        return firstValueFrom(this.http.post<{ isTemplate: boolean }>(`/api/drafts/${id}/template`, { isTemplate }));
    }

    // FI3.4 — the server slugifies and enforces global uniqueness, so this can send raw text.
    // Idea #4 - blank clears it, which restores "the draft's name is the title".
    setArticleTitle(id: string, articleTitle: string) {
        return firstValueFrom(this.http.post<{ articleTitle: string | null }>(
            `/api/drafts/${id}/article-title`, { articleTitle }));
    }

    setBlogSlug(id: string, slug: string) {
        return firstValueFrom(this.http.post<{ blogSlug: string }>(`/api/drafts/${id}/slug`, { slug }));
    }

    // Blank clears the watermark; the server trims and returns null for an empty value (I7).
    setDraftWatermark(id: string, watermarkText: string) {
        return firstValueFrom(this.http.post<{ watermarkText: string | null }>(`/api/drafts/${id}/watermark`, { watermarkText }));
    }

    // FI4.1 — `language` names the slot: the primary language writes the post's own form, any
    // other writes that language's entry beside it.
    setRegistrationForm(id: string, formJson: string | null, language = DEFAULT_PRIMARY_LANGUAGE) {
        return firstValueFrom(this.http.post<{ registrationFormJson: string | null; formLanguages: string[] }>(
            `/api/drafts/${id}/registration-form`, { formJson, language }));
    }

    listRegistrations(id: string) {
        return firstValueFrom(this.http.get<PostRegistration[]>(`/api/drafts/${id}/registrations`));
    }

    /** Deletes one submission — and with it that reader's access, since the row carries the grant. */
    deleteRegistration(id: string, registrationId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/drafts/${id}/registrations/${registrationId}`));
    }

    listInvites(id: string) {
        return firstValueFrom(this.http.get<PostInvite[]>(`/api/drafts/${id}/invites`));
    }

    addInvite(id: string, email: string) {
        return firstValueFrom(this.http.post<PostInvite & { emailSent: boolean }>(`/api/drafts/${id}/invites`, { email }));
    }

    revokeInvite(id: string, inviteId: string) {
        return firstValueFrom(this.http.delete(`/api/drafts/${id}/invites/${inviteId}`));
    }

    resendInvite(id: string, inviteId: string) {
        return firstValueFrom(this.http.post<{ emailSent: boolean }>(`/api/drafts/${id}/invites/${inviteId}/resend`, {}));
    }

    listFolders() {
        return firstValueFrom(this.http.get<FolderMeta[]>('/api/folders'));
    }

    createFolder(name: string) {
        return firstValueFrom(this.http.post<{ id: string; name: string }>('/api/folders', { name }));
    }

    renameFolder(id: string, name: string) {
        return firstValueFrom(this.http.put<{ id: string; name: string }>(`/api/folders/${id}`, { name }));
    }

    deleteFolder(id: string) {
        return firstValueFrom(this.http.delete(`/api/folders/${id}`));
    }

    listSeries() {
        return firstValueFrom(this.http.get<SeriesMeta[]>('/api/series'));
    }

    createSeries(name: string) {
        return firstValueFrom(this.http.post<{ id: string; name: string; slug: string; description: string | null }>('/api/series', { name }));
    }

    renameSeries(id: string, name: string) {
        return firstValueFrom(this.http.put<{ id: string; name: string; slug: string }>(`/api/series/${id}`, { name }));
    }

    deleteSeries(id: string) {
        return firstValueFrom(this.http.delete(`/api/series/${id}`));
    }

    getTranslation(id: string, lang: string) {
        return firstValueFrom(this.http.get<TranslationFull>(`/api/drafts/${id}/translations/${lang}`));
    }

    saveTranslation(id: string, lang: string, title: string, cedarJson: string, guards: SaveGuards = {}) {
        return firstValueFrom(this.http.put<{ language: string; updatedAt: string; sourceSnapshotJson: string | null }>(
            `/api/drafts/${id}/translations/${lang}`, { title, cedarJson, ...guards }));
    }

    setPrimaryLanguage(id: string, language: string) {
        return firstValueFrom(this.http.post<{ primaryLanguage: string; updatedAt: string }>(
            `/api/drafts/${id}/primary-language`, { language }));
    }

    revisions(id: string, language: string) {
        return firstValueFrom(this.http.get<DraftRevision[]>(`/api/drafts/${id}/revisions/${language}`));
    }

    revision(id: string, language: string, revisionId: string) {
        return firstValueFrom(this.http.get<DraftRevisionDetail>(`/api/drafts/${id}/revisions/${language}/${revisionId}`));
    }

    // T-017 — either end may be the literal 'current', which is whatever is stored for that
    // language right now.
    revisionDiff(id: string, language: string, from: string, to: string) {
        return firstValueFrom(this.http.get<{ diff: RevisionDiff }>(
            `/api/drafts/${id}/revisions/${language}/diff`, { params: { from, to } }));
    }

    restoreRevision(id: string, revisionId: string) {
        return firstValueFrom(this.http.post<{ language: string; title: string; cedarJson: string; updatedAt: string }>(
            `/api/drafts/${id}/revisions/${revisionId}/restore`, {}));
    }

    removeTranslation(id: string, lang: string) {
        return firstValueFrom(this.http.delete(`/api/drafts/${id}/translations/${lang}`));
    }

    // ADR-058-follow-up (29.07.2026) — auto-translate/ai-edit no longer hold one HTTP request open
    // for the whole Anthropic call (a large document's translation can legitimately outrun
    // Cloudflare Tunnel's own edge timeout, which then 502s the browser even though this server
    // finishes and saves the result seconds later — confirmed directly, not theorized). The POST
    // now starts a background job and returns immediately; editor.component.ts polls
    // getAiJob() with short requests instead.
    startAutoTranslate(id: string, lang: string) {
        return firstValueFrom(this.http.post<{ jobId: string }>(`/api/drafts/${id}/translations/${lang}/auto`, {}));
    }

    startAiEdit(id: string, lang: string, kind: AiEditKind) {
        return firstValueFrom(this.http.post<{ jobId: string }>(`/api/drafts/${id}/ai-edit/${lang}/${kind}`, {}));
    }

    getAiJob<T>(jobId: string) {
        return firstValueFrom(this.http.get<AiJobPoll<T>>(`/api/ai-jobs/${jobId}`));
    }

    cancelAiJob(jobId: string) {
        return firstValueFrom(this.http.delete(`/api/ai-jobs/${jobId}`));
    }

    importCedar(file: File) {
        const formData = new FormData();
        formData.append('file', file);
        return firstValueFrom(this.http.post<{ id: string }>('/api/drafts/import', formData));
    }

    // Raw event stream (reportProgress), not a Promise — real upload-percentage feedback needs the
    // HttpEvent stream, and returning an Observable lets the caller unsubscribe to cancel the
    // in-flight upload.
    importMarkdown$(file: File): Observable<HttpEvent<{ id: string; unmatchedImages: string[] }>> {
        const formData = new FormData();
        formData.append('file', file);
        return this.http.post<{ id: string; unmatchedImages: string[] }>('/api/drafts/import-markdown', formData, {
            reportProgress: true, observe: 'events',
        }).pipe(timeout({ each: UPLOAD_STALL_TIMEOUT_MS }));
    }

    // ADR-065 — one click republishes every language, so the confirmation names a version per
    // language; the server rejects the publish outright if any of them is no longer current.
    publishToBlog(id: string, confirmedFingerprints?: Record<string, string>) {
        return firstValueFrom(this.http.post<{ slug: string; url: string }>(
            `/api/drafts/${id}/publish-blog`, { confirmedFingerprints }));
    }

    unpublishFromBlog(id: string) {
        return firstValueFrom(this.http.post(`/api/drafts/${id}/unpublish-blog`, {}));
    }

    /** T-039 — both flags travel together: they are set from one row of checkboxes. */
    setEngagement(id: string, disableReactions: boolean, disableComments: boolean) {
        return firstValueFrom(this.http.post<{ disableReactions: boolean; disableComments: boolean }>(
            `/api/drafts/${id}/engagement`, { disableReactions, disableComments }));
    }
}
