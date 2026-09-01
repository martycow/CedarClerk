import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core/theme.service';
import { AnalyticsService } from './core/analytics.service';
import { ConsentBannerComponent } from './shared/consent-banner.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ConsentBannerComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  // Injected for its constructor alone: ThemeService stamps data-theme on <html> as it is built,
  // and nothing else in the root reads it.
  private theme = inject(ThemeService);
  private analytics = inject(AnalyticsService);
  protected readonly title = signal('cedarclerk-web');

  constructor() {
    // Loads the provider only if consent was already given on an earlier visit; a first-time
    // visitor gets the banner and nothing else runs until they answer it.
    void this.analytics.enableIfConsented();
  }
}
