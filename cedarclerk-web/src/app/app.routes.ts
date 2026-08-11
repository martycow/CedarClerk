import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
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
    { path: 'terms', loadComponent: () => import('./pages/terms.component').then(m => m.TermsComponent) },
    { path: 'privacy', loadComponent: () => import('./pages/privacy.component').then(m => m.PrivacyComponent) },
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
    {
        path: 'glossary',
        loadComponent: () => import('./pages/glossary.component').then(m => m.GlossaryComponent),
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
    // T-078 / T-080 — the design-system reference and the icon inventory (ADR-071/072). Behind
    // authGuard rather than open: development surfaces, with no reason to be part of the public
    // site.
    {
        path: 'dev/styleguide',
        loadComponent: () => import('./pages/styleguide.component').then(m => m.StyleguideComponent),
        canActivate: [authGuard],
    },
    {
        path: 'dev/icons',
        loadComponent: () => import('./pages/icons.component').then(m => m.IconsComponent),
        canActivate: [authGuard],
    },
    // N7 folded both of these into the Posts Manager; the old paths stay as redirects because
    // they're what any existing bookmark points at.
    { path: 'comments', redirectTo: 'posts' },
    { path: 'stats', redirectTo: 'posts' },
    // Drafts, not the editor, is the landing screen — you pick what to work on first.
    { path: '', pathMatch: 'full', redirectTo: 'drafts' },
    { path: '**', redirectTo: 'drafts' },
];
