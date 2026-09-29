import { Component, input } from '@angular/core';

@Component({
    selector: 'app-cedar-logo',
    styles: [`img { display: block; } .dark { display: none; } :host-context([data-theme="dark"]) .light { display: none; } :host-context([data-theme="dark"]) .dark { display: block; }`],
    template: `
        <img class="light" src="/assets/brand/cedar-clerk-mark.svg" alt="" [width]="size()" [height]="size()" />
        <img class="dark" src="/assets/brand/cedar-clerk-mark-dark.svg" alt="" [width]="size()" [height]="size()" />
    `,
})
export class CedarLogoComponent {
    size = input(20);
    fill = input('var(--accent)');
}
