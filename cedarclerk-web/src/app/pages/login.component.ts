import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
    selector: 'app-login',
    imports: [FormsModule],
    templateUrl: 'login.component.html',
    styleUrls: ['login.component.css']
})
export class LoginComponent {
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
        const ok = await this.auth.login(this.email, this.password);
        this.busy.set(false);
        ok ? this.router.navigateByUrl('/editor')
            : this.error.set('Invalid email or password');
    }

  async register() {
    this.busy.set(true);
    this.error.set('');
    const ok = await this.auth.register(this.email, this.password, this.inviteCode);
    this.busy.set(false);
    ok ? this.router.navigateByUrl('/editor')
        : this.error.set('Registration failed');
  }
}