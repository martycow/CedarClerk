import { mergeCanvasItems, sortCanvasItems } from './canvas-hub.service';
import { CanvasItem } from './boards.service';

function item(part: Partial<CanvasItem> & { id: string }): CanvasItem {
    return {
        boardId: 'b1', kind: 'note', x: 0, y: 0, width: 220, height: 180, rotation: 0, z: 1,
        color: '', payload: { text: '', align: 'left' },
        createdBy: 'u1', createdAt: '2026-08-27T10:00:00Z',
        updatedBy: 'u1', updatedAt: '2026-08-27T10:00:00Z', version: 1,
        ...part,
    };
}

describe('sortCanvasItems', () => {
    it('orders by z, then by when it was made, then by id', () => {
        const order = sortCanvasItems([
            item({ id: 'c', z: 2 }),
            item({ id: 'b', z: 1, createdAt: '2026-08-27T11:00:00Z' }),
            item({ id: 'a', z: 1, createdAt: '2026-08-27T10:00:00Z' }),
        ]).map(i => i.id);

        expect(order).toEqual(['a', 'b', 'c']);
    });

    it('is total, so a shared z still draws the same way in every client', () => {
        const tied = [item({ id: 'z9', z: 3 }), item({ id: 'a1', z: 3 })];

        expect(sortCanvasItems(tied).map(i => i.id)).toEqual(['a1', 'z9']);
        expect(sortCanvasItems([...tied].reverse()).map(i => i.id)).toEqual(['a1', 'z9']);
    });

    it('leaves the array it was handed alone', () => {
        const given = [item({ id: 'b', z: 2 }), item({ id: 'a', z: 1 })];
        sortCanvasItems(given);
        expect(given.map(i => i.id)).toEqual(['b', 'a']);
    });
});

describe('mergeCanvasItems', () => {
    it('takes an echo that is newer than what is held', () => {
        const held = [item({ id: 'a', x: 0, version: 1 })];
        const merged = mergeCanvasItems(held, [item({ id: 'a', x: 90, version: 2 })]);

        expect(merged[0].x).toBe(90);
        expect(merged[0].version).toBe(2);
    });

    it('discards an echo older than what is held', () => {
        // Out-of-order delivery, or our own optimistic row overtaken by a newer answer. Dropping it
        // is the whole reason the server's version is on the wire.
        const held = [item({ id: 'a', x: 90, version: 5 })];
        const merged = mergeCanvasItems(held, [item({ id: 'a', x: 0, version: 3 })]);

        expect(merged[0].x).toBe(90);
    });

    it('takes an echo of the same version, because that is the authoritative row', () => {
        const held = [item({ id: 'a', x: 0, version: 1 })];
        const merged = mergeCanvasItems(held, [item({ id: 'a', x: 12, version: 1 })]);

        expect(merged[0].x).toBe(12);
    });

    it('adds an item it has never seen', () => {
        const merged = mergeCanvasItems([item({ id: 'a' })], [item({ id: 'b', z: 2 })]);
        expect(merged.map(i => i.id)).toEqual(['a', 'b']);
    });

    it('comes back in render order whatever order the echo arrived in', () => {
        const merged = mergeCanvasItems(
            [item({ id: 'a', z: 3 })],
            [item({ id: 'c', z: 1 }), item({ id: 'b', z: 2 })]);

        expect(merged.map(i => i.id)).toEqual(['c', 'b', 'a']);
    });
});
