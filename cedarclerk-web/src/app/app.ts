import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  // Injected for its constructor alone: ThemeService stamps data-theme on <html> as it is built,
  // and nothing else in the root reads it.
  private theme = inject(ThemeService);
  protected readonly title = signal('cedarclerk-web');
}
