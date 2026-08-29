import { Component } from '@angular/core';

// The hardware for anything FIXED to the wall — a tool on the rail. Something that hangs takes
// app-brass-hook, paper lying on a surface takes app-brass-pin; swapping them breaks the
// workshop's physical logic. Never labelled: a nail is scenery, and what it fixes carries
// whatever the row means.
@Component({
    selector: 'app-brass-nail',
    host: { 'aria-hidden': 'true' },
    template: `
        <!-- A domed head over a shaft tapering to the point that bites the tool's edge. The
             gradient id is document-global (default encapsulation keeps it in the DOM); every
             instance is identical, so the duplicates all resolve to the same paint — keep the id
             unique to this component. The glint's #FFF3D6 is a specular spot on metal, not a
             themable ink. -->
        <svg width="10" height="14" viewBox="0 0 10 14">
            <defs>
                <linearGradient id="brassNail" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0" stop-color="var(--brass-hi)"></stop>
                    <stop offset="1" stop-color="var(--brass)"></stop>
                </linearGradient>
            </defs>
            <path d="M3.9 4h2.2L5 13Z" fill="url(#brassNail)" stroke="var(--brass-edge)" stroke-width="0.5"></path>
            <circle cx="5" cy="2.6" r="2.4" fill="url(#brassNail)" stroke="var(--brass-edge)" stroke-width="0.6"></circle>
            <circle cx="4.2" cy="1.9" r="0.8" fill="#FFF3D6" opacity="0.55"></circle>
        </svg>
    `,
    styles: [`
        :host { display: inline-flex; flex: none; }
        svg { display: block; }
    `],
})
export class BrassNailComponent {}
