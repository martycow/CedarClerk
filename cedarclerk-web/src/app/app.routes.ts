import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';
import { LoginComponent } from './pages/login.component';
import { RegisterComponent } from './pages/register.component';
import { EditorComponent } from './pages/editor.component';
import { DraftsPageComponent } from './pages/drafts.component';
import { SettingsComponent } from './pages/settings.component';
import { PostsManagerComponent } from './pages/posts-manager.component';
import { TermsComponent } from './pages/terms.component';
import { PrivacyComponent } from './pages/privacy.component';
import { AdminComponent } from './pages/admin.component';
import { GlossaryComponent } from './pages/glossary.component';

export const routes: Routes = [
    { path: 'login', component: LoginComponent },
    { path: 'register', component: RegisterComponent },
    { path: 'terms', component: TermsComponent },
    { path: 'privacy', component: PrivacyComponent },
    { path: 'editor', component: EditorComponent, canActivate: [authGuard] },
    { path: 'drafts', component: DraftsPageComponent, canActivate: [authGuard] },
    { path: 'settings', component: SettingsComponent, canActivate: [authGuard] },
    { path: 'posts', component: PostsManagerComponent, canActivate: [authGuard] },
    { path: 'glossary', component: GlossaryComponent, canActivate: [authGuard] },
    // adminGuard already covers signed-in — it redirects to /login itself (IF2).
    { path: 'admin', component: AdminComponent, canActivate: [adminGuard] },
    // N7 folded both of these into the Posts Manager; the old paths stay as redirects because
    // they're what any existing bookmark points at.
    { path: 'comments', redirectTo: 'posts' },
    { path: 'stats', redirectTo: 'posts' },
    // Drafts, not the editor, is the landing screen — you pick what to work on first.
    { path: '', pathMatch: 'full', redirectTo: 'drafts' },
    { path: '**', redirectTo: 'drafts' },
];