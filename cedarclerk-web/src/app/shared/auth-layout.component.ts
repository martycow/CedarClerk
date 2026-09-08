import { Component, inject } from '@angular/core';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/theme.service';
import { ButtonComponent } from '../bench/forms/button.component';
import { IconComponent } from './icon.component';

@Component({
    selector: 'app-auth-layout',
    imports: [ButtonComponent, IconComponent],
    template: `
        <div class="door">
            <app-button class="theme-toggle" variant="paper" [title]="t().common.toggleTheme" (clicked)="theme.toggle()">
                <app-icon [name]="theme.theme() === 'dark' ? 'sun' : 'moon'" size="sm" />
            </app-button>
            <main class="auth-layout">
                <aside class="brand-panel">
                    <a href="/" class="brand-home" aria-label="Cedar Clerk">
                        <img src="/assets/brand/cedar-clerk-horizontal.svg" alt="Cedar Clerk" width="320" height="116" />
                    </a>
                    <div class="brand-story">
                        <p class="eyebrow">{{ t().authLayout.eyebrow }}</p>
                        <h2>{{ t().authLayout.title }}</h2>
                        <p class="description">{{ t().authLayout.description }}</p>
                    </div>
                    <div class="brand-footnote"><span class="brand-rule"></span>{{ t().login.tagline }}</div>
                </aside>
                <ng-content />
            </main>
        </div>
    `,
    styles: [`
        :host { display: block; min-height: 100dvh; background: var(--canvas); font-family: var(--font-sans); }
        .door { min-height: 100dvh; box-sizing: border-box; display: grid; place-items: center; padding: calc(var(--space-8) * 2) var(--space-5); background-image: var(--tex-wood); }
        .theme-toggle { position: absolute; top: var(--space-4); right: var(--space-4); }
        .auth-layout { width: min(100%, 960px); display: grid; grid-template-columns: 1fr 1fr; align-items: stretch; box-shadow: var(--shadow-lg); border: 1px solid var(--border); border-radius: var(--radius-lg); overflow: hidden; }
        .brand-panel { min-width: 0; padding: var(--space-8); display: flex; flex-direction: column; gap: var(--space-8); background: var(--surface); color: var(--text); border-right: 1px solid var(--border); }
        .brand-home { display: block; }
        img { display: block; width: 100%; max-width: 320px; height: auto; }
        .brand-story { margin: auto 0; }
        .eyebrow { font-size: var(--fs-meta); letter-spacing: .1em; text-transform: uppercase; color: var(--t2); font-weight: 700; }
        h2 { margin: var(--space-4) 0; font-family: var(--font-display); font-size: var(--fs-27); line-height: 1.25; text-wrap: balance; }
        .description { font-size: var(--fs-body); line-height: 1.65; color: var(--t2); }
        .brand-footnote { font-size: var(--fs-meta); color: var(--t2); line-height: 1.5; }
        .brand-rule { display: block; width: var(--space-8); border-top: 3px solid var(--accent); margin-bottom: var(--space-4); }
        @media (max-width: 720px) {
            .door { padding: calc(var(--space-8) + var(--space-4)) var(--space-3) var(--space-5); }
            .auth-layout { max-width: 480px; grid-template-columns: 1fr; }
            .brand-panel { padding: var(--space-5) var(--space-6); border-right: 0; border-bottom: 1px solid var(--border); }
            img { max-width: 200px; margin: auto; }
            .brand-story, .brand-footnote { display: none; }
        }
    `],
})
export class AuthLayoutComponent {
    readonly t = inject(LocaleService).t;
    readonly theme = inject(ThemeService);
}
