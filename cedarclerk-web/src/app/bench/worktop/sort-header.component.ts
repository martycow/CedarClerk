import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';
import { SortDirection } from '../../core/collection-query';

@Component({
    selector: 'app-sort-header',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    template: `
        <button type="button" [class.is-active]="active()" [class.is-end]="align() === 'end'"
                [attr.aria-label]="accessibleLabel()" [attr.title]="accessibleLabel()"
                (click)="sorted.emit()">
            <span><ng-content>{{ label() }}</ng-content></span>
            @if (active()) {
                <app-icon [name]="direction() === 'asc' ? 'arrow-up' : 'arrow-down'" size="xs" />
            }
        </button>
    `,
    styles: [`
        :host { display: block; min-width: 0; }

        button {
            display: flex;
            align-items: center;
            gap: var(--space-1);
            width: 100%;
            min-width: 0;
            min-height: var(--hit-surface, var(--hit-chrome));
            padding: 0;
            border: 0;
            background: none;
            color: inherit;
            font: inherit;
            text-align: left;
            cursor: pointer;
        }

        button.is-end { justify-content: flex-end; text-align: right; }
        button.is-active { color: var(--text); }
        button > span { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        app-icon { flex: none; color: var(--accent); }
    `],
})
export class SortHeaderComponent {
    readonly label = input.required<string>();
    readonly active = input(false);
    readonly direction = input<SortDirection>('asc');
    readonly align = input<'start' | 'end'>('start');
    readonly sortByLabel = input.required<string>();
    readonly ascendingLabel = input.required<string>();
    readonly descendingLabel = input.required<string>();
    readonly sorted = output<void>();

    readonly accessibleLabel = computed(() => {
        const prefix = `${this.sortByLabel()}: ${this.label()}`;
        if (!this.active()) return prefix;
        return `${prefix}, ${this.direction() === 'asc' ? this.ascendingLabel() : this.descendingLabel()}`;
    });
}
