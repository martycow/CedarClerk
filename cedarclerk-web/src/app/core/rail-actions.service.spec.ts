import { TestBed } from '@angular/core/testing';
import { RailActionsService } from './rail-actions.service';
import { STRIP_GROUP_IDS, TOOLBAR_GROUPS } from './toolbar-layout';

describe('RailActionsService', () => {
    function svc() {
        const rail = TestBed.inject(RailActionsService);
        rail.clear();
        return rail;
    }

    it('reads as empty until a screen contributes something', () => {
        const rail = svc();
        expect(rail.save()).toBeNull();
        expect(rail.primary()).toBeNull();
    });

    it('replaces the whole contribution rather than adding to it', () => {
        const rail = svc();
        rail.publish({
            save: { state: 'set', hint: 'synced' },
            primary: { label: 'Export', icon: 'paper-plane-tilt', run: () => undefined },
        });
        rail.publish({ save: { state: 'forming', hint: 'saving' } });

        expect(rail.save()!.state).toBe('forming');
        expect(rail.primary()).toBeNull();
    });

    it('clears both halves, so a screen cannot leave its button on the next one', () => {
        const rail = svc();
        rail.publish({
            save: { state: 'set', hint: 'synced' },
            primary: { label: 'Export', icon: 'paper-plane-tilt', run: () => undefined },
        });
        rail.clear();
        expect(rail.save()).toBeNull();
        expect(rail.primary()).toBeNull();
    });

    it('runs the handler the publisher gave it, untouched', () => {
        const rail = svc();
        let ran = 0;
        rail.publish({ primary: { label: 'Export', icon: 'paper-plane-tilt', run: () => { ran++; } } });
        rail.primary()!.run();
        expect(ran).toBe(1);
    });
});

// ADR-150 deleted the stored layout; what is left of core/toolbar-layout.ts is a catalogue, and the
// strip renders from it. A group added there has to reach the strip, which is what this pins.
describe('the tool strip catalogue', () => {
    it('offers every group but AI, which is pinned outside the fit', () => {
        expect(STRIP_GROUP_IDS).toEqual(['text', 'insert', 'lists', 'code', 'media', 'blocks', 'feedback']);
        expect(TOOLBAR_GROUPS.map(g => g.id)).toContain('ai');
        expect(STRIP_GROUP_IDS).not.toContain('ai');
    });

    it('holds no preference of any kind — no preset, no hidden list, no stored rows', () => {
        const source = Object.keys(TOOLBAR_GROUPS[0]);
        expect(source).toEqual(['id', 'label', 'buttons']);
    });
});
