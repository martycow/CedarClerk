import { Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { Preset, PresetsService, parseDocumentConfig } from '../core/presets.service';
import { DOCUMENT_TYPES, DocumentType, DOCUMENT_TYPE_ICONS } from '../core/projects.service';
import { RulerService } from '../core/ruler.service';
import { IconComponent } from '../shared/icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { HintDotComponent } from '../shared/hint-dot.component';

// T-331/T-355 — the Preset Manager. Document presets today: a named starting point that bundles a
// base type (what it publishes as) and a heading skeleton the new document is born with. This is
// what gives a Document Type real meaning — a preset is the type made concrete and editable.
@Component({
    selector: 'app-presets',
    imports: [FormsModule, NgTemplateOutlet, IconComponent, ButtonComponent, WorktopComponent, ShelfPanelComponent, HintDotComponent],
    templateUrl: 'presets.component.html',
    styleUrls: ['presets.component.css'],
})
export class PresetsComponent implements OnDestroy {
    private api = inject(PresetsService);
    private ruler = inject(RulerService);
    t = inject(LocaleService).t;

    readonly baseTypes = DOCUMENT_TYPES;
    readonly typeIcons = DOCUMENT_TYPE_ICONS;

    presets = signal<Preset[]>([]);
    loading = signal(true);
    error = signal<string | null>(null);
    busy = signal(false);

    // The one being edited (its id) or 'new' while creating, or null.
    editingId = signal<string | null>(null);
    form = signal<{ name: string; baseType: DocumentType; icon: string; headings: string }>(this.blank());

    parsed = (p: Preset) => parseDocumentConfig(p.configJson);

    constructor() {
        void this.load();
        effect(() => this.ruler.publish({
            label: this.t().presets.crumb,
            right: [{ text: this.t().presets.count(this.presets().length) }],
        }));
    }

    ngOnDestroy() { this.ruler.clear(); }

    private blank() {
        return { name: '', baseType: 'post' as DocumentType, icon: 'file-text', headings: '' };
    }

    async load() {
        this.loading.set(true);
        this.error.set(null);
        try { this.presets.set(await this.api.list('document')); }
        catch (e) { this.error.set(httpErrorMessage(e, this.t().presets.loadFailed)); }
        finally { this.loading.set(false); }
    }

    startNew() {
        this.form.set(this.blank());
        this.editingId.set('new');
    }

    startEdit(p: Preset) {
        const c = this.parsed(p);
        this.form.set({ name: p.name, baseType: c.baseType as DocumentType, icon: c.icon, headings: c.headings.join('\n') });
        this.editingId.set(p.id);
    }

    cancel() { this.editingId.set(null); }

    // The base type's own icon is the sensible default; the user does not pick an icon separately.
    pickBaseType(type: DocumentType) {
        this.form.update(f => ({ ...f, baseType: type, icon: this.typeIcons[type] }));
    }

    canSave = computed(() => this.form().name.trim().length > 0);

    async save() {
        if (!this.canSave() || this.busy()) return;
        const f = this.form();
        const input = {
            kind: 'document' as const,
            name: f.name.trim(),
            baseType: f.baseType,
            icon: this.typeIcons[f.baseType],
            headings: f.headings.split('\n').map(h => h.trim()).filter(Boolean),
        };
        this.busy.set(true);
        this.error.set(null);
        try {
            const id = this.editingId();
            if (id && id !== 'new') await this.api.update(id, input);
            else await this.api.create(input);
            this.editingId.set(null);
            await this.load();
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().presets.saveFailed));
        } finally {
            this.busy.set(false);
        }
    }

    async remove(p: Preset) {
        if (!confirm(this.t().presets.deleteConfirm(p.name))) return;
        this.busy.set(true);
        try { await this.api.remove(p.id); await this.load(); }
        catch (e) { this.error.set(httpErrorMessage(e, this.t().presets.saveFailed)); }
        finally { this.busy.set(false); }
    }

    baseTypeName(type: string): string {
        const t = this.t().projects.docTypes;
        return (t as Record<string, { name: string }>)[type]?.name ?? type;
    }
}
