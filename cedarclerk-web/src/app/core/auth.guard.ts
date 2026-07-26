import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { AppearanceService } from './appearance.service';
import { ToolbarLayoutService } from './toolbar-layout.service';

export const authGuard: CanActivateFn = async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const appearance = inject(AppearanceService);
    const toolbarLayout = inject(ToolbarLayoutService);

    if (auth.userEmail()) {
        appearance.loadFromAuth();
        toolbarLayout.loadFromAuth();
        return true;
    }
    await auth.refresh();
    if (!auth.userEmail()) return router.parseUrl('/login');
    appearance.loadFromAuth();
    toolbarLayout.loadFromAuth();
    return true;
};