import {
    ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, signal, viewChildren,
} from '@angular/core';
import { OutlineEntry } from '../core/document-outline';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';

// The writer's structure shelf: every top-level block of the open document, one row each, the
// caret's block lit (ADR-162).
//
// It holds no selection of its own. `active` comes in, `pick` goes out, and the row that lights is
// a function of the input — so a click cannot light a row on its own, and the two-way sync has no
// second copy of the truth to loop through. The spec asserts exactly that: clicking a row leaves
// the highlight where it was until the document says otherwise.
@Component({
    selector: 'app-document-outline',
    standalone: true,
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        'data-surface': 'paper',
    },
    template: `
        @if (entries().length) {
            <ul class="ol-list" role="list" [attr.aria-label]="t().editor.outline.title"
                (keydown)="onKeydown($event)" (focusout)="onFocusOut($event)">
                @for (entry of entries(); track entry.index) {
                    <li>
                        <button #row type="button" class="ol-row"
                                [class.is-current]="entry.index === active()"
                                [class.is-indent-1]="entry.indent === 1"
                                [class.is-indent-2]="entry.indent >= 2"
                                [attr.aria-current]="entry.index === active() ? 'true' : null"
                                [attr.tabindex]="entry.index === anchor() ? 0 : -1"
                                [attr.title]="label(entry)"
                                (click)="pick.emit(entry)">
                            <app-icon [name]="entry.icon" size="xs" />
                            <span class="visually-hidden">{{ kindWord(entry) }}</span>
                            <span class="ol-label">{{ label(entry) }}</span>
                        </button>
                    </li>
                }
            </ul>
        } @else {
            <p class="ol-empty">{{ t().editor.outline.empty }}</p>
        }
    `,
    styles: [`
        /* The surface is in the selector, not only on the host: tools/check-density.mjs reads
           declared CSS, and this is what puts the rows under paper's floors. The shelf sheet this
           stands on is already paper — saying so again is what makes the numbers measurable. */
        :host([data-surface="paper"]) {
            display: block;
            min-width: 0;
        }

        :host([data-surface="paper"]) .ol-list {
            display: flex;
            flex-direction: column;
            gap: 1px;
            margin: 0;
            padding: 0;
            list-style: none;
        }

        :host([data-surface="paper"]) .ol-row {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            width: 100%;
            box-sizing: border-box;
            padding: var(--space-1) var(--space-2);
            border: 0;
            border-radius: var(--radius-sm);
            background: none;
            font-family: var(--font-sans);
            font-size: var(--fs-ui);
            text-align: left;
            color: var(--text);
            cursor: pointer;
        }

        :host([data-surface="paper"]) .ol-row:hover { background: var(--alt); }

        /* Three cues, one of them colour (ADR-162 clause 7): the rule down the left edge is a
           shape, the weight is a weight, and aria-current is a word. Written :not(:focus-visible)
           so the box-shadow cannot out-specify the global focus halo (ADR-140). */
        :host([data-surface="paper"]) .ol-row.is-current:not(:focus-visible) {
            box-shadow: inset 2px 0 0 var(--accent);
        }

        :host([data-surface="paper"]) .ol-row.is-current {
            background: var(--asoft);
        }

        :host([data-surface="paper"]) .ol-row.is-current .ol-label { font-weight: 700; }
        :host([data-surface="paper"]) .ol-row app-icon { color: var(--t3); }
        :host([data-surface="paper"]) .ol-row.is-current app-icon { color: var(--accent); }

        :host([data-surface="paper"]) .ol-row.is-indent-1 { padding-left: var(--space-5); }
        :host([data-surface="paper"]) .ol-row.is-indent-2 { padding-left: var(--space-7); }

        :host([data-surface="paper"]) .ol-label {
            flex: 1;
            min-width: 0;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        :host([data-surface="paper"]) .ol-empty {
            margin: 0;
            font-family: var(--font-sans);
            font-size: var(--fs-ui);
            color: var(--t3);
        }

        :host([data-surface="paper"]) .visually-hidden {
            position: absolute;
            width: 1px;
            height: 1px;
            margin: -1px;
            padding: 0;
            border: 0;
            overflow: hidden;
            white-space: nowrap;
            clip-path: inset(50%);
        }
    `],
})
export class DocumentOutlineComponent {
    readonly t = inject(LocaleService).t;

    readonly entries = input.required<OutlineEntry[]>();
    /** Top-level index of the block the caret is in. -1 when the document is empty. */
    readonly active = input(-1);
    readonly pick = output<OutlineEntry>();

    private readonly rows = viewChildren<ElementRef<HTMLButtonElement>>('row');

    /** Where arrow keys have walked to, while the panel has focus. Null the rest of the time. */
    private readonly browsing = signal<number | null>(null);

    /**
     * The one row in the tab order. Entering the panel lands on the current block; once the arrows
     * are in use they lead, so Tab out and back returns to where the reader was reading.
     */
    readonly anchor = computed(() => {
        const browsing = this.browsing();
        if (browsing !== null) return browsing;
        const active = this.active();
        return active >= 0 && active < this.entries().length ? active : 0;
    });

    constructor() {
        // Following the caret with the scrollbar, never with the selection: this reads state and
        // moves a scroll offset, so it cannot feed anything back into the document. Skipped while
        // the panel has focus, because arrow keys scroll by focusing and would fight this.
        effect(() => {
            const active = this.active();
            if (this.browsing() !== null) return;
            const row = this.rows()[active]?.nativeElement;
            row?.scrollIntoView({ block: 'nearest' });
        });
    }

    label(entry: OutlineEntry): string {
        const outline = this.t().editor.outline;
        if (entry.count !== undefined) return `${this.kindWord(entry)} — ${outline.items(entry.count)}`;
        return entry.text || this.kindWord(entry);
    }

    kindWord(entry: OutlineEntry): string {
        const outline = this.t().editor.outline;
        return entry.level !== undefined ? outline.headingLevel(entry.level) : outline.kinds[entry.kind];
    }

    onKeydown(event: KeyboardEvent) {
        const last = this.entries().length - 1;
        const from = this.anchor();
        let to: number;
        switch (event.key) {
            case 'ArrowDown': to = Math.min(from + 1, last); break;
            case 'ArrowUp': to = Math.max(from - 1, 0); break;
            case 'Home': to = 0; break;
            case 'End': to = last; break;
            default: return;
        }
        event.preventDefault();
        this.browsing.set(to);
        this.rows()[to]?.nativeElement.focus();
    }

    /** Focus left the list entirely — hand the tab order back to whatever the caret is in. */
    onFocusOut(event: FocusEvent) {
        const next = event.relatedTarget as Node | null;
        if (next && (event.currentTarget as HTMLElement).contains(next)) return;
        this.browsing.set(null);
    }
}
