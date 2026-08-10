import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Phase 13 / ADR-101 — keeps /projects from being reachable by typing the URL when the module is
// off. Convenience only, exactly like adminGuard: with the flag off the server never maps
// /api/projects at all, so the screen would have nothing to show either way.
//
// Redirects to /drafts rather than /login — the user is signed in, the feature just isn't here.
export const indieDevGuard: CanActivateFn = async () => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.userEmail()) await auth.refresh();
    if (!auth.userEmail()) return router.parseUrl('/login');
    return auth.indieDev() ? true : router.parseUrl('/drafts');
};
