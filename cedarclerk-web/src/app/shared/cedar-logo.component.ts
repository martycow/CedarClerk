import { Component, input } from '@angular/core';

@Component({
    selector: 'app-cedar-logo',
    template: `
        <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24">
            <polygon points="12,2 19,11 5,11" [attr.fill]="fill()"></polygon>
            <polygon points="12,7 21,18 3,18" [attr.fill]="fill()" opacity="0.75"></polygon>
            <rect x="10.6" y="18" width="2.8" height="4" rx="1" [attr.fill]="fill()" opacity="0.9"></rect>
        </svg>
    `,
})
export class CedarLogoComponent {
    size = input(20);
    fill = input('var(--accent)');
}
