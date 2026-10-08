import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { retiredManagerTabGuard } from './retired-manager-tab.guard';

describe('retired manager tab guard', () => {
    function run(queryParams: Record<string, string>) {
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: [provideRouter([])] });
        const route = { queryParams, queryParamMap: convertToParamMap(queryParams) } as unknown as ActivatedRouteSnapshot;
        const result = TestBed.runInInjectionContext(() => retiredManagerTabGuard(route, {} as RouterStateSnapshot));
        return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;
    }

    it('sends the stats tab to /metrics and the forms tab to /forms', () => {
        expect(run({ tab: 'stats' })).toBe('/metrics');
        expect(run({ tab: 'forms' })).toBe('/forms');
    });

    it('carries the rest of the query and drops the tab', () => {
        expect(run({ tab: 'stats', project: 'p1', statsDays: '30' })).toBe('/metrics?project=p1&statsDays=30');
    });

    it('lets the manager open for no tab or one it never had', () => {
        expect(run({})).toBe(true);
        expect(run({ tab: 'feedback', draft: 'd1' })).toBe(true);
    });
});
