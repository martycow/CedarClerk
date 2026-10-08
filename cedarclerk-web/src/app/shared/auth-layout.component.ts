import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/theme.service';
import { CedarLogoComponent } from './cedar-logo.component';
import { IconComponent } from './icon.component';
import { LangSwitchComponent } from './lang-switch.component';

@Component({
    selector: 'app-auth-layout',
    imports: [RouterLink, IconComponent, LangSwitchComponent, CedarLogoComponent],
    template: `
        <div class="door">
            <header class="door-header">
                <a href="/" class="brand-home" aria-label="Cedar Clerk">
                    <app-cedar-logo [size]="36" /><span>Cedar Clerk</span>
                </a>
                <div class="door-controls">
                    <app-lang-switch appearance="plain" />
                    <button class="theme-toggle" type="button" [attr.aria-label]="t().common.toggleTheme" (click)="theme.toggle()">
                        <app-icon [name]="theme.theme() === 'dark' ? 'sun' : 'moon'" size="sm" />
                    </button>
                </div>
            </header>
            <main class="auth-layout">
                <div class="auth-sheet">
                    <ng-content />
                </div>
            </main>
            <footer class="door-footer">
                <nav><a routerLink="/terms">{{ t().authLayout.legalTerms }}</a><span aria-hidden="true">·</span><a routerLink="/privacy">{{ t().authLayout.legalPrivacy }}</a></nav>
            </footer>
        </div>
    `,
    styles: [`
        :host { display: block; font-family: var(--font-sans); }
        .door { position: relative; isolation: isolate; min-height: 100dvh; box-sizing: border-box; display: flex; flex-direction: column; background: var(--canvas); color: var(--wood-ink); }
        .door-header { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); padding: var(--space-5) var(--space-8); }
        .brand-home { display: flex; align-items: center; gap: var(--space-3); color: inherit; font-size: var(--fs-title); font-weight: 600; text-decoration: none; }
        .door-controls { display: flex; align-items: center; gap: var(--space-3); }
        .theme-toggle { display: grid; place-items: center; width: var(--hit-touch); height: var(--hit-touch); padding: 0; border: 0; border-left: 1px solid currentColor; background: transparent; color: inherit; cursor: pointer; }
        .auth-layout { width: 100%; box-sizing: border-box; display: grid; grid-template-columns: minmax(0, 1fr); place-items: center; flex: 1; padding: var(--space-5) var(--space-4); }
        .auth-sheet { position: relative; min-width: 0; width: min(100%, var(--auth-sheet-width)); box-sizing: border-box; padding: var(--space-10) var(--space-7); border: 1px solid var(--auth-border); border-radius: var(--radius-sm); background: var(--auth-sheet); color: var(--auth-ink); box-shadow: var(--shadow-lg);
            --fs-ui: var(--auth-body-size); --fs-meta: var(--auth-meta-size); --border-field: 1px solid var(--auth-border); --field-label-ink: var(--auth-soft-ink); --shadow-field-inset: none; --sheet: var(--auth-sheet); --surface: var(--auth-field); --paper-bright: var(--auth-field); --text: var(--auth-ink); --t2: var(--auth-soft-ink); --ink-3: var(--auth-soft-ink); --border: var(--auth-border); --border-strong: var(--auth-border); --paper-edge: var(--auth-border); --accent: var(--auth-pine); --pine: var(--auth-pine); --pine-deep: var(--auth-pine-hover); --text-on-pine: var(--auth-forest-ink); --field-label-transform: none; --field-label-spacing: normal; --hit-target: var(--hit-touch); }
        .door-footer { text-align: center; padding: var(--space-3) var(--space-4) var(--space-8); }
        .door-footer::before { content: ''; display: block; width: var(--space-8); border-top: 1px solid currentColor; margin: 0 auto var(--space-5); opacity: .65; }
        nav { display: flex; flex-wrap: wrap; justify-content: center; align-items: center; gap: var(--space-3); font-size: var(--auth-meta-size); }
        nav a { color: inherit; text-decoration: none; padding: var(--space-2) 0; }
        nav a:hover { text-decoration: underline; }
        @media (max-width: 600px) {
            .door-header { padding: var(--space-3) var(--space-4); gap: var(--space-2); }
            .brand-home { width: 160px; }
            .brand-home img { height: 48px; }
            .door-controls { gap: 0; }
            .brand-home { width: min(160px, calc(100vw - 176px)); }
            .auth-layout { padding: var(--space-3); }
            .auth-sheet { --auth-title-size: var(--fs-27); padding: var(--space-7) var(--space-5); }
            .door-footer { padding-bottom: var(--space-5); }
        }
    `],
})
export class AuthLayoutComponent {
    readonly t = inject(LocaleService).t;
    readonly theme = inject(ThemeService);
}
