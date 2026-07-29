import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { CommentsService } from '../core/comments.service';
import { ThemeService } from '../core/theme.service';
import { VersionService } from '../core/version.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CedarLogoComponent } from './cedar-logo.component';
import { AccountMenuComponent } from './account-menu.component';
import { CountBadgeComponent } from './count-badge.component';
import {
    LucideArrowLeft as ArrowLeft, LucideNewspaper as Newspaper, LucideBookMarked as BookMarked,
    LucideSettings as Settings, LucideShieldCheck as ShieldCheck,
} from '@lucide/angular';

export type PageHeaderPage = 'posts' | 'glossary' | 'settings' | 'admin' | 'drafts';

// Header/nav redesign (27.07.2026, docs/DECISIONS.md) — one glass header shared by the "secondary"
// screens, replacing near-identical header blocks that had already drifted (glass on Posts/Admin,
// solid fill on Glossary/Settings). The nav row mirrors the editor topbar's own nav buttons (I11)
// so every screen reaches every other in one click instead of through the account popover — which
// is also why /drafts (the post-login landing page, outside the design brief's 5-screen list but
// left with no other way to reach Posts/Glossary/Settings once the popover dropped those links)
// takes this header too, just with the back arrow hidden.
@Component({
    selector: 'app-page-header',
    imports: [
        RouterLink, CedarLogoComponent, AccountMenuComponent, CountBadgeComponent,
        ArrowLeft, Newspaper, BookMarked, Settings, ShieldCheck,
    ],
    templateUrl: 'page-header.component.html',
    styleUrls: ['page-header.component.css'],
})
export class PageHeaderComponent {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    feedback = inject(CommentsService);
    version = inject(VersionService);
    t = inject(LocaleService).t;

    page = input.required<PageHeaderPage>();

    // Only /drafts has nothing to go back to — it's the landing page itself.
    showBack = input(true);

    crumb = computed(() => {
        const t = this.t();
        switch (this.page()) {
            case 'posts': return t.manager.crumb;
            case 'glossary': return t.glossary.crumb;
            case 'settings': return t.settings.crumb;
            case 'admin': return t.admin.crumb;
            case 'drafts': return t.drafts.crumb;
        }
    });
}
