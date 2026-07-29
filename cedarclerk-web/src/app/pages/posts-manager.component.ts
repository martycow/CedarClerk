import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import {
    DraftsService, DraftMeta, PostRegistration,
    RegistrationForm, RegistrationQuestion, RegistrationQuestionType, parseRegistrationForm,
} from '../core/drafts.service';
import {
    FormPresetsService, FormPreset, RegistrationFormEdit, FormQuestionEdit,
    normalizeFormForEdit, blankFormEdit, newQuestionId, newOptionId,
} from '../core/form-presets.service';
import { PostsService, ScheduledPost } from '../core/posts.service';
import { PRIMARY_LANGUAGE, CONTENT_LANGUAGES } from '../core/languages';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CountBadgeComponent } from '../shared/count-badge.component';
import { httpErrorMessage } from '../core/http-error.util';
import { PageHeaderComponent } from '../shared/page-header.component';
import { ModalComponent } from '../shared/modal.component';
import { CommentsComponent } from './comments.component';
import { TagPickerComponent } from '../shared/tag-picker.component';
import { FolderPickerComponent } from '../shared/folder-picker.component';
import { FormRefComponent } from '../shared/form-ref.component';
import { TagUsageService } from '../core/tag-usage.service';
import { FoldersService } from '../core/folders.service';
import { StatsComponent } from './stats.component';
import {
    LucideTrash2 as Trash2, LucideArchive as Archive, LucideArchiveRestore as ArchiveRestore,
    LucidePenLine as PenLine, LucideLock as Lock, LucideExternalLink as ExternalLink,
    LucideRefreshCw as RefreshCw, LucideX as X, LucideInfo as Info,
} from '@lucide/angular';

// FI3.5 removed the 'feedback' tab; ?tab=feedback still resolves (to posts, where feedback now
// lives) because links to it exist in the wild — the account menu, and Marty's own bookmarks.
export type ManagerTab = 'posts' | 'stats' | 'forms';
const MANAGER_TABS: ManagerTab[] = ['posts', 'stats', 'forms'];
const RETIRED_TABS: Record<string, ManagerTab> = { feedback: 'posts' };

