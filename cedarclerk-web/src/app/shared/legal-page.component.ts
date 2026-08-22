import { Component, inject, Input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ThemeService } from '../core/theme.service';
import { CedarLogoComponent } from './cedar-logo.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';
import { IconComponent } from './icon.component';

@Component({
    selector: 'app-legal-page',
    imports: [RouterLink, CedarLogoComponent, ButtonComponent, PaperCardComponent, IconComponent],
    templateUrl: 'legal-page.component.html',
    styleUrls: ['legal-page.component.css']
})
export class LegalPageComponent {
    @Input({ required: true }) title!: string;
    @Input() updated = '';
    theme = inject(ThemeService);
}
