import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { StampBadgeComponent, StampTone } from '../display/stamp-badge.component';

/** ok = finished · warn = needs a look · info = happened · build = a version was cut. */
export type LogLevel = 'ok' | 'warn' | 'info' | 'build';

/** The stock the line lies on, named by whoever lays it there (ADR-157). */
export type LogSurface = 'paper' | 'chrome';

const TONES: Record<LogLevel, StampTone> = { ok: 'pine', warn: 'rust', info: 'ink', build: 'brass' };

// One line of the bench journal: when it happened, what it was, and one sentence about it. The
// severity is a rubber stamp and never a coloured dot, a tinted row or a bar down the left edge —
// it is the same app-stamp-badge the rest of the app uses, so the journal needs no vocabulary of
// its own. Nothing about the row itself moves with the level.
@Component({
    selector: 'app-log-line',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [StampBadgeComponent],
    host: {
        '[attr.data-surface]': 'surface()',
    },
    template: `
        @if (time()) {
            <span class="ll-time">{{ time() }}</span>
        }
        <app-stamp-badge class="ll-mark" [tone]="resolvedTone()" [rotate]="-1.2">{{ stamp() }}</app-stamp-badge>
        <span class="ll-msg"><ng-content /></span>
        @if (at()) {
            <span class="ll-at">{{ at() }}</span>
        }
    `,
    styles: [`
        :host {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
            font-family: var(--font-mono);
        }

        .ll-time, .ll-mark, .ll-at { flex: none; }

        /* A stamp on a line is pressed smaller; the hooks inherit into the badge. */
        .ll-mark {
            --stamp-pad: 1px 6px;
            --stamp-border-w: 1px;
            --stamp-tracking: .1em;
        }

        /* The message is held to one line by the component and not by the consumer's discipline:
           the kit's rule is that the anchor goes in the at input rather than into the sentence, and a
           sentence that can wrap is an invitation to write the anchor into it. */
        .ll-msg {
            flex: 1;
            min-width: 0;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            color: var(--text);
        }

        /* The mirror puts both of these on --text-faint. ADR-137 rule 5 names the timestamp as one
           of the call sites the app moves up: a coordinate is read, and content does not sit on
           the third tier. */
        .ll-time, .ll-at { color: var(--t2); }

        /* Two surfaces, two type sizes, both declared so tools/check-density.mjs scores both
           (ADR-157). Only the size moves: the split governs size and never contrast (ADR-138
           item 6), and a line is not a control, so neither block declares a touch floor. */
        :host([data-surface="paper"]) {
            padding: var(--space-1) var(--space-3);
            font-size: var(--fs-ui);
        }

        :host([data-surface="chrome"]) {
            padding: calc(var(--space-1) / 2) var(--space-2);
            font-size: var(--text-chrome-sm);
        }
    `],
})
export class LogLineComponent {
    readonly level = input<LogLevel>('info');
    /** The stamped word. The consumer's to translate — a bench primitive holds no prose. */
    readonly word = input('');
    /** Overrides the tone the level would pick, for a line whose severity and colour disagree. */
    readonly tone = input<StampTone | null>(null);
    readonly time = input('');
    /** The right-hand anchor: a block number, a language, a clock. */
    readonly at = input('');
    readonly surface = input<LogSurface>('paper');

    protected readonly resolvedTone = computed<StampTone>(() => this.tone() ?? TONES[this.level()]);

    // A stamp with no word is the coloured dot the kit refuses, so the level's own id stands in
    // when no word is given. That is a technical token rather than prose, and a line written for a
    // reader passes the translated word.
    protected readonly stamp = computed(() => this.word() || this.level().toUpperCase());
}