// N7 — the Posts Manager. Comments/reactions and stats used to be two separate top-level pages
// with their own headers; they are now tab bodies here (their routes redirect), so there is one
// place that answers "what happened to my posts". The forms tab is intentionally read-only for
// now — editing, per-question breakdowns and the pie chart are N10, presets are N12.
@Component({
    selector: 'app-posts-manager',
    imports: [
        DatePipe, FormsModule, PageHeaderComponent, ModalComponent, CommentsComponent,
        StatsComponent, CountBadgeComponent, TagPickerComponent, FolderPickerComponent, FormRefComponent,
        Trash2, Archive, ArchiveRestore, PenLine, Lock, ExternalLink, RefreshCw, X, Info,
    ],
    templateUrl: 'posts-manager.component.html',
    styleUrls: ['posts-manager.component.css'],
})
export class PostsManagerComponent implements OnInit {
    auth = inject(AuthService);
    private draftsApi = inject(DraftsService);
    private presetsApi = inject(FormPresetsService);
    private postsApi = inject(PostsService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    feedback = inject(CommentsService);
    private tagUsageApi = inject(TagUsageService);
    private foldersApi = inject(FoldersService);
    t = inject(LocaleService).t;

    tab = signal<ManagerTab>('posts');
    loading = signal(true);
    error = signal('');
    busy = signal(false);

    drafts = signal<DraftMeta[]>([]);
    selectedId = signal<string | null>(null);

    // Minimal edits (Marty's wording) — everything here changes metadata only. Body text stays
    // the editor's job; the rename below still has to round-trip cedarJson because the save
    // endpoint takes title and body together.
    editTitle = '';
    // Idea #4 - the headline readers see, kept apart from the draft's own name above. Empty
    // means "same as the name", which is what it was before this field existed.
    editArticleTitle = '';
    editTags = signal<string[]>([]);
    // FI3.4 — the published post's own URL.
    editSlug = '';
    // FI3.10 — the list is publish-date ordered; search is how you reach one post directly.
    search = '';
    // Per-draft count of comments arrived since the last look, for the list's "+N" chip.
    newByDraft = signal<Record<string, number>>({});
    renaming = signal(false);
    deleteConfirmId = signal<string | null>(null);

    // FI2.1/FI2.8 — the two things the Export window stopped doing, because it exports and does
    // not manage what is already out there.
    unpublishing = signal(false);
    scheduled = signal<ScheduledPost[]>([]);

    registrations = signal<PostRegistration[]>([]);
    registrationsLoading = signal(false);

    // The form attached to the currently selected post. Shown on the POSTS tab (a post is where a
    // form is used), never edited there directly — you pick a preset, and the preset is copied.
    regForm = signal<RegistrationForm | null>(null);
    // FI4.1 — languages the selected post has a registration form for, primary first.
    postFormLanguages = signal<string[]>([]);
    regBusy = signal(false);

    // Forms tab — presets only. It used to require picking a post first and edited that post's
    // form, which made presets look like a property of a post; they aren't. Forms are authored
    // here as presets and chosen per post elsewhere.
    presets = signal<FormPreset[]>([]);
    selectedPresetId = signal<string | null>(null);
    presetName = '';
    readonly primaryLanguage = PRIMARY_LANGUAGE;
    readonly contentLanguages = CONTENT_LANGUAGES;
    // ADR-060 — the editor works on the v2 multi-language blob natively: one skeleton of stable
    // question/option ids, per-language texts on top, so "Да" and "Yes" stay one answer.
    presetForm = signal<RegistrationFormEdit | null>(null);
    presetState = signal<'saved' | 'dirty' | 'saving' | 'error'>('saved');
    deletePresetId = signal<string | null>(null);
    addLangOpen = signal(false);
    // The language a machine translation is currently running for, or null.
    presetTranslating = signal<string | null>(null);
    presetTranslateError = signal('');

    async ngOnInit() {
        this.feedback.refreshNewCount();
        // The default tab is 'posts' and setTab() only runs for a ?tab= deep link, so without
        // this eager load a plain landing here never fetched the presets at all — the form
        // dropdown then claimed "no saved presets yet" over a non-empty library.
        this.loadPresets();
        try {
            this.drafts.set(await this.draftsApi.list());
            this.loadScheduled();
            this.loadNewFeedback();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.load));
        } finally {
            this.loading.set(false);
        }
        // The export modal links straight here when no preset exists yet (I9), so land on the
        // tab that was asked for rather than on the default one.
        const requested = this.route.snapshot.queryParamMap.get('tab');
        if (requested) {
            const resolved = RETIRED_TABS[requested] ?? (MANAGER_TABS.includes(requested as ManagerTab) ? requested as ManagerTab : null);
            if (resolved) this.setTab(resolved);
        }
    }

    // Fire-and-forget: a missing "+N" chip is cosmetic and must not hold up the page.
    private async loadNewFeedback() {
        try {
            const feedback = await this.feedback.listAll();
            const counts: Record<string, number> = {};
            for (const c of feedback.comments) {
                if (c.isNew) counts[c.draftId] = (counts[c.draftId] ?? 0) + 1;
            }
            this.newByDraft.set(counts);
        } catch { /* ignore */ }
    }

    setTab(tab: ManagerTab) {
        // Leaving the posts tab is when the badge is worth re-checking: hovering feedback rows
        // there is exactly what clears the count.
        if (this.tab() === 'posts' && tab !== 'posts') this.feedback.refreshNewCount();
        // Leaving the forms tab with unsaved preset edits commits them rather than dropping them.
        if (this.tab() === 'forms' && tab !== 'forms') this.flushPreset();
        this.tab.set(tab);
        // Presets are needed by both tabs now: authored on forms, applied to a post on posts.
        if ((tab === 'forms' || tab === 'posts') && !this.presets().length) this.loadPresets();
    }

    selected(): DraftMeta | null {
        const id = this.selectedId();
        return id ? this.drafts().find(d => d.id === id) ?? null : null;
    }

    // FI3.10 — newest published first, then everything unpublished. Search covers title and tags,
    // which is what a post is actually remembered by.
    visiblePosts(): DraftMeta[] {
        const q = this.search.trim().toLowerCase();
        const matched = q
            ? this.drafts().filter(d => d.title.toLowerCase().includes(q) || d.tags.toLowerCase().includes(q))
            : this.drafts();
        return [...matched].sort((a, b) =>
            (b.blogPublishedAt ?? '').localeCompare(a.blogPublishedAt ?? '')
            || b.updatedAt.localeCompare(a.updatedAt));
    }

    // FI3.6 — how much arrived on this post since the last look. Comments only: reactions have no
    // per-draft "new" count in the feedback payload.
    private async loadScheduled() {
        try {
            this.scheduled.set(await this.postsApi.listScheduled());
        } catch { /* the list is context, not the point of the page */ }
    }

    scheduledFor(draftId: string): ScheduledPost[] {
        return this.scheduled().filter(p => p.draftId === draftId);
    }

    hasPendingSchedule(draftId: string): boolean {
        return this.scheduled().some(p => p.draftId === draftId && p.status === 'Pending');
    }

    // SQLite stores no DateTimeKind and the server sends UTC without a 'Z', which the browser
    // would otherwise read as local time.
    utcDate(iso: string): Date {
        return new Date(/Z|[+-]\d{2}:\d{2}$/.test(iso) ? iso : iso + 'Z');
    }

    async cancelScheduled(id: string) {
        try {
            await this.postsApi.cancelScheduled(id);
            this.scheduled.update(list => list.filter(p => p.id !== id));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.save));
        }
    }

    async unpublish(d: DraftMeta) {
        if (this.busy()) return;
        this.busy.set(true);
        this.unpublishing.set(true);
        this.error.set('');
        try {
            await this.draftsApi.unpublishFromBlog(d.id);
            this.patch(d.id, { isBlogPublished: false });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().editor.errors.unpublish));
        } finally {
            this.busy.set(false);
            this.unpublishing.set(false);
        }
    }

    // Six language chips in a list column is a wall of two-letter boxes that says little more
    // than three of them plus a count. The full list stays available as the chip's tooltip.
    private static readonly VisibleLanguageChips = 3;

    visibleLanguages(d: DraftMeta): string[] {
        return d.languages.slice(0, PostsManagerComponent.VisibleLanguageChips);
    }

    extraLanguageCount(d: DraftMeta): number {
        return Math.max(0, d.languages.length - PostsManagerComponent.VisibleLanguageChips);
    }

    extraLanguageTitle(d: DraftMeta): string {
        return d.languages.slice(PostsManagerComponent.VisibleLanguageChips).map(l => l.toUpperCase()).join(', ');
    }

    newFeedbackFor(draftId: string): number {
        return this.newByDraft()[draftId] ?? 0;
    }

    async select(d: DraftMeta) {
        this.selectedId.set(d.id);
        this.editTitle = d.title;
        this.editTags.set(d.tags.split(',').map(x => x.trim()).filter(x => x.length > 0));
        this.editSlug = d.blogSlug ?? '';
        this.editArticleTitle = '';
        this.loadArticleTitle(d.id);
        this.registrations.set([]);
        this.regForm.set(null);
        if (d.isPrivate) this.loadForm();
    }

    blogUrl(d: DraftMeta): string | null {
        return d.blogSlug ? `https://blog.mooexe.dev/${d.blogSlug}` : null;
    }

    telegramUrl(d: DraftMeta): string | null {
        return d.lastTelegramUsername && d.lastTelegramMessageId
            ? `https://t.me/${d.lastTelegramUsername}/${d.lastTelegramMessageId}`
            : null;
    }

    openInEditor(d: DraftMeta) {
        this.router.navigate(['/editor'], { queryParams: { draft: d.id } });
    }

    private patch(id: string, patch: Partial<DraftMeta>) {
        this.drafts.update(list => list.map(d => d.id === id ? { ...d, ...patch } : d));
    }

    async saveMeta() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        this.renaming.set(true);
        this.error.set('');
        try {
            const title = this.editTitle.trim() || 'Untitled';
            if (title !== d.title) {
                // The save endpoint replaces title and body together, so the body has to be
                // fetched and handed back unchanged — a rename must not touch content.
                const full = await this.draftsApi.get(d.id);
                await this.draftsApi.update(d.id, title, full.cedarJson);
            }
            const articleTitle = this.editArticleTitle.trim();
            await this.draftsApi.setArticleTitle(d.id, articleTitle);
            const tags = this.editTags().join(',');
            if (tags !== d.tags) {
                await this.draftsApi.updateTags(d.id, tags);
                this.tagUsageApi.refresh();
            }
            this.patch(d.id, { title, tags });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.save));
        } finally {
            this.busy.set(false);
            this.renaming.set(false);
        }
    }

    async assignFolder(folderId: string | null) {
        const d = this.selected();
        if (!d || d.folderId === folderId) return;
        try {
            await this.draftsApi.setDraftFolder(d.id, folderId);
            this.patch(d.id, { folderId });
            this.foldersApi.reload();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.move));
        }
    }

    async togglePrivate() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            await this.draftsApi.setDraftPrivate(d.id, !d.isPrivate);
            this.patch(d.id, { isPrivate: !d.isPrivate });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.privacy));
        } finally {
            this.busy.set(false);
        }
    }

    async toggleArchive() {
        const d = this.selected();
        if (!d || this.busy()) return;
        this.busy.set(true);
        try {
            const res = d.isArchived ? await this.draftsApi.unarchive(d.id) : await this.draftsApi.archive(d.id);
            this.patch(d.id, { isArchived: res.isArchived });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.update));
        } finally {
            this.busy.set(false);
        }
    }

    async confirmDelete() {
        const id = this.deleteConfirmId();
        if (!id) return;
        this.deleteConfirmId.set(null);
        this.busy.set(true);
        try {
            await this.draftsApi.remove(id);
            this.drafts.update(list => list.filter(d => d.id !== id));
            if (this.selectedId() === id) this.selectedId.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.delete));
        } finally {
            this.busy.set(false);
        }
    }

    // Answers are keyed by question id (ADR-042); the labels live in the form definition, which
    // this tab has in hand — so unlike the first cut, they resolve to the real question text. A
    // question deleted after someone answered it falls back to its raw key rather than vanishing.
    registrationAnswers(r: PostRegistration): { label: string; value: string }[] {
        if (!r.answersJson) return [];
        let parsed: Record<string, string>;
        try {
            parsed = JSON.parse(r.answersJson) as Record<string, string>;
        } catch {
            return [];
        }
        const questions = this.regForm()?.questions ?? [];
        return Object.entries(parsed)
            .filter(([, value]) => `${value}`.trim().length > 0)
            .map(([key, value]) => {
                const q = questions.find(x => x.id === key);
                // Stored answers are option ids (ADR-060) — shown as the current form's labels.
                // A raw value with no matching option (free text, or a pre-v2 row) shows as-is.
                const labelById = new Map((q?.options ?? []).map(o => [o.id, o.label]));
                const display = (v: string) => labelById.get(v) ?? v;
                return {
                    label: q?.label || key,
                    value: q?.type === 'multi'
                        ? splitMultiAnswer(value).map(display).join(', ')
                        : display(String(value)),
                };
            });
    }

    // FI3.4 — slugified server-side, so a hand-typed URL can't end up unroutable, and rejected
    // if another post already holds it (blog lookup is by slug across all owners).
    async saveSlug(d: DraftMeta) {
        const slug = this.editSlug.trim();
        if (!slug || this.busy()) return;
        this.busy.set(true);
        this.error.set('');
        try {
            const res = await this.draftsApi.setBlogSlug(d.id, slug);
            this.editSlug = res.blogSlug;
            this.drafts.update(list => list.map(x => x.id === d.id ? { ...x, blogSlug: res.blogSlug } : x));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.slug));
        } finally {
            this.busy.set(false);
        }
    }

    // FI3.7 — one dropdown handles both "use this preset" and "remove the form".
    pickPreset(value: string) {
        if (!value) return;
        if (value === '__none') {
            this.clearPostForm();
            return;
        }
        const preset = this.presets().find(p => p.id === value);
        if (preset) this.applyPresetToPost(preset);
    }

    // Lives on the full draft rather than the list projection - the list is already wide and
    // the article title is only ever needed for the one post being looked at.
    private async loadArticleTitle(id: string) {
        try {
            const full = await this.draftsApi.get(id);
            if (this.selectedId() === id) this.editArticleTitle = full.articleTitle ?? '';
        } catch { /* the field simply stays empty; saving it still works */ }
    }

    // ---------- The post's own form (Posts tab) ----------

    async loadForm() {
        const d = this.selected();
        if (!d || !d.isPrivate) return;
        this.registrationsLoading.set(true);
        try {
            const [full, regs] = await Promise.all([
                this.draftsApi.get(d.id),
                this.draftsApi.listRegistrations(d.id),
            ]);
            this.regForm.set(parseRegistrationForm(full.registrationFormJson));
            this.postFormLanguages.set(full.formLanguages ?? []);
            this.registrations.set(regs);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadForm));
        } finally {
            this.registrationsLoading.set(false);
        }
    }

    // The raw blob goes over the wire untouched — re-serializing the single-language projection
    // here would silently strip a v2 blob's other languages. The displayed form and language
    // list always come back from the server's response, whatever shape was written.
    private async persistFormJson(formJson: string | null, language = PRIMARY_LANGUAGE) {
        const d = this.selected();
        if (!d) return;
        this.regBusy.set(true);
        try {
            const res = await this.draftsApi.setRegistrationForm(d.id, formJson, language);
            this.postFormLanguages.set(res.formLanguages ?? []);
            this.regForm.set(parseRegistrationForm(res.registrationFormJson));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.saveForm));
        } finally {
            this.regBusy.set(false);
        }
    }

    // A post gets a form by choosing a preset, never by editing one in place — the definition is
    // authored once on the Forms tab. The preset is COPIED here (N12), so editing it afterwards
    // can't rewrite a post that already used it. A v2 preset carries every language in one blob,
    // so one click attaches them all (ADR-060); a legacy v1 preset still fills only its slot.
    async applyPresetToPost(p: FormPreset) {
        await this.persistFormJson(p.formJson, p.language || PRIMARY_LANGUAGE);
    }

    async clearPostForm() {
        await this.persistFormJson(null);
    }

    // ---------- Preset authoring (Forms tab) ----------

    async loadPresets() {
        try {
            this.presets.set(await this.presetsApi.list());
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadPresets));
        }
    }

    selectedPreset(): FormPreset | null {
        const id = this.selectedPresetId();
        return id ? this.presets().find(p => p.id === id) ?? null : null;
    }

    // Keyed on the blob itself so the list rows don't re-parse on every change-detection pass;
    // an edit produces a new formJson string and naturally misses the cache.
    private presetLangsCache = new Map<string, string[]>();
    presetLanguagesOf(p: FormPreset): string[] {
        let cached = this.presetLangsCache.get(p.formJson);
        if (!cached) {
            cached = normalizeFormForEdit(p.formJson, p.language || PRIMARY_LANGUAGE).languages;
            this.presetLangsCache.set(p.formJson, cached);
        }
        return cached;
    }

    async selectPreset(p: FormPreset) {
        await this.flushPreset();
        this.selectedPresetId.set(p.id);
        this.presetName = p.name;
        this.presetForm.set(normalizeFormForEdit(p.formJson, p.language || PRIMARY_LANGUAGE));
        this.presetState.set('saved');
        this.presetTranslateError.set('');
        this.addLangOpen.set(false);
    }

    // Created immediately rather than held as a local draft: a preset with no id has nowhere to
    // be saved to, and the list is the only place it would show up.
    async newPreset() {
        await this.flushPreset();
        const blank = blankFormEdit(PRIMARY_LANGUAGE);
        try {
            const created = await this.presetsApi.create(
                this.t().manager.forms.untitledPreset, JSON.stringify(blank), PRIMARY_LANGUAGE);
            this.presets.update(list => [...list, created]);
            this.selectedPresetId.set(created.id);
            this.presetName = created.name;
            this.presetForm.set(blank);
            this.presetState.set('saved');
            this.presetTranslateError.set('');
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.savePreset));
        }
    }

    private editPreset(next: RegistrationFormEdit) {
        this.presetForm.set(next);
        this.presetState.set('dirty');
    }

    markPresetNameDirty() {
        this.presetState.set('dirty');
    }

    async savePreset() {
        const p = this.selectedPreset();
        const form = this.presetForm();
        const name = this.presetName.trim();
        if (!p || !form || !name) return;
        this.presetState.set('saving');
        try {
            const saved = await this.presetsApi.update(p.id, name, JSON.stringify(form), form.languages[0]);
            this.presets.update(list => list.map(x => x.id === saved.id ? saved : x));
            this.presetState.set('saved');
        } catch (e) {
            this.presetState.set('error');
            this.error.set(httpErrorMessage(e, this.t().manager.errors.savePreset));
        }
    }

    // ---------- Preset languages (ADR-060) ----------

    presetLangs(): string[] {
        return this.presetForm()?.languages ?? [];
    }

    addableLanguages(): string[] {
        const used = this.presetLangs();
        return CONTENT_LANGUAGES.filter(l => !used.includes(l));
    }

    addPresetLanguage(lang: string) {
        const form = this.presetForm();
        if (!form || form.languages.includes(lang)) return;
        this.addLangOpen.set(false);
        this.editPreset({ ...form, languages: [...form.languages, lang] });
    }

    // The first language is the skeleton's fallback — everything else may go. Removing one also
    // strips its texts so a re-added language starts clean instead of resurrecting stale copy.
    removePresetLanguage(lang: string) {
        const form = this.presetForm();
        if (!form || form.languages[0] === lang) return;
        const strip = (map: Record<string, string>) => {
            const { [lang]: _, ...rest } = map;
            return rest;
        };
        this.editPreset({
            ...form,
            languages: form.languages.filter(l => l !== lang),
            intro: strip(form.intro),
            questions: form.questions.map(q => ({
                ...q,
                label: strip(q.label),
                options: q.options.map(o => ({ ...o, label: strip(o.label) })),
            })),
        });
    }

    // Fills one language by machine-translating the preset's first language server-side (same
    // Pro Plus + daily-quota gates as post auto-translate). Unsaved edits are flushed first so
    // the server translates what's on screen, and the saved result replaces the local state.
    async translatePresetLanguage(lang: string) {
        const p = this.selectedPreset();
        if (!p || this.presetTranslating()) return;
        this.presetTranslateError.set('');
        this.presetTranslating.set(lang);
        try {
            await this.flushPreset();
            const saved = await this.presetsApi.translate(p.id, lang);
            this.presets.update(list => list.map(x => x.id === saved.id ? saved : x));
            this.presetForm.set(normalizeFormForEdit(saved.formJson, saved.language || PRIMARY_LANGUAGE));
            this.presetState.set('saved');
        } catch (e) {
            this.presetTranslateError.set(httpErrorMessage(e, this.t().manager.errors.translatePreset));
        } finally {
            this.presetTranslating.set(null);
        }
    }

    private async flushPreset() {
        if (this.presetState() === 'dirty') await this.savePreset();
    }

    async confirmDeletePreset() {
        const id = this.deletePresetId();
        if (!id) return;
        this.deletePresetId.set(null);
        try {
            await this.presetsApi.remove(id);
            this.presets.update(list => list.filter(x => x.id !== id));
            if (this.selectedPresetId() === id) {
                this.selectedPresetId.set(null);
                this.presetForm.set(null);
                this.presetState.set('saved');
            }
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.deletePreset));
        }
    }

    // ---------- Preset form-definition editing ----------

    togglePresetField(field: 'requireName' | 'requireNickname' | 'requireEmail' | 'requireSocial') {
        const form = this.presetForm();
        if (!form) return;
        this.editPreset({ ...form, [field]: !form[field] });
    }

    setIntro(lang: string, intro: string) {
        const form = this.presetForm();
        if (!form) return;
        const next = { ...form.intro };
        if (intro.trim()) next[lang] = intro;
        else delete next[lang];
        this.editPreset({ ...form, intro: next });
    }

    addQuestion() {
        const form = this.presetForm();
        if (!form) return;
        const q: FormQuestionEdit = { id: newQuestionId(), label: {}, type: 'text', required: false, options: [] };
        this.editPreset({ ...form, questions: [...form.questions, q] });
    }

    updateQuestion(id: string, patch: Partial<FormQuestionEdit>) {
        const form = this.presetForm();
        if (!form) return;
        this.editPreset({ ...form, questions: form.questions.map(q => q.id === id ? { ...q, ...patch } : q) });
    }

    removeQuestion(id: string) {
        const form = this.presetForm();
        if (!form) return;
        this.editPreset({ ...form, questions: form.questions.filter(q => q.id !== id) });
    }

    setQuestionLabel(id: string, lang: string, value: string) {
        const q = this.presetForm()?.questions.find(x => x.id === id);
        if (!q) return;
        const label = { ...q.label };
        if (value.trim()) label[lang] = value;
        else delete label[lang];
        this.updateQuestion(id, { label });
    }

    setQuestionType(id: string, type: RegistrationQuestionType) {
        const q = this.presetForm()?.questions.find(x => x.id === id);
        if (!q) return;
        // An optional consent checkbox isn't a meaningful concept — forced on here (Core's Parse
        // forces it again server-side, so a hand-edited/older blob can't bypass it either).
        // Switching to a choice type seeds two empty option rows so there's something to type into.
        const options = (type === 'choice' || type === 'multi') && q.options.length === 0
            ? [{ id: newOptionId(), label: {} }, { id: newOptionId(), label: {} }]
            : q.options;
        this.updateQuestion(id, type === 'consent' ? { type, required: true, options } : { type, options });
    }

    addOption(qId: string) {
        const q = this.presetForm()?.questions.find(x => x.id === qId);
        if (!q) return;
        this.updateQuestion(qId, { options: [...q.options, { id: newOptionId(), label: {} }] });
    }

    removeOption(qId: string, optId: string) {
        const q = this.presetForm()?.questions.find(x => x.id === qId);
        if (!q) return;
        this.updateQuestion(qId, { options: q.options.filter(o => o.id !== optId) });
    }

    setOptionLabel(qId: string, optId: string, lang: string, value: string) {
        const q = this.presetForm()?.questions.find(x => x.id === qId);
        if (!q) return;
        this.updateQuestion(qId, {
            options: q.options.map(o => {
                if (o.id !== optId) return o;
                const label = { ...o.label };
                if (value.trim()) label[lang] = value;
                else delete label[lang];
                return { ...o, label };
            }),
        });
    }

    // ---------- Answer distribution (N10) ----------

    // Only closed questions have a distribution worth drawing — free text would produce as many
    // slices as submissions.
    chartQuestions(): RegistrationQuestion[] {
        return (this.regForm()?.questions ?? []).filter(q => q.type === 'choice' || q.type === 'multi');
    }

    distribution(q: RegistrationQuestion): PieSlice[] {
        // Stored answers are option ids (ADR-060), so "Да" picked on the RU form and "Yes" on
        // the EN one land in the same bucket; the bucket is displayed under the primary label.
        // Pre-v2 rows stored the label text itself, which simply misses the map and shows as-is.
        const labelById = new Map((q.options ?? []).map(o => [o.id, o.label]));
        const counts = new Map<string, number>();
        for (const r of this.registrations()) {
            if (!r.answersJson) continue;
            let parsed: Record<string, string>;
            try {
                parsed = JSON.parse(r.answersJson) as Record<string, string>;
            } catch {
                continue;
            }
            const raw = parsed[q.id];
            if (raw === undefined) continue;
            const values = q.type === 'multi' ? splitMultiAnswer(raw) : [String(raw)];
            for (const v of values) {
                if (!v.trim()) continue;
                const label = labelById.get(v) ?? v;
                counts.set(label, (counts.get(label) ?? 0) + 1);
            }
        }

        const ordered = [...counts.entries()].sort((a, b) => b[1] - a[1]);
        // Six is the ceiling on distinguishable series; anything past it folds into one "Other"
        // slice rather than inventing a seventh colour.
        const head = ordered.slice(0, SERIES_COUNT - 1);
        const tail = ordered.slice(SERIES_COUNT - 1);
        const slices = head.map(([label, count]) => ({ label, count }));
        if (tail.length) slices.push({ label: this.t().manager.forms.other, count: tail.reduce((sum, [, c]) => sum + c, 0) });

        const total = slices.reduce((sum, s) => sum + s.count, 0);
        if (total === 0) return [];

        // One angle pass, so the arcs and the legend can never disagree about who owns what.
        let angle = -Math.PI / 2;
        return slices.map((s, i) => {
            const sweep = (s.count / total) * Math.PI * 2;
            const slice: PieSlice = {
                label: s.label,
                count: s.count,
                percent: Math.round((s.count / total) * 100),
                color: `var(--series-${(i % SERIES_COUNT) + 1})`,
                path: arcPath(angle, angle + sweep),
            };
            angle += sweep;
            return slice;
        });
    }
}

