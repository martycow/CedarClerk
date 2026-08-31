import { Router, Routes } from '@angular/router';
import { inject } from '@angular/core';
import { authGuard, onboardingGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';
import { guestGuard } from './core/guest.guard';
import { indieDevGuard } from './core/indiedev.guard';

// T-092 — every route is lazy (`loadComponent`), and the router preloads them all in the
// background once the app has booted (see app.config.ts). The measurement behind that: with the
// pages imported eagerly, the initial bundle was 1.87 MB raw, and the single largest contributor
// was the editor — TipTap, ProseMirror and KaTeX — loaded in full before /drafts, the landing
// screen, could paint. Preloading is what keeps the split from costing anything: opening the
// editor still finds its chunk in cache, because the fetch started while the drafts list rendered.
export const routes: Routes = [
    // guestGuard, not none: a live session means you are already past these two pages.
    {
        path: 'login',
        loadComponent: () => import('./pages/login.component').then(m => m.LoginComponent),
        canActivate: [guestGuard],
    },
    {
        path: 'register',
        loadComponent: () => import('./pages/register.component').then(m => m.RegisterComponent),
        canActivate: [guestGuard],
    },
    // T-328 — the mandatory first stop after registration, outside the shell like the other doors.
    {
        path: 'onboarding',
        loadComponent: () => import('./pages/onboarding.component').then(m => m.OnboardingComponent),
        canActivate: [onboardingGuard],
    },
    { path: 'terms', loadComponent: () => import('./pages/terms.component').then(m => m.TermsComponent) },
    { path: 'privacy', loadComponent: () => import('./pages/privacy.component').then(m => m.PrivacyComponent) },
    // No guard: the desktop-app page has to answer a visitor and a signed-in user alike, and the
    // button on it points at the server's own GET /downloads/latest redirect.
    { path: 'download', loadComponent: () => import('./pages/download.component').then(m => m.DownloadComponent) },
    // ADR-139 clause 1 — one parent route, and the pre-auth pages are outside it by the shape of
    // the tree. The four above are the workshop door: the rail carries a project switcher, save
    // state and hooks into guarded screens, and every one of them is meaningless without a session.
    // /dev/* stays inside, because the styleguide's job is showing chrome components in chrome.
    {
        path: '',
        loadComponent: () => import('./bench/chrome/bench-shell.component').then(m => m.BenchShellComponent),
        children: [
            {
                path: 'editor',
                loadComponent: () => import('./pages/editor.component').then(m => m.EditorComponent),
                canActivate: [authGuard],
            },
            {
                path: 'drafts',
                loadComponent: () => import('./pages/drafts.component').then(m => m.DraftsPageComponent),
                canActivate: [authGuard],
            },
            {
                path: 'settings',
                loadComponent: () => import('./pages/settings.component').then(m => m.SettingsComponent),
                canActivate: [authGuard],
            },
            {
                path: 'posts',
                loadComponent: () => import('./pages/posts-manager.component').then(m => m.PostsManagerComponent),
                canActivate: [authGuard],
            },
            // Wave 2 item 9 — the content calendar: scheduled sends and queue slots as a month
            // board, between documents and posts on the rail.
            {
                path: 'calendar',
                loadComponent: () => import('./pages/calendar.component').then(m => m.CalendarComponent),
                canActivate: [authGuard],
            },
            {
                path: 'glossary',
                loadComponent: () => import('./pages/glossary.component').then(m => m.GlossaryComponent),
                canActivate: [authGuard],
            },
            {
                // 'library', not 'media' — /media/* is the uploaded files' own URL space (server static
                // route + dev proxy), and the dev proxy forwards the whole prefix to the backend.
                path: 'library',
                loadComponent: () => import('./pages/media-library.component').then(m => m.MediaLibraryComponent),
                canActivate: [authGuard],
            },
            // Phase 13 — the indie-gamedev module (ADR-101). indieDevGuard already covers signed-in, the
            // same way adminGuard does; with the flag off it redirects to /drafts rather than 404-ing,
            // because the URL is not wrong, the feature is simply not installed here.
            {
                path: 'projects',
                loadComponent: () => import('./pages/projects.component').then(m => m.ProjectsComponent),
                canActivate: [indieDevGuard],
            },
            {
                path: 'projects/:id',
                loadComponent: () => import('./pages/project.component').then(m => m.ProjectComponent),
                canActivate: [indieDevGuard],
            },
            {
                path: 'projects/:id/assets',
                loadComponent: () => import('./pages/project-assets.component').then(m => m.ProjectAssetsComponent),
                canActivate: [indieDevGuard],
            },
            {
                path: 'projects/:id/planner',
                loadComponent: () => import('./pages/project-planner.component').then(m => m.ProjectPlannerComponent),
                canActivate: [indieDevGuard],
            },
            {
                path: 'projects/:id/builds',
                loadComponent: () => import('./pages/project-builds.component').then(m => m.ProjectBuildsComponent),
                canActivate: [indieDevGuard],
            },
            // T-301 — the board list, then one board. Two routes rather than a tab: a board is
            // addressable on its own, which is what a reference someone else opens has to be.
            {
                path: 'projects/:id/canvas',
                loadComponent: () => import('./pages/project-boards.component').then(m => m.ProjectBoardsComponent),
                canActivate: [indieDevGuard],
            },
            {
                path: 'projects/:id/canvas/:boardId',
                loadComponent: () => import('./pages/project-canvas.component').then(m => m.ProjectCanvasComponent),
                canActivate: [indieDevGuard],
            },
            // The Yarn dialogue tool: the script list, then one script's node graph — the same
            // two-route shape as canvas, and for the same addressability reason.
            {
                path: 'projects/:id/dialogues',
                loadComponent: () => import('./pages/project-dialogues.component').then(m => m.ProjectDialoguesComponent),
                canActivate: [indieDevGuard],
            },
            {
                path: 'projects/:id/dialogues/:scriptId',
                loadComponent: () => import('./pages/project-dialogue.component').then(m => m.ProjectDialogueComponent),
                canActivate: [indieDevGuard],
            },
            // authGuard, not indieDevGuard: an invitation has to survive an install with the module
            // off and still land the reader somewhere honest.
            {
                path: 'invite/:token',
                loadComponent: () => import('./pages/invite-accept.component').then(m => m.InviteAcceptComponent),
                canActivate: [authGuard],
            },
            {
                path: 'projects/:id/showcase',
                loadComponent: () => import('./pages/project-showcase.component').then(m => m.ProjectShowcaseComponent),
                canActivate: [indieDevGuard],
            },
            {
                path: 'projects/:id/tasks',
                loadComponent: () => import('./pages/project-tasks.component').then(m => m.ProjectTasksComponent),
                canActivate: [indieDevGuard],
            },
            // adminGuard already covers signed-in — it redirects to /login itself (IF2).
            {
                path: 'admin',
                loadComponent: () => import('./pages/admin.component').then(m => m.AdminComponent),
                canActivate: [adminGuard],
            },
            // T-078 / T-080 — the design-system reference and the icon inventory (ADR-071/072).
            // Behind adminGuard (T-339): development surfaces, no reason for a user to see them.
            {
                path: 'dev/styleguide',
                loadComponent: () => import('./pages/styleguide.component').then(m => m.StyleguideComponent),
                canActivate: [adminGuard],
            },
            {
                path: 'dev/icons',
                loadComponent: () => import('./pages/icons.component').then(m => m.IconsComponent),
                canActivate: [adminGuard],
            },
            // N7 folded both of these into the Posts Manager; the old paths stay as redirects because
            // they're what any existing bookmark points at.
            { path: 'comments', redirectTo: 'posts' },
            // A redirectTo *string* is a path, so the query would become part of a segment and the
            // tab would be dropped — an old metrics bookmark landing on the posts list. Only a
            // UrlTree carries ?tab=, which is what the manager reads on entry (ADR-148).
            { path: 'stats', redirectTo: () => inject(Router).parseUrl('/posts?tab=stats') },
            // The hub is the landing screen: it is the one page that names the project everything
            // else hangs off. With the module off, indieDevGuard turns this into /drafts, so the
            // two builds land on the only screen each of them has.
            { path: '', pathMatch: 'full', redirectTo: 'projects' },
            { path: '**', redirectTo: 'projects' },
        ],
    },
];
