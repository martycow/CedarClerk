import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { LoginComponent } from './pages/login.component';
import { RegisterComponent } from './pages/register.component';
import { EditorComponent } from './pages/editor.component';
import { SettingsComponent } from './pages/settings.component';
import { CommentsComponent } from './pages/comments.component';
import { TermsComponent } from './pages/terms.component';
import { PrivacyComponent } from './pages/privacy.component';

export const routes: Routes = [
    { path: 'login', component: LoginComponent },
    { path: 'register', component: RegisterComponent },
    { path: 'terms', component: TermsComponent },
    { path: 'privacy', component: PrivacyComponent },
    { path: 'editor', component: EditorComponent, canActivate: [authGuard] },
    { path: 'settings', component: SettingsComponent, canActivate: [authGuard] },
    { path: 'comments', component: CommentsComponent, canActivate: [authGuard] },
    { path: '', pathMatch: 'full', redirectTo: 'editor' },
    { path: '**', redirectTo: 'editor' },
];