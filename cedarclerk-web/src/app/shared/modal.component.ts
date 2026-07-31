import { Component, HostListener, Input, inject, output } from '@angular/core';
import { IconComponent } from './icon.component';
import { LocaleService } from '../core/i18n/locale.service';

// Reusable centered modal shell — extracted from the hand-rolled .modal-overlay/.modal-card
// pattern that was duplicated between the AI-edit confirm dialog and the re-translate confirm
// dialog in editor.component.html. Closes on Escape and on backdrop click; content is split into
// three projected slots (icon, title, actions) plus a default slot for the body.
@Component({
    selector: 'app-modal',
    imports: [IconComponent],
    templateUrl: './modal.component.html',
    styleUrl: './modal.component.css',
})
export class ModalComponent {
    // The close button's name was the one hardcoded English string left in the shared chrome
    // (T-080) — and it sits in every modal in the app, so it was the most-seen of them.
    t = inject(LocaleService).t;

    @Input() width = 380;
    closed = output<void>();

    @HostListener('document:keydown.escape')
    onEscape() {
        this.closed.emit();
    }
}
