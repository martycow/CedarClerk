import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
    selector: 'app-register',
    imports: [FormsModule, RouterLink],
    templateUrl: 'register.component.html',
    styleUrls: ['register.component.css']
})
export class RegisterComponent {
    private auth = inject(AuthService);
    private router = inject(Router);

    email = '';
    password = '';
    inviteCode = '';
    busy = signal(false);
    error = signal('');

    async submit() {
        this.busy.set(true);
        this.error.set('');
        const ok = await this.auth.register(this.email, this.password, this.inviteCode);
        this.busy.set(false);
        ok ? this.router.navigateByUrl('/editor')
            : this.error.set('Registration failed');
    }
}
