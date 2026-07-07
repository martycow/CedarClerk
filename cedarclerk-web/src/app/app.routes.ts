import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { LoginComponent } from './pages/login.component';
import { RegisterComponent } from './pages/register.component';
import { EditorComponent } from './pages/editor.component';

export const routes: Routes = [
    { path: 'login', component: LoginComponent },
    { path: 'register', component: RegisterComponent },
    { path: 'editor', component: EditorComponent, canActivate: [authGuard] },
    { path: '', pathMatch: 'full', redirectTo: 'editor' },
    { path: '**', redirectTo: 'editor' },
];