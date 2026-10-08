import { runInInjectionContext, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Route, Router } from '@angular/router';
import { routes } from './app.routes';
import { projectScopeGuard } from './core/project-scope.guard';
import { retiredManagerTabGuard } from './core/retired-manager-tab.guard';

describe('app routes', () => {
    function find(path: string): Route {
        const shell = routes.find(r => Array.isArray(r.children) && r.children.some(c => c.path === path));
        const route = shell?.children?.find(c => c.path === path);
        if (!route) throw new Error(`no route for '${path}'`);
        return route;
    }

    // ADR-316 — the redirect is a function so the bookmark's own query reaches the page with it.
    it('sends an old /stats bookmark to /metrics with its query intact', () => {
        TestBed.configureTestingModule({ providers: [provideRouter([])] });
        const redirect = find('stats').redirectTo;
        expect(typeof redirect).toBe('function');

        const injector = TestBed.inject(Injector);
        const tree = runInInjectionContext(injector, () => (redirect as any)({ queryParams: { statsDays: '30' } }));
        const router = TestBed.inject(Router);
        const parsed = router.parseUrl(router.serializeUrl(tree));

        expect(parsed.root.children['primary'].segments.map(s => s.path)).toEqual(['metrics']);
        expect(parsed.queryParams['statsDays']).toBe('30');
    });

    it('gives Metrics and Forms their own project-scoped routes and takes the retired tabs off /posts', () => {
        expect(find('metrics').canActivate).toContain(projectScopeGuard);
        expect(find('forms').canActivate).toContain(projectScopeGuard);
        expect(find('posts').canActivate).toContain(retiredManagerTabGuard);
        expect(find('posts').canActivate!.indexOf(retiredManagerTabGuard))
            .toBeLessThan(find('posts').canActivate!.indexOf(projectScopeGuard));
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
