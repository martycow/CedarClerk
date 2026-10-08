import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { projectScopeGuard } from './project-scope.guard';
import { AuthService } from './auth.service';
import { CurrentProjectService } from './current-project.service';

describe('project scope guard', () => {
    function run(url: string, opts: { indieDev?: boolean; current?: string; data?: Record<string, unknown> } = {}) {
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: { userEmail: signal('a@b.co'), indieDev: signal(opts.indieDev ?? true), refresh: async () => { } } },
                { provide: CurrentProjectService, useValue: { id: signal(opts.current ?? '') } },
            ],
        });
        const router = TestBed.inject(Router);
        const tree = router.parseUrl(url);
        const route = { queryParamMap: convertToParamMap(tree.queryParams), data: opts.data ?? {} } as unknown as ActivatedRouteSnapshot;
        return TestBed.runInInjectionContext(() =>
            projectScopeGuard(route, { url } as RouterStateSnapshot)) as Promise<boolean | UrlTree>;
    }
    const str = (r: boolean | UrlTree) => (typeof r === 'boolean' ? r : r.toString());

    it('lets every screen through with the projects module off', async () =>
        expect(await run('/calendar', { indieDev: false })).toBe(true));

    it('sends a screen with no project anywhere to the Projects Hub', async () =>
        expect(str(await run('/calendar'))).toBe('/projects'));

    it('completes the URL from the open project', async () =>
        expect(str(await run('/posts?q=x', { current: 'p1' }))).toBe('/posts?q=x&project=p1'));

    it('keeps a URL that already names a project', async () =>
        expect(await run('/drafts?project=p2', { current: 'p1' })).toBe(true));

    it('sends the account library to the open project\'s assets', async () =>
        expect(str(await run('/library', { current: 'p1', data: { projectHome: 'assets' } }))).toBe('/projects/p1/assets'));
});
