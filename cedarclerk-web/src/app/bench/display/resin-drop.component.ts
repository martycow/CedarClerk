import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type ResinState = 'forming' | 'set';

// Sap on a cut branch: it forms while work is unsaved and sets hard once saved. This is the ONLY
// save indicator in the system — no spinner, no "saving…" toast — and it never goes on a button.
//
// Motion is not guarded here: styles.scss already collapses --motion-* and every animation under
// @media (prefers-reduced-motion: reduce), globally and with !important, which reaches an
// emulated-encapsulation component's own keyframes. A second block would be a second place for
// the two to disagree.
@Component({
    selector: 'app-resin-drop',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'chrome',
        'role': 'status',
        '[attr.data-state]': 'state()',
        '[attr.title]': 'title() || null',
        '[attr.aria-label]': 'title() || null',
    },
    template: `
        <span class="drop" aria-hidden="true">
            <span class="gloss"></span>
            @if (state() === 'set') { <span class="glint"></span> }
        </span>
        @if (label()) { <span class="label">{{ label() }}</span> }
    `,
    styles: [`
        :host {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
        }

        /* The drop's own geometry: one size, everything else a ratio of it, so the shape survives
           a change of scale. It rides --icon-xs because it sits in a line of chrome beside icons. */
        .drop {
            --drop-w: var(--icon-xs);
            position: relative;
            flex: none;
            width: var(--drop-w);
            height: calc(var(--drop-w) * 1.15);
            overflow: hidden;
            border-radius: 50% 50% 50% 50% / 62% 62% 40% 40%;
            background: radial-gradient(circle at 34% 30%,
                var(--resin-hi),
                var(--resin) 52%,
                color-mix(in srgb, var(--resin) 55%, var(--wood-edge)) 95%);
            box-shadow:
                inset 0 -2px 3px color-mix(in srgb, var(--wood-edge) 45%, transparent),
                0 1px 2px color-mix(in srgb, var(--rail-edge) 40%, transparent);
        }

        .gloss {
            position: absolute;
            top: 17%;
            left: 23%;
            width: 23%;
            height: 20%;
            border-radius: 50%;
            background: var(--paper-bright);
        }

        .glint {
            position: absolute;
            top: -27%;
            left: -77%;
            width: 38%;
            height: 173%;
            background: color-mix(in srgb, var(--paper-bright) 75%, transparent);
            transform: rotate(24deg);
            animation: bench-resin-glint calc(var(--motion-slow) * 3) var(--ease) 1 both;
        }

        :host([data-state="forming"]) .drop {
            opacity: .62;
            animation: bench-resin-form calc(var(--motion-slow) * 6) var(--ease) infinite alternate;
        }

        .label {
            font-family: var(--font-sans);
            font-weight: 600;
            white-space: nowrap;
            /* The drop hangs on the rail as well as on paper, and the rail's ink is not --t2. */
            color: var(--resin-label-ink, var(--t2));
        }

        /* The surface owns the size, so the lint can score it (ADR-138). */
        :host([data-surface="chrome"]) .label {
            font-size: var(--text-chrome);
        }

        @keyframes bench-resin-form {
            from { transform: scale(.88); }
            to { transform: scale(1); }
        }

        @keyframes bench-resin-glint {
            from { left: -77%; opacity: 0; }
            30% { opacity: 1; }
            to { left: 138%; opacity: 0; }
        }
    `],
})
export class ResinDropComponent {
    readonly state = input<ResinState>('forming');
    /** Already-translated text shown beside the drop. */
    readonly label = input('');
    /** Already-translated accessible name — what the drop means when its word is not shown. */
    readonly title = input('');
}
