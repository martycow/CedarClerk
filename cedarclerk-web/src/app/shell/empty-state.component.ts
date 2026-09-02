import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';

// Every empty state names the next action (ADR-239 clause 8): a dashed area, one sentence, one
// control, and a hand-written note under it where the page wants a hint.
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
            flex: 1;
            flex-direction: column;
            min-height: 0;
        }

        .empty-state { flex: 1; }
        .es-icon { opacity: .7; }
        .es-action { display: flex; gap: var(--space-2); }
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
