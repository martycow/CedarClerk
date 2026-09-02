import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export interface HeaderMeta {
    text: string;
    title?: string;
    tone?: 'ok' | 'muted' | 'warn';
    tag?: boolean;
    /** A token (`--series-1`, or its `var()`) drawn as a dot before the text — a legend entry (ADR-146). */
    swatch?: string;
}

// A page has a header, not a sign board (ADR-239 clause 6): the title, one meta line, the one
// primary action and at most one secondary. What a page used to publish into the ruler and the
// rail it renders here itself.
@Component({
    selector: 'app-page-header',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { 'data-surface': 'paper' },
    template: `
        <header class="page-header">
            <div class="page-heading">
                @if (kicker()) { <p class="page-kicker">{{ kicker() }}</p> }
                <div class="page-title-row">
                    @if (headingLevel() === 1) {
                        <h1 class="page-title">{{ title() }}</h1>
                    } @else {
                        <h2 class="page-title">{{ title() }}</h2>
                    }
                    <ng-content select="[title-tail]" />
                </div>
                @if (meta().length) {
                    <p class="page-meta">
                        @for (m of meta(); track $index; let last = $last) {
                            @if (m.tag) {
                                <span class="tag" [class.ok]="m.tone === 'ok'" [class.muted]="m.tone === 'muted' || !m.tone"
                                      [class.warn]="m.tone === 'warn'" [attr.title]="m.title || null">{{ m.text }}</span>
                            } @else {
                                <span [attr.title]="m.title || null">
                                    @if (m.swatch) { <i class="swatch" aria-hidden="true" [style.background]="swatchOf(m.swatch)"></i>
                                    }{{ m.text }}</span>
                            }
                            @if (!last) { <span class="sep" aria-hidden="true">·</span> }
                        }
                    </p>
                }
            </div>
            <div class="page-actions">
                <ng-content select="[secondary]" />
                <ng-content select="[primary]" />
            </div>
        </header>
    `,
    styles: [`
        :host { display: block; flex: none; }

        .page-header { flex-wrap: wrap; }

        .page-heading {
            display: flex;
            flex: 1 1 auto;
            flex-direction: column;
            gap: 6px;
            min-width: 0;
        }

        .page-kicker {
            margin: 0;
            font-size: var(--fs-14);
            color: var(--wood-ink-soft);
        }

        .page-title-row {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            min-width: 0;
        }

        .page-title {
            overflow: hidden;
            white-space: nowrap;
            text-overflow: ellipsis;
        }

        .page-actions {
            display: flex;
            align-items: center;
            justify-content: flex-end;
            flex: 0 1 auto;
            flex-wrap: wrap;
            gap: var(--space-2);
            min-width: 0;
            max-width: 100%;
        }
    `],
})
export class PageHeaderComponent {
    readonly title = input.required<string>();
    readonly kicker = input('');
    readonly meta = input<readonly HeaderMeta[]>([]);
    readonly headingLevel = input<1 | 2>(1);

    protected swatchOf(token: string): string {
        return token.startsWith('var(') ? token : `var(${token})`;
    }
}
