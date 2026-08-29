import { Component } from '@angular/core';

// The hardware for anything that HANGS — a task tag, a tool on the rail. Paper that lies on a
// surface takes app-brass-pin instead; swapping the two breaks the workshop's physical logic.
// Never labelled: a hook is scenery, and what hangs from it carries whatever the row means.
@Component({
    selector: 'app-brass-hook',
    host: { 'aria-hidden': 'true' },
    template: `
        <!-- One continuous forged J under its screw head. The gradient id is document-global
             (default encapsulation keeps it in the DOM); every instance is identical, so the
             duplicates all resolve to the same paint — keep the id unique to this component.
             The highlight's #FFF3D6 is a specular glint on metal, not a themable ink. -->
        <svg width="14" height="20" viewBox="0 0 14 20">
            <defs>
                <linearGradient id="brassHook" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0" stop-color="var(--brass-hi)"></stop>
                    <stop offset="1" stop-color="var(--brass)"></stop>
                </linearGradient>
            </defs>
            <circle cx="7" cy="2.2" r="1.8" fill="url(#brassHook)" stroke="var(--brass-edge)" stroke-width="0.6"></circle>
            <path d="M7 3.5v6.5c0 3.2-1.8 4.6-4 3.9" fill="none" stroke="url(#brassHook)" stroke-width="2.4" stroke-linecap="round"></path>
            <path d="M7 4v6c0 2.6-1.2 3.8-2.8 3.5" fill="none" stroke="#FFF3D6" stroke-width="0.7" stroke-linecap="round" opacity="0.55"></path>
        </svg>
    `,
    styles: [`
        :host { display: inline-flex; flex: none; }
        svg { display: block; }
    `],
})
export class BrassHookComponent {}
