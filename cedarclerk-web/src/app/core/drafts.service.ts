import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom, timeout } from 'rxjs';
import { PRIMARY_LANGUAGE } from './languages';

// Phase 8 Step 8, docs/ROADMAP.md — neither AI provider streams, so there's no way to signal
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

// A large Notion markdown export can legitimately take minutes to upload over the Pi's residential
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
export interface DraftMeta {
    id: string;
    title: string;
    createdAt: string;
    updatedAt: string;
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
    isPrivate: boolean; // blog page gated behind PostInvite tokens — see ADR-041
    isTemplate: boolean; // NF1 — a template, filtered into its own /drafts tab, never published
    disableCopy: boolean; // blocks selection/copy/context menu on the blog page; private posts only
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
export interface PostInvite { id: string; email: string; createdAt: string; url: string; }

// Registration form shown to uninvited visitors of a private post (B3). The JSON shape is
// owned by the client — the server only length-checks the blob.
// 'multi' (N10) answers arrive as a JSON array inside the same string-valued answers map the
// other types use — see MultiAnswer in CedarClerk.Core for why it isn't a wider type.
export type RegistrationQuestionType = 'text' | 'choice' | 'multi' | 'consent';
// ADR-060 — an option's stored answer value is its stable id, not its label, so the same choice
// picked from different language versions of the form aggregates as one answer. v1 blobs parse
// with id === label, which is also exactly what their stored answers hold.
export interface RegistrationOptionView { id: string; label: string; }
export interface RegistrationQuestion { id: string; label: string; type: RegistrationQuestionType; options?: RegistrationOptionView[]; required?: boolean; }
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
export function parseRegistrationForm(json: string | null | undefined, lang = PRIMARY_LANGUAGE): RegistrationForm | null {
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
                type: (q['type'] === 'choice' || q['type'] === 'multi' || q['type'] === 'consent' ? q['type'] : 'text') as RegistrationQuestionType,
                options,
                required: q['type'] === 'consent' || !!q['required'],
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
    update(id: string, title: string, cedarJson: string) {
        return firstValueFrom(this.http.put<{ id: string; updatedAt: string }>(`/api/drafts/${id}`, { title, cedarJson }));
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
    setRegistrationForm(id: string, formJson: string | null, language = PRIMARY_LANGUAGE) {
        return firstValueFrom(this.http.post<{ registrationFormJson: string | null; formLanguages: string[] }>(
            `/api/drafts/${id}/registration-form`, { formJson, language }));
    }

    listRegistrations(id: string) {
        return firstValueFrom(this.http.get<PostRegistration[]>(`/api/drafts/${id}/registrations`));
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

    getTranslation(id: string, lang: string) {
        return firstValueFrom(this.http.get<TranslationFull>(`/api/drafts/${id}/translations/${lang}`));
    }

    saveTranslation(id: string, lang: string, title: string, cedarJson: string) {
        return firstValueFrom(this.http.put<{ language: string; updatedAt: string; sourceSnapshotJson: string | null }>(
            `/api/drafts/${id}/translations/${lang}`, { title, cedarJson }));
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

    publishToBlog(id: string) {
        return firstValueFrom(this.http.post<{ slug: string; url: string }>(`/api/drafts/${id}/publish-blog`, {}));
    }

    unpublishFromBlog(id: string) {
        return firstValueFrom(this.http.post(`/api/drafts/${id}/unpublish-blog`, {}));
    }
}