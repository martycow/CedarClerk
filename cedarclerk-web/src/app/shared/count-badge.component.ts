import { Component, Input } from '@angular/core';

// Small round attention badge (N3). A shared component rather than a CSS class copied into every
// page, because unlike the page-chrome classes this app repeats, a badge has real behaviour worth
// keeping in one place: it renders nothing at zero, and caps at 99+ so a runaway count can't
// stretch the control it sits on.
@Component({
    selector: 'app-count-badge',
    template: `@if (count > 0) { <span class="count-badge" [title]="title">{{ label }}</span> }`,
    styles: [`
        .count-badge {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 17px;
            height: 17px;
            padding: 0 5px;
            border-radius: 999px;
            background: var(--accent);
            color: #F4F2EA;
            font-size: 10.5px;
            font-weight: 700;
            line-height: 1;
            flex: none;
        }
    `],
})
export class CountBadgeComponent {
    @Input() count = 0;
    @Input() title = '';

    get label(): string {
        return this.count > 99 ? '99+' : `${this.count}`;
    }
}
