import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { AiOperationsComponent } from './ai-operations.component';
import { AiOperation, AiOperationsService } from '../core/ai-operations.service';
import { ConfirmationService } from '../core/confirmation.service';
import { en } from '@localization/en';

function operation(over: Partial<AiOperation> = {}): AiOperation {
    return {
        id: 'op-1',
        kind: 'edit',
        task: 'Bring the descriptions to one style',
        status: 'applied',
        startedAt: '2026-09-16T10:00:00.000Z',
        finishedAt: '2026-09-16T10:00:12.000Z',
        scope: [{ id: 'd1', kind: 'document', title: 'Patch notes' }],
        protectedFields: ['Slug'],
        changes: [
            { objectId: 'd1', title: 'Patch notes', changed: true, revisionId: 'r1' },
            { objectId: 'd2', title: 'Roadmap', changed: false },
        ],
        credits: 4,
        ...over,
    };
}

class OperationsStub {
    readonly items = signal<readonly AiOperation[]>([]);
    readonly operations = this.items.asReadonly();
    creditsSpent = () => this.items().reduce((total, op) => total + (op.credits ?? 0), 0);
    canUndo = (op: AiOperation) =>
        op.status === 'applied' && op.changes.some(change => change.changed && !!change.revisionId);
    update = () => undefined;
    clear = () => this.items.set([]);
}

describe('AI operations log', () => {
    let fixture: ComponentFixture<AiOperationsComponent>;
    let api: OperationsStub;
    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;
    const t = en.ai;

    async function settle() {
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        api = new OperationsStub();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: AiOperationsService, useValue: api },
                { provide: ConfirmationService, useValue: { confirm: () => Promise.resolve(true) } },
            ],
        });
        fixture = TestBed.createComponent(AiOperationsComponent);
        await settle();
    });

    it('offers the empty state before any operation has run', () => {
        expect(el().querySelector('app-empty-state')).toBeTruthy();
        expect(el().textContent).toContain(t.emptyTitle);
        expect(el().querySelectorAll('.op-card')).toHaveLength(0);
    });

    it('lists an operation with what it changed and what it left alone', async () => {
        api.items.set([operation()]);
        await settle();

        const card = el().querySelector('.op-card');
        expect(card?.textContent).toContain('Bring the descriptions to one style');
        expect(card?.textContent).toContain(t.changedCount(1));
        expect(card?.textContent).toContain(t.untouchedCount(1));
        expect(card?.textContent).toContain(t.creditsSpent(4));
    });

    // Nothing picked must still fill the panel, or a full list sits beside a blank aside.
    it('inspects the first row when none was picked', async () => {
        api.items.set([operation({ id: 'op-a' }), operation({ id: 'op-b' })]);
        await settle();

        expect(page().selected()?.id).toBe('op-a');
        page().pick('op-b');
        await settle();
        expect(page().selected()?.id).toBe('op-b');
    });

    it('names the protected fields the run was not allowed to touch', async () => {
        api.items.set([operation()]);
        await settle();

        expect(el().querySelector('.insp-protected')?.textContent).toContain('Slug');
    });

    // ADR-301 clause 6 — offered only where a revision exists to go back to.
    it('offers undo only when a revision was saved before the run', async () => {
        api.items.set([operation()]);
        await settle();
        expect(el().querySelector('.insp-actions app-button')).toBeTruthy();

        api.items.set([operation({ changes: [{ objectId: 'd1', title: 'Patch notes', changed: true }] })]);
        await settle();
        expect(el().querySelector('.insp-actions app-button')).toBeNull();
        expect(el().querySelector('.insp-note')?.textContent).toContain(t.undoUnavailable);
    });

    it('filters by status and counts every tab off the whole log', async () => {
        api.items.set([operation({ id: 'a' }), operation({ id: 'b', status: 'failed' })]);
        await settle();

        expect(page().tabs().map(tab => tab.badge)).toEqual([2, 0, 1, 1]);

        page().setFilter('failed');
        await settle();
        expect(page().operations().map(op => op.id)).toEqual(['b']);
        expect(page().selected()?.id).toBe('b');
    });

    it('reports the run time in whole seconds', () => {
        expect(page().duration(operation())).toBe(t.duration(12));
        expect(page().duration(operation({ finishedAt: undefined }))).toBe('');
    });
});
