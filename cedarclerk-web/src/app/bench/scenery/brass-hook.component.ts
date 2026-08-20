import { Component } from '@angular/core';

// The hardware for anything that HANGS — a task tag, a tool on the rail. Paper that lies on a
// surface takes app-brass-pin instead; swapping the two breaks the workshop's physical logic.
// Never labelled: a hook is scenery, and what hangs from it carries whatever the row means.
@Component({
    selector: 'app-brass-hook',
    host: { 'aria-hidden': 'true' },
    template: `
        <svg width="14" height="18" viewBox="0 0 14 18">
            <rect x="6" y="0" width="2" height="7" rx="1" fill="var(--brass-lo)"></rect>
            <circle cx="7" cy="11" r="4" fill="none" stroke="var(--brass)" stroke-width="2.2"></circle>
            <circle cx="7" cy="2" r="1.6" fill="var(--brass)" stroke="var(--brass-edge)" stroke-width="0.6"></circle>
        </svg>
    `,
    styles: [`
        :host { display: inline-flex; flex: none; }
        svg { display: block; }
    `],
})
export class BrassHookComponent {}
