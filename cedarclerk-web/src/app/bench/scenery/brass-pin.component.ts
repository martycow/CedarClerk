import { Component, input } from '@angular/core';

// The only hardware allowed to hold paper DOWN — a card, a tag, anything lying on a surface.
// Something that hangs takes app-brass-hook instead; swapping the two breaks the workshop's
// physical logic. No size input on purpose: the drawing stops reading as a pin much past 16px.
@Component({
    selector: 'app-brass-pin',
    host: {
        '[attr.role]': 'label() ? "img" : null',
        '[attr.aria-label]': 'label() || null',
        '[attr.aria-hidden]': 'label() ? null : "true"',
    },
    template: `
        <svg width="14" height="14" viewBox="0 0 14 14">
            <ellipse cx="7" cy="11.5" rx="3.4" ry="1.4" fill="var(--brass-edge)" opacity="0.3"></ellipse>
            <circle cx="7" cy="6" r="4.4" fill="var(--brass)"></circle>
            <circle cx="7" cy="6" r="4.4" fill="none" stroke="var(--brass-edge)" stroke-width="0.8"></circle>
            <circle cx="5.4" cy="4.5" r="1.4" fill="var(--brass-hi)"></circle>
        </svg>
    `,
    styles: [`
        :host { display: inline-flex; flex: none; }
        svg { display: block; }
    `],
})
export class BrassPinComponent {
    label = input('');
}
