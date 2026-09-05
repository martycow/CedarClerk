import { Component, inject, input, output } from '@angular/core';
import { ButtonComponent } from '../bench/forms/button.component';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';
import { ModalComponent } from './modal.component';

@Component({
    selector: 'app-confirmation-dialog',
    imports: [ModalComponent, ButtonComponent, IconComponent],
    template: `
        <app-modal [width]="460" (closed)="answered.emit(false)">
            <app-icon modal-icon name="warning" size="md" />
            <span modal-title>{{ title() }}</span>
            <p class="confirmation-message">{{ message() }}</p>
            <div modal-actions class="confirmation-actions">
                <app-button variant="paper" data-modal-autofocus (clicked)="answered.emit(false)">{{ t().common.cancel }}</app-button>
                <app-button variant="danger" (clicked)="answered.emit(true)">{{ confirmLabel() }}</app-button>
            </div>
        </app-modal>
    `,
    styles: [`
        .confirmation-message { margin: 0; overflow-wrap: anywhere; color: var(--text); }
        .confirmation-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--space-2); }
        .confirmation-actions app-button { max-width: 100%; }
    `],
})
export class ConfirmationDialogComponent {
    protected readonly t = inject(LocaleService).t;
    readonly title = input.required<string>();
    readonly message = input.required<string>();
    readonly confirmLabel = input.required<string>();
    readonly answered = output<boolean>();
}
