import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import {
    DraftsService, DraftMeta, FolderMeta, PostRegistration,
    RegistrationForm, RegistrationQuestion, RegistrationQuestionType, parseRegistrationForm,
} from '../core/drafts.service';
import { FormPresetsService, FormPreset } from '../core/form-presets.service';
import { CommentsService } from '../core/comments.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CountBadgeComponent } from '../shared/count-badge.component';
import { httpErrorMessage } from '../core/http-error.util';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { AccountMenuComponent } from '../shared/account-menu.component';
import { ModalComponent } from '../shared/modal.component';
import { CommentsComponent } from './comments.component';
import { StatsComponent } from './stats.component';
import {
    LucideTrash2 as Trash2, LucideArchive as Archive, LucideArchiveRestore as ArchiveRestore,
    LucidePenLine as PenLine, LucideLock as Lock, LucideExternalLink as ExternalLink,
    LucideRefreshCw as RefreshCw, LucideArrowLeft as ArrowLeft,
} from '@lucide/angular';

export type ManagerTab = 'posts' | 'feedback' | 'stats' | 'forms';
const MANAGER_TABS: ManagerTab[] = ['posts', 'feedback', 'stats', 'forms'];

// N7 — the Posts Manager. Comments/reactions and stats used to be two separate top-level pages
// with their own headers; they are now tab bodies here (their routes redirect), so there is one
// place that answers "what happened to my posts". The forms tab is intentionally read-only for
// now — editing, per-question breakdowns and the pie chart are N10, presets are N12.
@Component({
    selector: 'app-posts-manager',
    imports: [
        DatePipe, FormsModule, RouterLink, CedarLogoComponent, ModalComponent, CommentsComponent,
        StatsComponent, CountBadgeComponent, AccountMenuComponent,
        Trash2, Archive, ArchiveRestore, PenLine, Lock, ExternalLink, RefreshCw, ArrowLeft,
    ],
    templateUrl: 'posts-manager.component.html',
    styleUrls: ['posts-manager.component.css'],
})
export class PostsManagerComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    private draftsApi = inject(DraftsService);
    private presetsApi = inject(FormPresetsService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    feedback = inject(CommentsService);
    t = inject(LocaleService).t;

    tab = signal<ManagerTab>('posts');
    loading = signal(true);
    error = signal('');
    busy = signal(false);

    drafts = signal<DraftMeta[]>([]);
    folders = signal<FolderMeta[]>([]);
    selectedId = signal<string | null>(null);

    // Minimal edits (Marty's wording) — everything here changes metadata only. Body text stays
    // the editor's job; the rename below still has to round-trip cedarJson because the save
    // endpoint takes title and body together.
    editTitle = '';
    editTags = '';
    renaming = signal(false);
    deleteConfirmId = signal<string | null>(null);

    registrations = signal<PostRegistration[]>([]);
    registrationsLoading = signal(false);

    // Forms tab (N10) — the form editor moved here from the export modal, which now only picks a
    // preset before publishing (N12, ADR-047).
    regForm = signal<RegistrationForm | null>(null);
    regBusy = signal(false);
    presets = signal<FormPreset[]>([]);
    newPresetName = '';

    // I9 — the form editor used to persist silently on every keystroke/toggle, which left no way
    // to tell whether anything had been saved. Edits now mark the form dirty and an explicit Save
    // button commits them; switching post or leaving the tab flushes first, so nothing is lost.
    formState = signal<'saved' | 'dirty' | 'saving' | 'error'>('saved');

    async ngOnInit() {
        this.feedback.refreshNewCount();
        try {
            const [drafts, folders] = await Promise.all([this.draftsApi.list(), this.draftsApi.listFolders()]);
            this.drafts.set(drafts);
            this.folders.set(folders);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.load));
        } finally {
            this.loading.set(false);
        }
        // The export modal links straight here when no preset exists yet (I9), so land on the
        // tab that was asked for rather than on the default one.
        const requested = this.route.snapshot.queryParamMap.get('tab');
        if (requested && MANAGER_TABS.includes(requested as ManagerTab)) this.setTab(requested as ManagerTab);
    }

    setTab(tab: ManagerTab) {
        // Leaving the feedback tab is when its badge is worth re-checking: hovering rows there
        // is exactly what clears the count.
        if (this.tab() === 'feedback' && tab !== 'feedback') this.feedback.refreshNewCount();
        // Leaving the forms tab with unsaved edits commits them rather than dropping them (I9).
        if (this.tab() === 'forms' && tab !== 'forms') this.flushForm();
        this.tab.set(tab);
        if (tab === 'forms') {
            // Presets are independent of any post, so they load with the tab, not with a selection.
            if (!this.presets().length) this.loadPresets();
            if (this.selected()?.isPrivate) this.loadForm();
        }
    }

    // The forms tab only ever deals with private posts — that's the whole point of it.
    privatePosts(): DraftMeta[] {
        return this.drafts().filter(d => d.isPrivate);
    }

    selected(): DraftMeta | null {
        const id = this.selectedId();
        return id ? this.drafts().find(d => d.id === id) ?? null : null;
    }

    async select(d: DraftMeta) {
        // Same guard the editor uses when switching drafts: commit before the form is replaced.
        await this.flushForm();
        this.selectedId.set(d.id);
        this.editTitle = d.title;
        this.editTags = d.tags;
        this.registrations.set([]);
        this.regForm.set(null);
        this.formState.set('saved');
        if (this.tab() === 'forms' && d.isPrivate) this.loadForm();
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
            const tags = this.editTags.trim();
            if (tags !== d.tags) await this.draftsApi.updateTags(d.id, tags);
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
            this.folders.set(await this.draftsApi.listFolders());
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
                return {
                    label: q?.label || key,
                    value: q?.type === 'multi' ? splitMultiAnswer(value).join(', ') : String(value),
                };
            });
    }

    // ---------- Form editor (N10) ----------

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
            this.formState.set('saved');
            this.registrations.set(regs);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadForm));
        } finally {
            this.registrationsLoading.set(false);
        }
    }

    // The definition is small and always fully in hand, so a save writes the whole blob — same
    // reasoning as the editor's original version of this.
    private async persistForm() {
        const d = this.selected();
        if (!d) return;
        this.regBusy.set(true);
        this.formState.set('saving');
        try {
            const form = this.regForm();
            await this.draftsApi.setRegistrationForm(d.id, form ? JSON.stringify(form) : null);
            this.formState.set('saved');
        } catch (e) {
            this.formState.set('error');
            this.error.set(httpErrorMessage(e, this.t().manager.errors.saveForm));
        } finally {
            this.regBusy.set(false);
        }
    }

    // Every field edit routes through here (I9): change the blob, mark it dirty, wait for Save.
    private editForm(next: RegistrationForm | null) {
        this.regForm.set(next);
        this.formState.set('dirty');
    }

    async saveForm() {
        await this.persistForm();
    }

    // Called before anything that would navigate away from the current form.
    private async flushForm() {
        if (this.formState() === 'dirty') await this.persistForm();
    }

    // Turning the form on/off is structural rather than an edit — it changes what the public page
    // does with an uninvited visitor, so it commits straight away instead of waiting for Save.
    async toggleForm() {
        this.regForm.set(this.regForm()
            ? null
            : { requireName: true, requireNickname: false, requireEmail: true, requireSocial: false, questions: [] });
        await this.persistForm();
    }

    async deleteForm() {
        this.regForm.set(null);
        await this.persistForm();
    }

    toggleFormField(field: 'requireName' | 'requireNickname' | 'requireEmail' | 'requireSocial') {
        const form = this.regForm();
        if (!form) return;
        this.editForm({ ...form, [field]: !form[field] });
    }

    setIntro(intro: string) {
        const form = this.regForm();
        if (!form) return;
        this.editForm({ ...form, intro: intro.trim() || undefined });
    }

    addQuestion() {
        const form = this.regForm();
        if (!form) return;
        const q: RegistrationQuestion = { id: `q${Date.now()}`, label: '', type: 'text', required: false };
        this.editForm({ ...form, questions: [...form.questions, q] });
    }

    updateQuestion(id: string, patch: Partial<RegistrationQuestion>) {
        const form = this.regForm();
        if (!form) return;
        this.editForm({ ...form, questions: form.questions.map(q => q.id === id ? { ...q, ...patch } : q) });
    }

    removeQuestion(id: string) {
        const form = this.regForm();
        if (!form) return;
        this.editForm({ ...form, questions: form.questions.filter(q => q.id !== id) });
    }

    setQuestionType(id: string, type: RegistrationQuestionType) {
        this.updateQuestion(id, { type });
    }

    setQuestionOptions(id: string, raw: string) {
        this.updateQuestion(id, { options: raw.split(',').map(o => o.trim()).filter(o => o.length > 0) });
    }

    questionOptionsText(q: RegistrationQuestion): string {
        return (q.options ?? []).join(', ');
    }

    // ---------- Presets (N12) ----------

    async loadPresets() {
        try {
            this.presets.set(await this.presetsApi.list());
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.loadPresets));
        }
    }

    async saveAsPreset() {
        const form = this.regForm();
        const name = this.newPresetName.trim();
        if (!form || !name || this.regBusy()) return;
        this.regBusy.set(true);
        try {
            const created = await this.presetsApi.create(name, JSON.stringify(form));
            this.presets.update(list => [...list, created].sort((a, b) => a.name.localeCompare(b.name)));
            this.newPresetName = '';
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.savePreset));
        } finally {
            this.regBusy.set(false);
        }
    }

    // Copies the preset's definition onto the post. Editing the preset afterwards does not touch
    // posts that already applied it — that's the point of copying instead of linking.
    async applyPreset(p: FormPreset) {
        this.regForm.set(parseRegistrationForm(p.formJson));
        await this.persistForm();
    }

    async deletePreset(p: FormPreset) {
        try {
            await this.presetsApi.remove(p.id);
            this.presets.update(list => list.filter(x => x.id !== p.id));
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().manager.errors.deletePreset));
        }
    }

    // ---------- Answer distribution (N10) ----------

    // Only closed questions have a distribution worth drawing — free text would produce as many
    // slices as submissions.
    chartQuestions(): RegistrationQuestion[] {
        return (this.regForm()?.questions ?? []).filter(q => q.type === 'choice' || q.type === 'multi');
    }

    distribution(q: RegistrationQuestion): PieSlice[] {
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
                counts.set(v, (counts.get(v) ?? 0) + 1);
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
