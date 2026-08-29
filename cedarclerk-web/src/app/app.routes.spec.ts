import { runInInjectionContext, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Route, Router } from '@angular/router';
import { routes } from './app.routes';

// The old form was the string 'posts?tab=stats', which the router reads as one path segment: the
// redirect resolved and the tab silently did not (ADR-148). Asserting the parsed query is the only
// thing that tells the two apart — the path is '/posts' either way.
describe('app routes', () => {
    function find(path: string): Route {
        const shell = routes.find(r => Array.isArray(r.children) && r.children.some(c => c.path === path));
        const route = shell?.children?.find(c => c.path === path);
        if (!route) throw new Error(`no route for '${path}'`);
        return route;
    }

    it('sends an old /stats bookmark to the manager with its tab still set', () => {
        TestBed.configureTestingModule({ providers: [provideRouter([])] });
        const redirect = find('stats').redirectTo;
        expect(typeof redirect).toBe('function');

        const injector = TestBed.inject(Injector);
        const tree = runInInjectionContext(injector, () => (redirect as any)({}));
        const router = TestBed.inject(Router);
        const parsed = router.parseUrl(router.serializeUrl(tree));

        expect(parsed.root.children['primary'].segments.map(s => s.path)).toEqual(['posts']);
        expect(parsed.queryParams['tab']).toBe('stats');
    });

    // The desktop-app page answers a visitor and a signed-in user alike, so it stands beside the
    // legal pages rather than inside the shell, whose ground demands a session.
    it('keeps /download public and outside the shell', () => {
        const route = routes.find(r => r.path === 'download');
        expect(route).toBeTruthy();
        expect(route!.canActivate).toBeUndefined();
        const shell = routes.find(r => Array.isArray(r.children));
        expect(shell!.children!.some(c => c.path === 'download')).toBe(false);
    });
});
