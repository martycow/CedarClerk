import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { RouterLink } from '@angular/router';
import { AiOperation, AiOperationStatus, AiOperationsService } from '../core/ai-operations.service';
import { ConfirmationService } from '../core/confirmation.service';
import { LocaleService } from '../core/i18n/locale.service';
import { WorkspaceObject } from '../core/workspace-context.service';
import { IndexTabItem, IndexTabsComponent } from '../bench/chrome/index-tabs.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';

type StatusFilter = 'all' | 'running' | 'applied' | 'failed';

const STATUS_ICONS: Record<AiOperationStatus, IconName> = {
    running: 'circle-notch',
    review: 'eye',
    applied: 'check',
    failed: 'warning',
    cancelled: 'prohibit',
    undone: 'arrow-counter-clockwise',
};

// ADR-301 clause 5/6 — the record every AI run leaves: what it was asked to do, what it touched,
// what it cost, and the way back where one exists. Credits are NOT restated here; the summary links
// to Settings → Billing, which stays their one home.
@Component({
    selector: 'app-ai-operations',
    imports: [
        RouterLink, IconComponent, ButtonComponent, IndexTabsComponent,
        PageHeaderComponent, EmptyStateComponent,
    ],
    templateUrl: 'ai-operations.component.html',
    styleUrls: ['ai-operations.component.css'],
})
export class AiOperationsComponent {
    private readonly confirmation = inject(ConfirmationService);
    private readonly router = inject(Router);
    protected readonly api = inject(AiOperationsService);
    t = inject(LocaleService).t;

    readonly filter = signal<StatusFilter>('all');
    readonly selectedId = signal<string | null>(null);

    readonly operations = computed(() => {
        const all = this.api.operations();
        switch (this.filter()) {
            case 'running': return all.filter(op => op.status === 'running' || op.status === 'review');
            case 'applied': return all.filter(op => op.status === 'applied' || op.status === 'undone');
            case 'failed': return all.filter(op => op.status === 'failed' || op.status === 'cancelled');
            default: return all;
        }
    });

    /** The first row when nothing was picked, so the panel is never blank beside a full list. */
    readonly selected = computed<AiOperation | null>(() => {
        const id = this.selectedId();
        const list = this.operations();
        return (id ? list.find(op => op.id === id) : null) ?? list[0] ?? null;
    });

    readonly tabs = computed<IndexTabItem[]>(() => {
        const t = this.t().ai;
        const all = this.api.operations();
        const count = (predicate: (op: AiOperation) => boolean) => all.filter(predicate).length;
        return [
            { id: 'all', label: t.tabAll, badge: all.length },
            { id: 'running', label: t.tabRunning, badge: count(op => op.status === 'running' || op.status === 'review') },
            { id: 'applied', label: t.tabApplied, badge: count(op => op.status === 'applied' || op.status === 'undone') },
            { id: 'failed', label: t.tabFailed, badge: count(op => op.status === 'failed' || op.status === 'cancelled') },
        ];
    });

    readonly headerMeta = computed(() => {
        const t = this.t().ai;
        const spent = this.api.creditsSpent();
        const meta: HeaderMeta[] = [{ text: t.metaTotal(this.api.operations().length) }];
        if (spent > 0) meta.push({ text: t.metaCredits(spent), tone: 'muted' });
        return meta;
    });

    statusIcon(status: AiOperationStatus): IconName {
        return STATUS_ICONS[status];
    }

    statusLabel(status: AiOperationStatus): string {
        return this.t().ai.status[status];
    }

    changedCount(operation: AiOperation): number {
        return operation.changes.filter(change => change.changed).length;
    }

    untouchedCount(operation: AiOperation): number {
        return operation.changes.filter(change => !change.changed).length;
    }

    canUndo(operation: AiOperation): boolean {
        return this.api.canUndo(operation);
    }

    duration(operation: AiOperation): string {
        if (!operation.finishedAt) return '';
        const ms = Date.parse(operation.finishedAt) - Date.parse(operation.startedAt);
        if (!Number.isFinite(ms) || ms < 0) return '';
        return this.t().ai.duration(Math.max(1, Math.round(ms / 1000)));
    }

    pick(id: string): void {
        this.selectedId.set(id);
    }

    setFilter(id: string): void {
        this.filter.set(id as StatusFilter);
        this.selectedId.set(null);
    }

    openObject(object: WorkspaceObject): void {
        if (object.kind !== 'document') return;
        void this.router.navigate(['/editor'], { queryParams: { draft: object.id } });
    }

    async undo(operation: AiOperation): Promise<void> {
        if (!this.canUndo(operation)) return;
        const ok = await this.confirmation.confirm({
            title: this.t().ai.undoTitle,
            message: this.t().ai.undoConfirm(this.changedCount(operation)),
            confirmLabel: this.t().ai.undo,
        });
        if (!ok) return;
        // The record is what this screen owns; restoring a revision is the editor's own path, and
        // sending the user there is honest about where the change actually gets rolled back.
        this.api.update(operation.id, { status: 'undone' });
        const first = operation.changes.find(change => change.changed && change.revisionId);
        if (first) void this.router.navigate(['/editor'], { queryParams: { draft: first.objectId } });
    }

    async clear(): Promise<void> {
        const ok = await this.confirmation.confirm({
            title: this.t().ai.clearTitle,
            message: this.t().ai.clearConfirm,
            confirmLabel: this.t().common.delete,
        });
        if (ok) this.api.clear();
    }
}