const SERIES_COUNT = 6;
const PIE_RADIUS = 46;
const PIE_CENTER = 50;

export interface PieSlice {
    label: string;
    count: number;
    percent: number;
    color: string;
    path: string;
}

function splitMultiAnswer(value: string): string[] {
    const trimmed = (value ?? '').trim();
    if (!trimmed.startsWith('[')) return trimmed ? [trimmed] : [];
    try {
        const parsed = JSON.parse(trimmed);
        return Array.isArray(parsed) ? parsed.map(String).filter(v => v.trim().length > 0) : [trimmed];
    } catch {
        return [trimmed];
    }
}

// A single slice covering the whole circle can't be drawn as an arc (start and end coincide, so
// the path collapses) — it becomes two half-circle arcs instead.
function arcPath(start: number, end: number): string {
    const full = end - start >= Math.PI * 2 - 1e-6;
    if (full) {
        const left = `${PIE_CENTER - PIE_RADIUS} ${PIE_CENTER}`;
        const right = `${PIE_CENTER + PIE_RADIUS} ${PIE_CENTER}`;
        return `M ${left} A ${PIE_RADIUS} ${PIE_RADIUS} 0 1 1 ${right} A ${PIE_RADIUS} ${PIE_RADIUS} 0 1 1 ${left} Z`;
    }
    const x1 = PIE_CENTER + PIE_RADIUS * Math.cos(start);
    const y1 = PIE_CENTER + PIE_RADIUS * Math.sin(start);
    const x2 = PIE_CENTER + PIE_RADIUS * Math.cos(end);
    const y2 = PIE_CENTER + PIE_RADIUS * Math.sin(end);
    const largeArc = end - start > Math.PI ? 1 : 0;
    return `M ${PIE_CENTER} ${PIE_CENTER} L ${x1.toFixed(2)} ${y1.toFixed(2)} `
        + `A ${PIE_RADIUS} ${PIE_RADIUS} 0 ${largeArc} 1 ${x2.toFixed(2)} ${y2.toFixed(2)} Z`;
}
