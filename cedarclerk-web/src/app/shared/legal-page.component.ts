import { Component, inject, Input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ThemeService } from '../core/theme.service';
import { CedarLogoComponent } from './cedar-logo.component';

@Component({
    selector: 'app-legal-page',
    imports: [RouterLink, CedarLogoComponent],
    templateUrl: 'legal-page.component.html',
    styleUrls: ['legal-page.component.css']
})
export class LegalPageComponent {
    @Input({ required: true }) title!: string;
    @Input() updated = '';
    theme = inject(ThemeService);
}
