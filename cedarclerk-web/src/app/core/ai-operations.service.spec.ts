import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { AiOperation, AiOperationsService } from './ai-operations.service';
import { AuthService } from './auth.service';
import { WorkspaceObject } from './workspace-context.service';

const doc = (id: string): WorkspaceObject => ({ id, kind: 'document', title: id });

function seed(over: Partial<AiOperation> = {}) {
    return {
        kind: 'edit' as const,
        task: 'One style',
        scope: [doc('d1')],
        protectedFields: ['Slug'],
        ...over,
    };
}

describe('AI operation log', () => {
    const email = signal<string | null>('a@local.test');
    let service: AiOperationsService;

    function create(): AiOperationsService {
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({
            providers: [{ provide: AuthService, useValue: { userEmail: email } }],
        });
        const created = TestBed.inject(AiOperationsService);
        TestBed.tick();
        return created;
    }

    beforeEach(() => {
        localStorage.clear();
        email.set('a@local.test');
        service = create();
    });

    // NG0600: the log used to load itself from inside a computed, which throws the first time the
    // screen reads it. Reading the list must be a plain read.
    it('reads the list without writing a signal', () => {
        expect(() => service.operations()).not.toThrow();
        expect(service.operations()).toEqual([]);
    });

    it('opens a record as running before any work answers', () => {
        const operation = service.start(seed());
        expect(operation.status).toBe('running');
        expect(operation.startedAt).toBeTruthy();
        expect(service.operations()).toHaveLength(1);
        expect(service.running()).toHaveLength(1);
    });

    it('finishes a record with its status and stamps the end', () => {
        const { id } = service.start(seed());
        service.finish(id, 'applied', { credits: 4 });

        const stored = service.find(id)!;
        expect(stored.status).toBe('applied');
        expect(stored.finishedAt).toBeTruthy();
        expect(stored.credits).toBe(4);
        expect(service.creditsSpent()).toBe(4);
        expect(service.running()).toHaveLength(0);
    });

    // ADR-301 clause 6 — undo is offered only where a revision exists to restore.
    it('allows undo only for an applied run with a restorable change', () => {
        const withRevision = service.start(seed({
            changes: [{ objectId: 'd1', title: 'd1', changed: true, revisionId: 'r1' }],
        }));
        const without = service.start(seed({
            changes: [{ objectId: 'd2', title: 'd2', changed: true }],
        }));
        const untouched = service.start(seed({
            changes: [{ objectId: 'd3', title: 'd3', changed: false, revisionId: 'r3' }],
        }));
        for (const op of [withRevision, without, untouched]) service.finish(op.id, 'applied');

        expect(service.canUndo(service.find(withRevision.id)!)).toBe(true);
        expect(service.canUndo(service.find(without.id)!)).toBe(false);
        expect(service.canUndo(service.find(untouched.id)!)).toBe(false);

        service.update(withRevision.id, { status: 'undone' });
        expect(service.canUndo(service.find(withRevision.id)!)).toBe(false);
    });

    it('survives a reload and stays scoped to the account that made it', () => {
        service.start(seed({ task: 'first account' }));

        email.set('b@local.test');
        service = create();
        expect(service.operations()).toEqual([]);
        service.start(seed({ task: 'second account' }));

        email.set('a@local.test');
        service = create();
        expect(service.operations().map(op => op.task)).toEqual(['first account']);
    });

    it('clear empties the log and the stored copy with it', () => {
        service.start(seed());
        service.clear();
        expect(service.operations()).toEqual([]);

        service = create();
        expect(service.operations()).toEqual([]);
    });

    it('keeps the newest first', () => {
        service.start(seed({ task: 'older' }));
        service.start(seed({ task: 'newer' }));
        expect(service.operations().map(op => op.task)).toEqual(['newer', 'older']);
    });
});
