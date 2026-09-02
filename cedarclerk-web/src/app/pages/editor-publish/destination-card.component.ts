import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import { BrandIconComponent, BrandIconName } from '../../shared/brand-icon.component';
import { IconName } from '../../shared/icon-data.generated';
import { IconComponent } from '../../shared/icon.component';
import { DestinationState, StateTagComponent } from '../editor-preview/state-tag.component';

export type DestinationKind = 'publish' | 'copy' | 'unsupported';

// One row of the Publish rack (ADR-242 clause 6): a labelled checkbox that says whether the
// destination publishes, and a button that says whose settings the middle column shows. Two
// controls, two tab stops, two names — the checkbox is never inside the button.
@Component({
    selector: 'app-destination-card',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent, BrandIconComponent, StateTagComponent],
    host: {
        class: 'dest-card',
        '[class.on]': 'included()',
        '[class.active]': 'active()',
        '[class.off]': '!includable()',
        '[class.copy-target]': "kind() === 'copy'",
        '[class.unsupported]': "kind() === 'unsupported'",
        '[attr.data-destination]': 'id()',
    },
    template: `
        @if (includable()) {
            <input type="checkbox" class="dest-include" [checked]="included()" [attr.aria-label]="includeLabel()"
                   (change)="include.emit($any($event.target).checked)">
        } @else {
            <span class="dest-include-gap" aria-hidden="true"></span>
        }
        <button type="button" class="dest-select" [attr.aria-pressed]="active()" [attr.aria-label]="selectLabel()"
                (click)="select.emit()">
            <span class="dest-mark" aria-hidden="true">
                @if (brand()) { <app-brand-icon [name]="brand()!" [size]="16" /> }
                @else if (icon()) { <app-icon [name]="icon()!" size="sm" /> }
            </span>
            <span class="dest-text">
                <span class="dest-card-name">{{ name() }}</span>
                @if (state()) { <app-state-tag [state]="state()!" /> }
                @if (meta()) { <span class="dest-card-meta">{{ meta() }}</span> }
            </span>
        </button>
        <div class="dest-extra"><ng-content /></div>
    `,
    styles: [`
        :host {
            display: grid;
            grid-template-columns: 18px minmax(0, 1fr);
            align-items: center;
            column-gap: var(--space-2);
            box-sizing: border-box;
            padding: var(--space-2) var(--space-3);
            border: 1px solid transparent;
            border-left: 3px solid transparent;
            border-radius: var(--radius-sm);
            font-family: var(--font-sans);
            font-size: var(--fs-ui);
        }

        :host(:hover) { background: var(--alt); }
        :host(.active) { border-color: var(--abord); border-left-color: var(--accent); background: var(--asoft); }
        :host(.unsupported) { opacity: .72; }
        :host(.unsupported) .dest-select { cursor: default; }

        .dest-include {
            width: 18px;
            height: 18px;
            margin: 0;
            accent-color: var(--accent);
        }

        .dest-include-gap { width: 18px; height: 18px; }

        .dest-select {
            display: flex;
            align-items: flex-start;
            gap: var(--space-3);
            min-width: 0;
            min-height: var(--hit-surface, var(--hit-target));
            padding: var(--space-1) 0;
            border: 0;
            background: none;
            font-family: inherit;
            font-size: inherit;
            color: var(--text);
            text-align: left;
            cursor: pointer;
        }

        .dest-mark {
            display: flex;
            align-items: center;
            justify-content: center;
            flex: none;
            width: 26px;
            height: 26px;
            margin-top: 2px;
            border-radius: 50%;
            background: var(--surface);
            color: var(--text);
        }

        .dest-text { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; min-width: 0; }

        .dest-card-name { font-size: var(--fs-15); font-weight: 700; }

        .dest-card-meta {
            max-width: 100%;
            overflow: hidden;
            font-size: var(--fs-13);
            color: var(--t2);
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .dest-extra { grid-column: 2; font-size: var(--fs-13); }
        .dest-extra:empty { display: none; }
    `],
})
export class DestinationCardComponent {
    readonly id = input.required<string>();
    readonly name = input.required<string>();
    readonly brand = input<BrandIconName | null>(null);
    readonly icon = input<IconName | null>(null);
    readonly meta = input('');
    readonly state = input<DestinationState | null>(null);
    readonly kind = input<DestinationKind>('publish');
    /** A publish destination that can be ticked — an unconnected network or a copy target cannot. */
    readonly includable = input(false);
    readonly included = input(false);
    readonly active = input(false);
    readonly include = output<boolean>();
    readonly select = output<void>();

    private readonly t = inject(LocaleService).t;

    protected readonly includeLabel = computed(() => this.t().editor.exportModal.includeDestination(this.name()));
    protected readonly selectLabel = computed(() => this.t().editor.publishTab.settingsFor(this.name()));
}
