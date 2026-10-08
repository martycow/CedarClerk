import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { ThemeMenuComponent } from '../shared/theme-menu.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { IconComponent } from '../shared/icon.component';

// The desktop-app page. Outside the shell like the legal pages (ADR-166): a visitor reaches it
// from the landing before there is a session, a signed-in user from the Assets empty state, so it
// declares its own surface and carries its own theme toggle. The button is a full-page href — the
// server's GET /downloads/latest redirect, not an SPA route.
@Component({
    selector: 'app-download',
    imports: [ThemeMenuComponent, RouterLink, ButtonComponent, CedarLogoComponent, IconComponent],
    templateUrl: 'download.component.html',
    styleUrl: 'download.component.css',
})
export class DownloadComponent {
    protected readonly t = inject(LocaleService).t;
}
