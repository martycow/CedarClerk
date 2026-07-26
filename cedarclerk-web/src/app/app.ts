import { Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { ThemeService } from './core/theme.service';
import { DebugConsoleComponent } from './shared/debug-console.component';

// Routes reachable without logging in. The debug console is an owner tool (it shows this
// account's own API traffic), so it has no business rendering over a login/register form.
const PUBLIC_ROUTES = ['/login', '/register', '/terms', '/privacy'];

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, DebugConsoleComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private theme = inject(ThemeService);
  private router = inject(Router);
  protected readonly title = signal('cedarclerk-web');

  private currentUrl = signal(this.router.url);
  protected showDebugConsole = computed(() => {
    const path = this.currentUrl().split('?')[0];
    return !PUBLIC_ROUTES.some(r => path === r || path.startsWith(r + '/'));
  });

  constructor() {
    this.router.events.subscribe(e => {
      if (e instanceof NavigationEnd) this.currentUrl.set(e.urlAfterRedirects);
    });
  }
}
