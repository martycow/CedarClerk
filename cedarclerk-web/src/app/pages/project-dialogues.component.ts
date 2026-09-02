import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { DialogueScriptSummary, DialoguesService } from '../core/dialogues.service';
import { ProjectsService } from '../core/projects.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';

// The Yarn dialogue tool's list — the same card-per-thing shape as the board list, minus the
// people column: dialogues are owner-only, so there is nobody to list beside them.
@Component({
    selector: 'app-project-dialogues',
    imports: [RouterLink, IconComponent, ModalComponent, PageHeaderComponent, EmptyStateComponent, ButtonComponent, InputComponent],
    templateUrl: 'project-dialogues.component.html',
    styleUrls: ['project-dialogues.component.css'],
})
export class ProjectDialoguesComponent {
    private api = inject(DialoguesService);
    private projects = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    t = inject(LocaleService).t;

    projectId = signal('');
    projectName = signal('');
    scripts = signal<readonly DialogueScriptSummary[]>([]);
    loading = signal(true);
    loadError = signal<string | null>(null);
    actionError = signal<string | null>(null);
    busy = signal(false);

    editing = signal<DialogueScriptSummary | null>(null);
    creating = signal(false);
    formName = signal('');
    confirmDelete = signal<DialogueScriptSummary | null>(null);

    /** The former rule readout: how many dialogues. */
    headerMeta = computed<HeaderMeta[]>(() => [{ text: this.t().projects.dialogues.sub(this.scripts().length) }]);

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });
    }

    async load() {
        const id = this.projectId();
        if (!id) return;
        this.loading.set(true);
        this.loadError.set(null);
        try {
            this.scripts.set(await this.api.list(id));
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.dialogues.loadFailed));
        } finally {
            this.loading.set(false);
        }
        try {
            this.projectName.set((await this.projects.get(id)).name);
        } catch {
            this.projectName.set('');
        }
    }

    openCreate() {
        this.editing.set(null);
        this.formName.set('');
        this.actionError.set(null);
        this.creating.set(true);
    }

    openRename(script: DialogueScriptSummary) {
        this.editing.set(script);
        this.formName.set(script.name);
        this.actionError.set(null);
        this.creating.set(true);
    }

    closeForm() {
        this.creating.set(false);
        this.editing.set(null);
    }

    async submitForm() {
        const name = this.formName().trim();
        if (!name) {
            this.actionError.set(this.t().projects.dialogues.nameRequired);
            return;
        }
        if (this.busy()) return;
        this.busy.set(true);
        this.actionError.set(null);
        try {
            const script = this.editing();
            if (script) {
                const next = await this.api.save(script.id, { name });
                this.scripts.set(this.scripts().map(s => (s.id === next.id ? { ...s, name: next.name } : s)));
            } else {
                const next = await this.api.create(this.projectId(), name);
                this.scripts.set([
                    { id: next.id, name: next.name, projectId: next.projectId, createdAt: next.createdAt, updatedAt: next.updatedAt, nodeCount: 1 },
                    ...this.scripts(),
                ]);
            }
            this.closeForm();
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }

    openDelete(script: DialogueScriptSummary) {
        this.actionError.set(null);
        this.confirmDelete.set(script);
    }

    async removeScript() {
        const script = this.confirmDelete();
        if (!script || this.busy()) return;
        this.busy.set(true);
        try {
            await this.api.remove(script.id);
            this.scripts.set(this.scripts().filter(s => s.id !== script.id));
            this.confirmDelete.set(null);
        } catch (e) {
            this.actionError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.busy.set(false);
        }
    }
}
