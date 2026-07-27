import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Keeps a non-admin from landing on /admin by typing the URL. Convenience only — the page would
// show nothing anyway, because every endpoint it calls is gated server-side (IF2). Redirects to
// the editor rather than to /login: the user IS signed in, just not an admin.
export const adminGuard: CanActivateFn = async () => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.userEmail()) await auth.refresh();
    if (!auth.userEmail()) return router.parseUrl('/login');
    return auth.isAdmin() ? true : router.parseUrl('/editor');
};
