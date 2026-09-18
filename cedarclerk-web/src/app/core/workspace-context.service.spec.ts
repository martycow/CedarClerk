import { TestBed } from '@angular/core/testing';
import { WorkspaceContextService, WorkspaceObject } from './workspace-context.service';

const doc = (id: string): WorkspaceObject => ({ id, kind: 'document', title: id });

describe('workspace context scope', () => {
    let service: WorkspaceContextService;

    beforeEach(() => {
        TestBed.configureTestingModule({});
        service = TestBed.inject(WorkspaceContextService);
    });

    it('is empty until a page says otherwise', () => {
        expect(service.scope()).toEqual([]);
        expect(service.surface()).toBe('');
    });

    it('falls back to the open object when nothing is ticked', () => {
        service.set({ surface: 'Editor', open: doc('a') });
        expect(service.scope().map(o => o.id)).toEqual(['a']);
    });

    // The whole point of ADR-301 clause 5: an operation can never reach past the ticked set.
    it('is exactly the selection once something is ticked', () => {
        service.set({ surface: 'Drafts', open: doc('a'), selection: [doc('b'), doc('c')] });
        expect(service.scope().map(o => o.id)).toEqual(['b', 'c']);
    });

    it('set replaces the whole state, patch keeps the rest', () => {
        service.set({ surface: 'Drafts', open: doc('a'), properties: [{ label: 'Type', value: 'post' }] });
        service.patch({ selection: [doc('b')] });

        expect(service.open()?.id).toBe('a');
        expect(service.properties()).toHaveLength(1);

        service.set({ surface: 'Library' });
        expect(service.open()).toBeNull();
        expect(service.properties()).toEqual([]);
    });

    it('names the protected fields an AI operation must not rewrite', () => {
        service.set({
            surface: 'Editor',
            properties: [
                { label: 'Title', value: 'Hello' },
                { label: 'Slug', value: 'hello', protected: true },
                { label: 'Id', value: '42', protected: true },
            ],
        });

        expect(service.protectedFields()).toEqual(['Slug', 'Id']);
    });

    it('clear returns it to empty', () => {
        service.set({ surface: 'Editor', open: doc('a') });
        service.clear();
        expect(service.scope()).toEqual([]);
    });
});
