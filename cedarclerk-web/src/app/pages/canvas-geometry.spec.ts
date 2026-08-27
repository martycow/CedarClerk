import { resized, zoomAt } from './project-canvas.component';
import { CanvasGeometry } from '../core/boards.service';

const view = (x: number, y: number, z: number) => ({ x, y, z });

// The pure half of the surface. The component's pointer handlers are DOM-and-timing; these two
// functions are the arithmetic under them, and getting either subtly wrong shows up as a board
// that drifts under the cursor rather than as an error.
describe('zoomAt', () => {
    it('keeps the point under the cursor where it was', () => {
        const at = { x: 400, y: 300 };
        const before = view(0, 0, 1);
        const after = zoomAt(before, 2, at);

        const world = (v: { x: number; y: number; z: number }) => ({
            x: (at.x - v.x) / v.z, y: (at.y - v.y) / v.z,
        });

        expect(world(after).x).toBeCloseTo(world(before).x, 6);
        expect(world(after).y).toBeCloseTo(world(before).y, 6);
    });

    it('round-trips a zoom in and back out', () => {
        const at = { x: 137, y: 421 };
        const there = zoomAt(view(-50, 20, 1), 1.5, at);
        const back = zoomAt(there, 1 / 1.5, at);

        expect(back.x).toBeCloseTo(-50, 6);
        expect(back.y).toBeCloseTo(20, 6);
        expect(back.z).toBeCloseTo(1, 6);
    });

    it('clamps rather than letting a wheel run away', () => {
        expect(zoomAt(view(0, 0, 1), 1000, { x: 0, y: 0 }).z).toBe(4);
        expect(zoomAt(view(0, 0, 1), 0.0001, { x: 0, y: 0 }).z).toBeCloseTo(0.1, 6);
    });
});

describe('resized', () => {
    const from: CanvasGeometry = { id: 'a', x: 100, y: 100, width: 200, height: 200, rotation: 0 };

    it('grows to the south-east without moving the origin', () => {
        const next = resized(from, 'se', 40, 60);

        expect(next).toMatchObject({ x: 100, y: 100, width: 240, height: 260 });
    });

    it('moves the origin when the north-west corner is dragged', () => {
        const next = resized(from, 'nw', 40, 60);

        expect(next).toMatchObject({ x: 140, y: 160, width: 160, height: 140 });
        // The opposite corner is the one that must not move.
        expect(next.x + next.width).toBe(from.x + from.width);
        expect(next.y + next.height).toBe(from.y + from.height);
    });

    it('never inverts an item, however far the pointer goes', () => {
        const next = resized(from, 'se', -9000, -9000);

        expect(next.width).toBeGreaterThan(0);
        expect(next.height).toBeGreaterThan(0);
    });

    it('carries the id and the rotation through untouched', () => {
        const tilted: CanvasGeometry = { ...from, rotation: 12 };
        expect(resized(tilted, 'ne', 10, 10)).toMatchObject({ id: 'a', rotation: 12 });
    });
});
