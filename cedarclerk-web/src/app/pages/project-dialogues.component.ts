import { Component, OnDestroy, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { DialogueScriptSummary, DialoguesService } from '../core/dialogues.service';
import { ProjectsService } from '../core/projects.service';
import { RailActionsService } from '../core/rail-actions.service';
import { RulerService } from '../core/ruler.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';

// The Yarn dialogue tool's list — the same sheet-per-thing shape as the board list, minus the
// people dock: dialogues are owner-only, so there is nobody to list beside them.
@Component({
    selector: 'app-project-dialogues',
    imports: [RouterLink, IconComponent, ModalComponent, WorktopComponent, ButtonComponent, InputComponent],
    templateUrl: 'project-dialogues.component.html',
    styleUrls: ['project-dialogues.component.css'],
})
export class ProjectDialoguesComponent implements OnDestroy {
    private api = inject(DialoguesService);
    private projects = inject(ProjectsService);
    private route = inject(ActivatedRoute);
    private ruler = inject(RulerService);
    private rail = inject(RailActionsService);
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

    constructor() {
        this.route.paramMap.subscribe(params => {
            const id = params.get('id');
            if (!id) return;
            this.projectId.set(id);
            void this.load();
        });

        effect(() => {
            const t = this.t().projects.dialogues;
            this.ruler.publish({
                label: this.projectName(),
                left: [{ text: t.sub(this.scripts().length) }],
            });
            this.rail.publish({
                primary: { label: t.newScript, icon: 'plus', run: () => this.openCreate() },
            });
        });
    }

    ngOnDestroy(): void {
        this.ruler.clear();
        this.rail.clear();
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
