import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';

@Component({
    selector: 'app-register',
    imports: [FormsModule, RouterLink, CedarLogoComponent],
    templateUrl: 'register.component.html',
    styleUrls: ['register.component.css']
})
export class RegisterComponent {
    private auth = inject(AuthService);
    private router = inject(Router);
    theme = inject(ThemeService);

    email = '';
    password = '';
    inviteCode = '';
    busy = signal(false);
    error = signal('');

    async submit() {
        this.busy.set(true);
        this.error.set('');
        const result = await this.auth.register(this.email, this.password, this.inviteCode);
        this.busy.set(false);
        if (result.ok) {
            this.router.navigateByUrl('/editor');
        } else {
            this.error.set(result.error);
        }
    }
}
