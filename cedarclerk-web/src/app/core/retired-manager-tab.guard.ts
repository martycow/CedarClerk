import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

const TAB_PAGES: Record<string, string> = { stats: '/metrics', forms: '/forms' };

// ADR-316 — Metrics and Forms are pages of their own; a bookmark that still names one as a tab of
// /posts lands on its page with the rest of its query intact.
export const retiredManagerTabGuard: CanActivateFn = route => {
    const target = TAB_PAGES[route.queryParamMap.get('tab') ?? ''];
    if (!target) return true;
    const { tab: _tab, ...queryParams } = route.queryParams;
    return inject(Router).createUrlTree([target], { queryParams });
};
