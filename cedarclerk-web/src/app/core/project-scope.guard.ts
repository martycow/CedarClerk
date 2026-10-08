import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { CurrentProjectService } from './current-project.service';

// ADR-322 — Documents, Assets, Calendar and Publishing exist only inside a project. A URL without
// one is completed from the session's open project, or sent to the Projects Hub to pick one.
// With the projects module off there is nothing to scope by, and the account-wide screens stay.
export const projectScopeGuard: CanActivateFn = async (route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const current = inject(CurrentProjectService);
    if (!auth.userEmail()) await auth.refresh();
    if (!auth.indieDev()) return true;

    const id = route.queryParamMap.get('project') || current.id();
    if (!id) return router.parseUrl('/projects');
    if (route.data['projectHome']) return router.createUrlTree(['/projects', id, route.data['projectHome']]);
    if (route.queryParamMap.get('project')) return true;

    const tree = router.parseUrl(state.url);
    tree.queryParams = { ...tree.queryParams, project: id };
    return tree;
};
