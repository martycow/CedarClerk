import { ActivatedRouteSnapshot } from '@angular/router';
import { skipViewTransition } from './app.config';
import { routes } from './app.routes';

// ADR-287 — the router's cross-fade is skipped on either side of the editor and under reduced
// motion. The snapshots are built by hand: only `routeConfig` and `firstChild` are read.
function chain(...paths: string[]): ActivatedRouteSnapshot {
    const shell = routes.find(r => Array.isArray(r.children))!;
    let node: any = null;
    for (const path of [...paths].reverse()) {
        const config = path === '' ? shell : shell.children!.find(c => c.path === path)!;
        node = { routeConfig: config, firstChild: node };
    }
    return { routeConfig: null, firstChild: node } as ActivatedRouteSnapshot;
}

describe('view transitions', () => {
    it('the editor route carries the opt-out flag', () => {
        const shell = routes.find(r => Array.isArray(r.children))!;
        expect(shell.children!.find(c => c.path === 'editor')!.data).toEqual({ viewTransition: false });
    });

    it('runs between ordinary screens', () => {
        expect(skipViewTransition(chain('', 'drafts'), chain('', 'posts'), false)).toBe(false);
    });

    it('is skipped entering the editor, leaving it, and under reduced motion', () => {
        expect(skipViewTransition(chain('', 'drafts'), chain('', 'editor'), false)).toBe(true);
        expect(skipViewTransition(chain('', 'editor'), chain('', 'drafts'), false)).toBe(true);
        expect(skipViewTransition(chain('', 'drafts'), chain('', 'posts'), true)).toBe(true);
    });
});
