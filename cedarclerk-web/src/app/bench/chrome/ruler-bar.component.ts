import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** One readout: a count, a state word, a version. */
export interface RulerReadout {
    /** What the ruler says. Digit runs inside it are drawn mono. */
    text: string;
    /** Long-form for a readout too terse to read on its own. */
    title?: string;
}

interface Part {
    text: string;
    num: boolean;
}

// A run of digits, with the separators that belong inside one number rather than between two:
// "11/19", "0.12.0" and "09:51" are each one numeral, not two or three.
const NUMERAL = /(\d+(?:[.,:/]\d+)*)/g;

// The bench's cut bottom edge: a plank of the shell's own wood carrying mono numerals, the status
// bar. Wood rather than the kit's milled brass (ADR-184) — brass is what you take hold of, and this
// strip is gripped by nothing. It takes readouts as strings and never as projected content, because
// the one rule this bar has is that it is read-only — a slot here is an invitation to put a control
// in it, and the invitation is the violation. Anything clickable belongs on the lip above.
@Component({
    selector: 'app-ruler-bar',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { 'data-surface': 'chrome' },
    imports: [NgTemplateOutlet],
    // Written without a break between a readout's pieces: Angular collapses template whitespace,
    // and a newline between two spans of one numeral would be rendered as a space inside it.
    template: `
        @if (label()) {<span class="label">{{ label() }}</span>}
        <ng-container *ngTemplateOutlet="rule; context: { $implicit: leftParts() }" />
        <span class="gap"></span>
        <ng-container *ngTemplateOutlet="rule; context: { $implicit: rightParts() }" />

        <ng-template #rule let-list>
            @for (r of list; track $index) {
                <span class="readout" [attr.title]="r.title">@for (p of r.parts; track $index) {<span [class.num]="p.num">{{ p.text }}</span>}</span>
            }
        </ng-template>
    `,
    styles: [`
        :host([data-surface="chrome"]) {
            display: flex;
            align-items: center;
            gap: var(--space-4);
            box-sizing: border-box;
            min-height: var(--bench-ruler-h);
            padding: 0 14px;
            border-top: 1px solid var(--rail-edge);
            box-shadow: inset 0 1px 0 rgba(255, 240, 210, .1);
            font-family: var(--font-readout);
            font-size: var(--text-chrome-sm);
            white-space: nowrap;
            overflow: hidden;

            /* The same wood as the rail and the lip, laid the darker way round: two boards meeting
               at an edge, not one board with a seam drawn on it. The lip above is a drawer front
               and has a pull; this is the edge of the bench and has nothing. */
            background-color: var(--rail-lo);
            background-image: var(--tex-wood), var(--surface-rail);
            background-size: 420px, auto;

            color: var(--rail-ink);
            text-shadow: 0 1px 1px var(--rail-edge);

            .label {
                flex: none;
                font-family: var(--font-sans);
                font-size: var(--text-chrome-sm);
                font-weight: 700;
                letter-spacing: .1em;
                text-transform: uppercase;
            }

            .readout {
                flex: none;
                min-width: 0;
                overflow: hidden;
                text-overflow: ellipsis;
            }

            /* Every number on the rule is mono, and reads as a scale rather than as prose: the
               figures are the same width, so a count that ticks does not shuffle the bar. */
            .num {
                font-family: var(--font-readout);
                font-variant-numeric: tabular-nums;
            }

            .gap { flex: 1; }
        }
    `],
})
export class RulerBarComponent {
    /** The object being measured — letterspaced sans at the left end. */
    readonly label = input('');
    /** Readouts before the gap. */
    readonly left = input<readonly RulerReadout[]>([]);
    /** Readouts after the gap, at the right end of the rule. */
    readonly right = input<readonly RulerReadout[]>([]);

    readonly leftParts = computed(() => this.left().map(split));
    readonly rightParts = computed(() => this.right().map(split));
}

function split(readout: RulerReadout): { title: string | null; parts: Part[] } {
    const parts = readout.text
        .split(NUMERAL)
        .filter(chunk => chunk !== '')
        .map(chunk => ({ text: chunk, num: /^\d/.test(chunk) }));
    return { title: readout.title ?? null, parts };
}
