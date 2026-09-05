import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';

@Component({
    selector: 'app-empty-state',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: { 'data-surface': 'paper' },
    template: `
        <div class="empty-state">
            @if (icon(); as name) { <app-icon class="es-icon" [name]="name" size="md" /> }
            @if (title()) { <p class="es-title">{{ title() }}</p> }
            @if (text()) { <p class="es-text">{{ text() }}</p> }
            <div class="es-action"><ng-content /></div>
        </div>
        @if (note()) { <p class="margin-note es-note">{{ note() }}</p> }
    `,
    styles: [`
        :host {
            display: flex;
            flex: 0 0 auto;
            flex-direction: column;
            min-height: 0;
        }

        .empty-state {
            flex: 0 0 auto;
            box-sizing: border-box;
            min-height: 240px;
            background: var(--sheet);
            color: var(--text);
            border: 1px solid var(--border);
        }
        .es-icon {
            display: grid;
            place-items: center;
            width: var(--space-8);
            height: var(--space-8);
            border-radius: var(--radius-md);
            background: var(--asoft);
            color: var(--accent);
        }
        .empty-state .es-title { font-family: var(--font-display); font-size: var(--empty-title-size, var(--fs-20)); }
        .empty-state .es-text { max-width: 48ch; opacity: 1; color: var(--t2); }
        .es-action { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--space-2); max-width: 100%; }
        .es-action:empty { display: none; }
        .es-note { margin: var(--space-3) var(--space-4) 0; }
    `],
})
export class EmptyStateComponent {
    readonly icon = input<IconName | null>('plus');
    readonly title = input('');
    readonly text = input('');
    readonly note = input('');
}
