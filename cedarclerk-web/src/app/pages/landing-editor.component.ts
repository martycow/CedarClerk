import {
    Component, DestroyRef, ElementRef, Injector, OnInit, afterNextRender, computed, effect, inject, output, signal, viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import {
    AdminLanding, AdminService, LandingBlock, LandingBlockSpec, LandingDocument, LandingItem,
    LandingSection, LandingStyleSpec, LandingTextMap,
} from '../core/admin.service';
import { ConfirmationService } from '../core/confirmation.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { ButtonComponent } from '../bench/forms/button.component';
import { IconComponent } from '../shared/icon.component';
import { ICONS, IconName } from '../shared/icon-data.generated';
import { SkeletonComponent, heldLoading } from '../shared/skeleton.component';

type TextHolder = { text: Record<string, LandingTextMap> };
type PreviewWidth = 'desktop' | 'phone';

const PREVIEW_DELAY_MS = 500;
const PREVIEW_WIDTHS: Record<PreviewWidth, number> = { desktop: 1280, phone: 390 };

// ADR-323 — Admin › Landing. The document is edited as data and drawn by the server: the preview
// is the public renderer's own output for the unsaved document, so the two cannot disagree.
@Component({
    selector: 'app-landing-editor',
    imports: [FormsModule, ButtonComponent, IconComponent, SkeletonComponent],
    templateUrl: 'landing-editor.component.html',
    styleUrls: ['landing-editor.component.css'],
})
export class LandingEditorComponent implements OnInit {
    private readonly api = inject(AdminService);
    private readonly confirmation = inject(ConfirmationService);
    private readonly sanitizer = inject(DomSanitizer);
    private readonly injector = inject(Injector);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly frameBox = viewChild<ElementRef<HTMLElement>>('frameBox');
    t = inject(LocaleService).t;

    /** The waitlist count rides on the same read, and the tab strip shows it. */
    readonly loaded = output<AdminLanding>();

    data = signal<AdminLanding | null>(null);
    doc = signal<LandingDocument | null>(null);
    busy = signal(false);
    showSkeleton = heldLoading(computed(() => this.busy() && !this.data()));
    error = signal('');
    saved = signal(false);
    dirty = signal(false);
    lang = signal('en');
    announcement = signal('');

    previewHtml = signal<SafeHtml | null>(null);
    previewBusy = signal(false);
    problem = signal<string | null>(null);
    previewWidth = signal<PreviewWidth>('desktop');
    previewBox = signal(0);
    previewScale = computed(() => {
        const width = PREVIEW_WIDTHS[this.previewWidth()];
        const box = this.previewBox();
        return box > 0 && box < width ? box / width : 1;
    });
    readonly previewWidths = PREVIEW_WIDTHS;

    newLayout = 'stack';
    newBlockType: Record<string, string> = {};
    newLanguage = '';
    readonly iconNames = Object.keys(ICONS.regular).sort() as IconName[];

    private previewTimer: ReturnType<typeof setTimeout> | null = null;
    private previewRun = 0;
    private nextId = 0;

    schema = computed(() => this.data()?.schema ?? null);

    // The document is edited in place, so what depends on it is read on every pass, not memoised.
    addableLanguages() {
        const used = new Set(this.doc()?.languages ?? []);
        return (this.schema()?.languages ?? []).filter(l => !used.has(l.code));
    }

    constructor() {
        const destroyed = inject(DestroyRef);
        destroyed.onDestroy(() => {
            if (this.previewTimer) clearTimeout(this.previewTimer);
            this.previewRun++;
        });
        if (typeof ResizeObserver === 'undefined') return;
        const observer = new ResizeObserver(entries => this.previewBox.set(entries[0].contentRect.width));
        effect(() => {
            observer.disconnect();
            const box = this.frameBox();
            if (box) observer.observe(box.nativeElement);
        });
        destroyed.onDestroy(() => observer.disconnect());
    }

    ngOnInit() { void this.load(); }

    async load() {
        this.busy.set(true);
        try {
            const data = await this.api.landing();
            this.data.set(data);
            this.doc.set(data.document);
            if (!data.document.languages.includes(this.lang())) this.lang.set('en');
            this.dirty.set(false);
            this.loaded.emit(data);
            void this.refreshPreview();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.loadFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async save() {
        const doc = this.doc();
        if (!doc || this.busy()) return;
        this.busy.set(true);
        this.error.set('');
        this.saved.set(false);
        try {
            await this.api.saveLanding(doc);
            await this.load();
            this.saved.set(true);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    /** Every edit goes through here: the page is unsaved, and the preview is owed a redraw. */
    changed() {
        this.dirty.set(true);
        this.saved.set(false);
        if (this.previewTimer) clearTimeout(this.previewTimer);
        this.previewTimer = setTimeout(() => void this.refreshPreview(), PREVIEW_DELAY_MS);
    }

    async refreshPreview() {
        const doc = this.doc();
        if (!doc) return;
        if (this.previewTimer) { clearTimeout(this.previewTimer); this.previewTimer = null; }
        const run = ++this.previewRun;
        this.previewBusy.set(true);
        try {
            const preview = await this.api.previewLanding(doc, this.lang());
            if (run !== this.previewRun) return;
            // The frame's sandbox runs no script (same-origin only so the page's fonts load), and the
            // markup is the server renderer's own escaped output — which is what makes handing it
            // over unsanitised sound.
            this.previewHtml.set(this.sanitizer.bypassSecurityTrustHtml(preview.html));
            this.problem.set(preview.problem);
        } catch (e) {
            if (run === this.previewRun) this.problem.set(httpErrorMessage(e, this.t().admin.landing.previewFailed));
        } finally {
            if (run === this.previewRun) this.previewBusy.set(false);
        }
    }

    // ---------- languages ----------

    setLanguage(code: string) {
        this.lang.set(code);
        void this.refreshPreview();
    }

    isRequiredLanguage(code: string): boolean {
        return this.schema()?.requiredLanguages.includes(code) ?? false;
    }

    addLanguage() {
        const doc = this.doc();
        if (!doc || !this.newLanguage || doc.languages.includes(this.newLanguage)) return;
        doc.languages.push(this.newLanguage);
        this.lang.set(this.newLanguage);
        this.newLanguage = '';
        this.changed();
    }

    async removeLanguage(code: string) {
        const doc = this.doc();
        if (!doc || this.isRequiredLanguage(code)) return;
        if (!await this.confirmation.confirm(this.t().admin.landing.removeLanguageConfirm(code.toUpperCase()))) return;
        doc.languages = doc.languages.filter(l => l !== code);
        for (const map of this.allMaps(doc)) delete map[code];
        if (this.lang() === code) this.lang.set('en');
        this.changed();
    }

    private *allMaps(doc: LandingDocument): Generator<LandingTextMap> {
        for (const feature of doc.features) { yield feature.title; yield feature.body; }
        for (const section of doc.sections) {
            yield section.nav;
            yield section.label;
            for (const block of section.blocks) {
                yield* Object.values(block.text);
                for (const item of block.items) {
                    yield* Object.values(item.text);
                    yield* item.entries;
                }
            }
        }
    }

    // ---------- text ----------

    value(map: LandingTextMap | undefined): string {
        return map?.[this.lang()] ?? '';
    }

    setValue(map: LandingTextMap, value: string) {
        if (value) map[this.lang()] = value;
        else delete map[this.lang()];
        this.changed();
    }

    field(holder: TextHolder, key: string): string {
        return this.value(holder.text[key]);
    }

    setField(holder: TextHolder, key: string, value: string) {
        const map = holder.text[key] ??= {};
        this.setValue(map, value);
        if (!Object.keys(map).length) delete holder.text[key];
    }

    /** The languages a text still owes: RU and EN always once it is written or required, never an added one. */
    missing(map: LandingTextMap | undefined, required: boolean): string[] {
        const written = Object.values(map ?? {}).some(v => v.trim());
        if (!required && !written) return [];
        return (this.schema()?.requiredLanguages ?? []).filter(code => !map?.[code]?.trim()).map(c => c.toUpperCase());
    }

    entries(item: LandingItem): string {
        return item.entries.map(entry => entry[this.lang()] ?? '').join('\n');
    }

    /** One entry per line, lined up across languages by line number. */
    setEntries(item: LandingItem, value: string) {
        const lines = value.split('\n');
        const count = Math.max(lines.length, item.entries.length);
        const next: LandingTextMap[] = [];
        for (let i = 0; i < count; i++) {
            const entry = { ...(item.entries[i] ?? {}) };
            if (lines[i]?.trim()) entry[this.lang()] = lines[i];
            else delete entry[this.lang()];
            next.push(entry);
        }
        while (next.length && !Object.keys(next[next.length - 1]).length) next.pop();
        item.entries = next;
        this.changed();
    }

    // ---------- schema ----------

    spec(block: LandingBlock): LandingBlockSpec | undefined {
        return this.schema()?.blocks.find(s => s.type === block.type);
    }

    style(block: LandingBlock): LandingStyleSpec | undefined {
        const spec = this.spec(block);
        return spec?.styles.find(s => s.id === block.style) ?? spec?.styles[0];
    }

    name(group: 'blockTypes' | 'styles' | 'layouts' | 'fields' | 'options' | 'marks', key: string): string {
        return (this.t().admin.landing[group] as Record<string, string>)[key] ?? key;
    }

    /** A style draws its own fields, so text and properties the new one does not draw are dropped. */
    setStyle(block: LandingBlock, id: string) {
        block.style = id;
        const style = this.style(block);
        if (!style) return;
        for (const key of Object.keys(block.text)) if (!style.fields.includes(key)) delete block.text[key];
        if (!style.url) block.url = null;
        if (!style.image) block.image = null;
        for (const item of block.items) {
            for (const key of Object.keys(item.text)) if (!style.itemFields.includes(key)) delete item.text[key];
            item.icon = style.icons ? item.icon ?? 'check' : null;
            item.mark = style.marks ? item.mark ?? 'next' : null;
            if (!style.entries) item.entries = [];
            if (!style.files) item.file = null;
        }
        if (!style.itemFields.length && !style.files) block.items = [];
        else if (style.files) block.items = block.items.filter(item => item.file);
        this.changed();
    }

    // ---------- structure ----------

    private id(prefix: string): string {
        const doc = this.doc()!;
        const used = new Set([
            ...doc.sections.map(s => s.id), ...doc.sections.flatMap(s => s.blocks.map(b => b.id)), ...doc.features.map(f => f.id),
        ]);
        let id: string;
        do id = `${prefix}-${(Date.now() + this.nextId++).toString(36)}`; while (used.has(id));
        return id;
    }

    addSection() {
        this.doc()!.sections.push({
            id: this.id('s'), layout: this.newLayout, anchor: null, hidden: false, nav: {}, label: {}, blocks: [],
        });
        this.changed();
    }

    addableTypes(): LandingBlockSpec[] {
        const used = new Set(this.doc()?.sections.flatMap(s => s.blocks.map(b => b.type)) ?? []);
        return (this.schema()?.blocks ?? []).filter(spec => !spec.single || !used.has(spec.type));
    }

    addBlock(section: LandingSection) {
        const spec = this.schema()?.blocks.find(s => s.type === (this.newBlockType[section.id] || 'text'));
        if (!spec) return;
        const taken = spec.type === 'subscribe'
            && this.doc()!.sections.some(s => s.blocks.some(b => b.type === 'subscribe' && b.style === 'form'));
        const style = taken ? spec.styles[1] : spec.styles[0];
        section.blocks.push({
            id: this.id('b'), type: spec.type, style: style.id, hidden: false, text: {}, items: [],
            options: Object.fromEntries(spec.options.map(o => [o, true])), url: null, image: null,
        });
        this.changed();
    }

    addItem(block: LandingBlock, file: string | null = null) {
        const style = this.style(block);
        if (!style) return;
        block.items.push({
            text: {}, icon: style.icons ? 'check' : null, file, mark: style.marks ? 'next' : null, entries: [],
        });
        this.changed();
    }

    addFeature() {
        this.doc()!.features.push({ id: this.id('f'), icon: 'cube', title: {}, body: {}, shot: null });
        this.changed();
    }

    async remove<T>(list: T[], index: number) {
        if (!await this.confirmation.confirm(this.t().common.removeAuthoredContentConfirm)) return;
        list.splice(index, 1);
        this.changed();
    }

    toggle(target: { hidden: boolean }) {
        target.hidden = !target.hidden;
        this.changed();
    }

    /**
     * Moves by one place and keeps the keyboard where it was: the control that was pressed moves
     * with its row, so the same key can be pressed again.
     */
    move<T>(list: T[], index: number, by: -1 | 1, focusKey?: string) {
        const to = index + by;
        if (to < 0 || to >= list.length) return;
        [list[index], list[to]] = [list[to], list[index]];
        this.announcement.set(this.t().admin.landing.moved(to + 1, list.length));
        this.changed();
        if (!focusKey) return;
        afterNextRender(() => {
            const button = (direction: string) =>
                this.host.nativeElement.querySelector<HTMLButtonElement>(`[data-move="${focusKey}-${direction}"] button`);
            const pressed = button(by < 0 ? 'up' : 'down');
            const fallback = button(by < 0 ? 'down' : 'up');
            (pressed && !pressed.disabled ? pressed : fallback)?.focus();
        }, { injector: this.injector });
    }

    /** Alt+↑ / Alt+↓ on a row's heading moves the row, for a keyboard that would rather not tab to the buttons. */
    moveKey<T>(event: KeyboardEvent, list: T[], index: number) {
        if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
        event.preventDefault();
        const heading = event.currentTarget as HTMLElement;
        this.move(list, index, event.key === 'ArrowUp' ? -1 : 1);
        afterNextRender(() => heading.focus(), { injector: this.injector });
    }

    setOption(block: LandingBlock, key: string, on: boolean) {
        block.options[key] = on;
        this.changed();
    }

    blank(value: string): string | null {
        return value.trim() || null;
    }

    // ---------- files ----------

    fileUrl(file: string | null): string {
        if (!file) return '';
        return file.startsWith('/') ? file : `/landing-media/${encodeURIComponent(file)}`;
    }

    /** Uploaded files first, then whatever the document already points at that is not one of them. */
    fileChoices(current: string | null): string[] {
        const files = this.data()?.files ?? [];
        return current && !files.includes(current) ? [current, ...files] : files;
    }

    private usedFiles(): Set<string> {
        const doc = this.doc();
        const used = new Set<string>();
        if (!doc) return used;
        for (const feature of doc.features) if (feature.shot) used.add(feature.shot);
        for (const block of doc.sections.flatMap(s => s.blocks)) {
            if (block.image) used.add(block.image);
            for (const item of block.items) if (item.file) used.add(item.file);
        }
        return used;
    }

    isUsed(file: string): boolean {
        return this.usedFiles().has(file);
    }

    async upload(event: Event) {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        const data = this.data();
        if (!file || !data) return;
        this.busy.set(true);
        this.error.set('');
        try {
            const saved = await this.api.uploadLandingShot(file);
            this.data.set({ ...data, files: [saved.file, ...data.files] });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.landing.uploadFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async deleteFile(file: string) {
        const data = this.data();
        if (!data || !await this.confirmation.confirm(this.t().common.removeNamed(file))) return;
        this.busy.set(true);
        try {
            await this.api.deleteLandingFile(file);
            this.data.set({ ...data, files: data.files.filter(f => f !== file) });
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().admin.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }
}
