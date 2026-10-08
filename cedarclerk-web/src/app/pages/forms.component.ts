import { ConfirmationService } from '../core/confirmation.service';
import { Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';
import { AuthService } from '../core/auth.service';
import {
    DraftsService, DraftMeta, PostRegistration,
    RegistrationForm, RegistrationQuestion, RegistrationQuestionType, parseRegistrationForm,
} from '../core/drafts.service';
import {
    FormPresetsService, FormPreset, RegistrationFormEdit, FormQuestionEdit,
    normalizeFormForEdit, blankFormEdit, newQuestionId, newOptionId,
} from '../core/form-presets.service';
import { DEFAULT_PRIMARY_LANGUAGE, CONTENT_LANGUAGES } from '../core/languages';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { isPublishableType } from '../core/projects.service';
import { ModalComponent } from '../shared/modal.component';
import { IconComponent } from '../shared/icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { PlanLockComponent } from '../shared/plan-lock.component';
import { LanguageMenuComponent } from '../shared/language-menu.component';
import { HintDotComponent } from '../shared/hint-dot.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { SortDirection } from '../core/collection-query';

export type FormsView = 'presets' | 'submissions';
type PresetSort = 'created' | 'name' | 'questions';

// ADR-316 — registration forms as their own page: the preset library and its editor, and what
// readers submitted through the form of each private post.
@Component({
    selector: 'app-forms',
    imports: [
        IconComponent, ZonedDatePipe, FormsModule, ModalComponent, ButtonComponent, PlanLockComponent,
        HintDotComponent, PageHeaderComponent, EmptyStateComponent, LanguageMenuComponent, InputComponent,
    ],
    templateUrl: 'forms.component.html',
    styleUrls: ['publishing-workspace.css', 'forms.component.css'],
})
export class FormsComponent implements OnInit {
    private readonly confirmation = inject(ConfirmationService);
    auth = inject(AuthService);
    private draftsApi = inject(DraftsService);
    private presetsApi = inject(FormPresetsService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private locale = inject(LocaleService);
    t = this.locale.t;
    /** ADR-322: the open project's posts only; absent with the projects module off. */
    private readonly project = this.route.snapshot.queryParamMap.get('project');

    @ViewChild('presetSheetBody') private presetSheetBody?: ElementRef<HTMLElement>;

    view = signal<FormsView>('presets');
    error = signal('');

    // A form is authored here as a preset and copied onto a post when it is picked there, so a
    // preset belongs to no post.
    presets = signal<FormPreset[]>([]);
    presetsLoading = signal(false);
    presetsLoaded = signal(false);
    presetLoadError = signal('');
    private presetsLoadPromise: Promise<void> | null = null;
    selectedPresetId = signal<string | null>(null);
    presetSearch = signal('');
    presetLanguageFilter = signal('all');
    presetSort = signal<PresetSort>('created');
    presetSortDirection = signal<SortDirection>('desc');
    presetName = '';
    readonly primaryLanguage = DEFAULT_PRIMARY_LANGUAGE;
    readonly contentLanguages = CONTENT_LANGUAGES;
    // ADR-060 — the editor works on the v2 multi-language blob natively: one skeleton of stable
    // question/option ids, per-language texts on top, so "Да" and "Yes" stay one answer.
    presetForm = signal<RegistrationFormEdit | null>(null);
    presetState = signal<'saved' | 'dirty' | 'saving' | 'error'>('saved');
    deletePresetId = signal<string | null>(null);
    presetTranslating = signal<string | null>(null);
    presetTranslateError = signal('');

    posts = signal<DraftMeta[]>([]);
    postsLoading = signal(false);
    private postsLoaded = false;
    selectedPostId = signal<string | null>(null);
    regForm = signal<RegistrationForm | null>(null);
    registrations = signal<PostRegistration[]>([]);
    registrationsLoading = signal(false);
    deleteRegistrationTarget = signal<PostRegistration | null>(null);
    registrationDeleting = signal(false);
    // T-108 — revoking throws a live reader out, so it confirms like delete; restoring lets one
    // back in and does not.
    revokeRegistrationTarget = signal<PostRegistration | null>(null);
    registrationRevoking = signal(false);
    // T-035 — the submission opened in full; null when the list is just a list.
    selectedRegistration = signal<PostRegistration | null>(null);

    async ngOnInit() {
        this.restoreCollectionQuery();
        const params = this.route.snapshot.queryParamMap;
        void this.loadPresets();
        if (params.get('view') === 'submissions' || params.get('post')) {
            this.view.set('submissions');
            await this.loadPosts();
            const asked = this.posts().find(p => p.id === params.get('post'));
            if (asked) await this.selectPost(asked);
        }
    }

    async setView(view: FormsView) {
        if (view === this.view()) return;
        // Leaving the editor with unsaved preset edits commits them rather than dropping them.
        if (this.view() === 'presets') await this.flushPreset();
        this.view.set(view);
        this.syncCollectionQuery();
        if (view === 'submissions') await this.loadPosts();
        else this.resetPresetScroll();
    }

    headerMeta(): HeaderMeta[] {
        const t = this.t().manager.forms;
        return this.presetsLoaded() ? [{ text: t.presetCount(this.presets().length) }] : [];
    }

    // ---------- Submissions ----------

    private async loadPosts() {
        if (this.postsLoaded) return;
        this.postsLoading.set(true);
        try {
            const drafts = await this.draftsApi.list();
            this.posts.set(drafts
                .filter(d => d.isPrivate && !d.isTemplate && isPublishableType(d.documentType)
                    && (!this.project || d.projectId === this.project))
                .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)));
            this.postsLoaded = true;
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.load));
        } finally {
            this.postsLoading.set(false);
        }
    }

    selectedPost(): DraftMeta | null {
        return this.posts().find(p => p.id === this.selectedPostId()) ?? null;
    }

    async selectPost(post: DraftMeta) {
        this.selectedPostId.set(post.id);
        this.registrations.set([]);
        this.regForm.set(null);
        this.syncCollectionQuery();
        this.registrationsLoading.set(true);
        try {
            const [full, regs] = await Promise.all([
                this.draftsApi.get(post.id),
                this.draftsApi.listRegistrations(post.id),
            ]);
            if (this.selectedPostId() !== post.id) return;
            this.regForm.set(parseRegistrationForm(full.registrationFormJson, this.locale.uiLang()));
            this.registrations.set(regs);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadForm));
        } finally {
            this.registrationsLoading.set(false);
        }
    }

    submissionsLabel(): string {
        const post = this.selectedPost();
        return post ? (post.title || this.t().drafts.untitled) : this.t().manager.forms.submissions;
    }

    // Answers are keyed by question id (ADR-042) and resolve to the form's own labels; a question
    // deleted after someone answered it falls back to its raw key rather than vanishing.
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

    // Deleting a submission also revokes that reader's access — the row carries the grant
    // (ADR-084), and removing a test account should mean exactly that. The charts recompute from
    // the updated list on their own: distribution() reads registrations().
    async confirmDeleteRegistration() {
        const d = this.selectedPost();
        const target = this.deleteRegistrationTarget();
        if (!d || !target || this.registrationDeleting()) return;
        this.registrationDeleting.set(true);
        try {
            await this.draftsApi.deleteRegistration(d.id, target.id);
            this.registrations.update(list => list.filter(r => r.id !== target.id));
            this.deleteRegistrationTarget.set(null);
            if (this.selectedRegistration()?.id === target.id) this.selectedRegistration.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadForm));
        } finally {
            this.registrationDeleting.set(false);
        }
    }

    async confirmRevokeRegistration() {
        const target = this.revokeRegistrationTarget();
        if (!target) return;
        const done = await this.setRegistrationRevoked(target, true);
        if (done) this.revokeRegistrationTarget.set(null);
    }

    restoreRegistration(r: PostRegistration) {
        return this.setRegistrationRevoked(r, false);
    }

    private async setRegistrationRevoked(target: PostRegistration, revoked: boolean): Promise<boolean> {
        const d = this.selectedPost();
        if (!d || this.registrationRevoking()) return false;
        this.registrationRevoking.set(true);
        try {
            const res = revoked
                ? await this.draftsApi.revokeRegistration(d.id, target.id)
                : await this.draftsApi.restoreRegistration(d.id, target.id);
            const patch = (r: PostRegistration) => r.id === target.id ? { ...r, isRevoked: res.isRevoked } : r;
            this.registrations.update(list => list.map(patch));
            const open = this.selectedRegistration();
            if (open?.id === target.id) this.selectedRegistration.set(patch(open));
            return true;
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.forms.revokeFailed));
            return false;
        } finally {
            this.registrationRevoking.set(false);
        }
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

    // ---------- Preset library ----------

    readonly availablePresetLanguages = computed(() => [...new Set(
        this.presets().flatMap(p => this.presetLanguagesOf(p)))].sort());

    readonly presetSortOptions = computed(() => {
        const t = this.t().manager.forms;
        return [
            { value: 'created:desc', label: t.sortNewest },
            { value: 'created:asc', label: t.sortOldest },
            { value: 'name:asc', label: t.sortNameAsc },
            { value: 'name:desc', label: t.sortNameDesc },
            { value: 'questions:desc', label: t.sortQuestionsDesc },
            { value: 'questions:asc', label: t.sortQuestionsAsc },
        ];
    });

    presetSortValue(): string {
        return `${this.presetSort()}:${this.presetSortDirection()}`;
    }

    hasPresetFilters(): boolean {
        return !!this.presetSearch().trim() || this.presetLanguageFilter() !== 'all';
    }

    onPresetSearch(value: string) {
        this.presetSearch.set(value);
        this.ensureVisiblePresetSelection();
        this.syncCollectionQuery();
    }

    setPresetLanguageFilter(value: string) {
        this.presetLanguageFilter.set(value);
        this.ensureVisiblePresetSelection();
        this.syncCollectionQuery();
    }

    setPresetSortValue(value: string) {
        const [key, direction] = value.split(':');
        if (!['created', 'name', 'questions'].includes(key)
            || (direction !== 'asc' && direction !== 'desc')) return;
        this.presetSort.set(key as PresetSort);
        this.presetSortDirection.set(direction);
        this.syncCollectionQuery();
    }

    clearPresetFilters() {
        this.presetSearch.set('');
        this.presetLanguageFilter.set('all');
        this.syncCollectionQuery();
    }

    private ensureVisiblePresetSelection() {
        const selected = this.selectedPresetId();
        if (!selected || this.visiblePresets().some(p => p.id === selected)) return;
        void this.flushPreset();
        this.selectedPresetId.set(null);
        this.presetForm.set(null);
        this.presetState.set('saved');
    }

    loadPresets(): Promise<void> {
        if (this.presetsLoaded()) return Promise.resolve();
        if (!this.presetsLoadPromise) {
            this.presetsLoadPromise = this.fetchPresets().finally(() => {
                this.presetsLoadPromise = null;
            });
        }
        return this.presetsLoadPromise;
    }

    private async fetchPresets() {
        this.presetsLoading.set(true);
        this.presetLoadError.set('');
        try {
            const remote = await this.presetsApi.list();
            this.presets.update(current => {
                const currentById = new Map(current.map(p => [p.id, p]));
                const remoteIds = new Set(remote.map(p => p.id));
                return [
                    ...remote.map(p => currentById.get(p.id) ?? p),
                    ...current.filter(p => !remoteIds.has(p.id)),
                ];
            });
            this.presetsLoaded.set(true);
        } catch (e) {
            this.presetLoadError.set(httpErrorMessage(e, this.t().manager.errors.loadPresets));
        } finally {
            this.presetsLoading.set(false);
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
            cached = normalizeFormForEdit(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE).languages;
            this.presetLangsCache.set(p.formJson, cached);
        }
        return cached;
    }

    presetQuestionCount(p: FormPreset): number {
        return normalizeFormForEdit(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE).questions.length;
    }

    visiblePresets(): FormPreset[] {
        const query = this.presetSearch().trim().toLowerCase();
        const language = this.presetLanguageFilter();
        const key = this.presetSort();
        const direction = this.presetSortDirection() === 'asc' ? 1 : -1;
        return this.presets()
            .filter(p => (!query || p.name.toLowerCase().includes(query))
                && (language === 'all' || this.presetLanguagesOf(p).includes(language)))
            .sort((a, b) => {
                const compared = key === 'created'
                    ? a.createdAt.localeCompare(b.createdAt)
                    : key === 'name'
                        ? a.name.localeCompare(b.name)
                        : this.presetQuestionCount(a) - this.presetQuestionCount(b);
                return direction * compared || a.id.localeCompare(b.id);
            });
    }

    async selectPreset(p: FormPreset) {
        await this.flushPreset();
        this.selectedPresetId.set(p.id);
        this.presetName = p.name;
        this.presetForm.set(normalizeFormForEdit(p.formJson, p.language || DEFAULT_PRIMARY_LANGUAGE));
        this.presetState.set('saved');
        this.presetTranslateError.set('');
        this.resetPresetScroll();
    }

    // Created immediately rather than held as a local draft: a preset with no id has nowhere to
    // be saved to, and the list is the only place it would show up.
    async newPreset() {
        await this.flushPreset();
        const blank = blankFormEdit(DEFAULT_PRIMARY_LANGUAGE);
        try {
            const created = await this.presetsApi.create(
                this.t().manager.forms.untitledPreset, JSON.stringify(blank), DEFAULT_PRIMARY_LANGUAGE);
            this.presets.update(list => [...list, created]);
            this.presetsLoaded.set(true);
            this.selectedPresetId.set(created.id);
            this.presetName = created.name;
            this.presetForm.set(blank);
            this.presetState.set('saved');
            this.presetTranslateError.set('');
            this.resetPresetScroll();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.savePreset));
        }
    }

    private resetPresetScroll() {
        const body = this.presetSheetBody?.nativeElement;
        if (body) body.scrollTop = 0;
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
        this.editPreset({ ...form, languages: [...form.languages, lang] });
    }

    // The first language is the skeleton's fallback — everything else may go. Removing one also
    // strips its texts so a re-added language starts clean instead of resurrecting stale copy.
    async removePresetLanguage(lang: string) {
        if (!await this.confirmation.confirm(this.t().common.removeAuthoredContentConfirm)) return;
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
            responseEmailSubject: strip(form.responseEmailSubject),
            responseEmailBody: strip(form.responseEmailBody),
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
            this.presetForm.set(normalizeFormForEdit(saved.formJson, saved.language || DEFAULT_PRIMARY_LANGUAGE));
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

    setReplySubject(lang: string, value: string) {
        this.setFormTextMap('responseEmailSubject', lang, value);
    }

    setReplyBody(lang: string, value: string) {
        this.setFormTextMap('responseEmailBody', lang, value);
    }

    /** Shared by both reply fields: a blank clears the entry rather than storing an empty string. */
    private setFormTextMap(key: 'responseEmailSubject' | 'responseEmailBody', lang: string, value: string) {
        const form = this.presetForm();
        if (!form) return;
        const next = { ...form[key] };
        if (value.trim()) next[lang] = value.trim(); else delete next[lang];
        this.editPreset({ ...form, [key]: next });
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

    async removeQuestion(id: string) {
        if (!await this.confirmation.confirm(this.t().common.removeAuthoredContentConfirm)) return;
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
        if (type === 'consent') { this.updateQuestion(id, { type, required: true, options }); return; }
        // T-031 — a required block nobody can fill in is a form that cannot be submitted; Core's
        // Parse forces this too, so a hand-edited blob can't bypass it either.
        if (type === 'static') { this.updateQuestion(id, { type, required: false, options }); return; }
        this.updateQuestion(id, { type, options });
    }

    setQuestionImage(id: string, url: string) {
        this.updateQuestion(id, { imageUrl: url.trim() || null });
    }

    addOption(qId: string) {
        const q = this.presetForm()?.questions.find(x => x.id === qId);
        if (!q) return;
        this.updateQuestion(qId, { options: [...q.options, { id: newOptionId(), label: {} }] });
    }

    async removeOption(qId: string, optId: string) {
        if (!await this.confirmation.confirm(this.t().common.removeAuthoredContentConfirm)) return;
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

    presetSheetLabel(): string {
        const preset = this.presets().find(p => p.id === this.selectedPresetId());
        return preset?.name || this.t().manager.forms.crumb;
    }

    presetSheetMeta(): string {
        const form = this.presetForm();
        return form ? this.presetLangs().map(l => l.toUpperCase()).join(' ') : '';
    }

    private restoreCollectionQuery() {
        const params = this.route.snapshot.queryParamMap;
        this.presetSearch.set(params.get('formq') ?? '');
        const presetLanguage = params.get('formlanguage');
        if (presetLanguage) this.presetLanguageFilter.set(presetLanguage);
        const presetSort = params.get('formsort');
        const presetDirection = params.get('formdir');
        if (presetSort && ['created', 'name', 'questions'].includes(presetSort))
            this.presetSort.set(presetSort as PresetSort);
        if (presetDirection === 'asc' || presetDirection === 'desc')
            this.presetSortDirection.set(presetDirection);
    }

    private syncCollectionQuery() {
        void this.router.navigate([], {
            relativeTo: this.route,
            replaceUrl: true,
            queryParamsHandling: 'merge',
            queryParams: {
                view: this.view() === 'presets' ? null : this.view(),
                post: this.view() === 'submissions' ? this.selectedPostId() : null,
                formq: this.presetSearch().trim() || null,
                formlanguage: this.presetLanguageFilter() === 'all' ? null : this.presetLanguageFilter(),
                formsort: this.presetSort() === 'created' ? null : this.presetSort(),
                formdir: this.presetSort() === 'created' && this.presetSortDirection() === 'desc'
                    ? null : this.presetSortDirection(),
            },
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
