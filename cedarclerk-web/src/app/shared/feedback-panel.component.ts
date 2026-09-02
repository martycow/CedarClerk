import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FeedbackFormService, FeedbackKind } from '../core/feedback-form.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { ModalComponent } from './modal.component';
import { IconComponent } from './icon.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { LeafTagComponent } from '../bench/display/leaf-tag.component';

// T-191 — the feedback modal, hoisted in the shell and opened from the tray. A coarse kind and a
// message; the screen the reporter was on rides along so a bug report carries its context.
@Component({
    selector: 'app-feedback-panel',
    imports: [FormsModule, ModalComponent, IconComponent, ButtonComponent, LeafTagComponent],
    template: `
        @if (feedback.open()) {
        <app-modal [width]="460" overlayOwner="feedback" (closed)="close()">
            <app-icon modal-icon name="chat-teardrop-dots" size="sm" />
            <span modal-title>{{ t().feedbackForm.title }}</span>
            <p class="fb-hint">{{ t().feedbackForm.hint }}</p>

            <div class="fb-kinds" role="group" [attr.aria-label]="t().feedbackForm.kindLabel">
                @for (k of kinds; track k) {
                <app-leaf-tag interactive [state]="kind() === k ? 'active' : 'idle'"
                              (activated)="kind.set(k)">{{ t().feedbackForm.kinds[k] }}</app-leaf-tag>
                }
            </div>

            <label class="fb-label" for="cedar-feedback-message">{{ t().feedbackForm.messageLabel }}</label>
            <textarea id="cedar-feedback-message" class="fb-message" rows="5" [(ngModel)]="message"
                      [placeholder]="t().feedbackForm.placeholder" maxlength="4000"></textarea>

            @if (error()) { <p class="fb-error">{{ error() }}</p> }
            @if (done()) { <p class="fb-done"><app-icon name="check" size="xs" />{{ t().feedbackForm.thanks }}</p> }

            <app-button modal-actions variant="paper" (clicked)="close()">{{ t().common.cancel }}</app-button>
            <app-button modal-actions variant="pine" [disabled]="busy() || !message.trim()" (clicked)="send()">
                {{ busy() ? '…' : t().feedbackForm.send }}
            </app-button>
        </app-modal>
        }
    `,
    styles: [`
        .fb-hint { margin: 0 0 var(--space-3); font-size: var(--fs-ui); color: var(--t2); line-height: 1.45; }
        .fb-kinds { display: flex; gap: var(--space-2); margin-bottom: var(--space-3); }
        .fb-label {
            display: block; margin: 0 0 var(--space-1); color: var(--text);
            font-size: var(--fs-13); font-weight: 700;
        }
        .fb-message {
            box-sizing: border-box; width: 100%; padding: var(--space-2) var(--space-3);
            border: 1px solid var(--border); border-radius: var(--radius-md);
            background: var(--sheet); color: var(--text);
            font-family: var(--font-sans); font-size: var(--fs-ui); resize: vertical;
        }
        .fb-error { margin: var(--space-2) 0 0; color: var(--danger); font-size: var(--fs-ui); }
        .fb-done {
            display: flex; align-items: center; gap: var(--space-1);
            margin: var(--space-2) 0 0; color: var(--ok); font-size: var(--fs-ui);
        }
    `],
})
export class FeedbackPanelComponent {
    protected readonly feedback = inject(FeedbackFormService);
    protected readonly t = inject(LocaleService).t;

    protected readonly kinds: FeedbackKind[] = ['bug', 'idea', 'other'];
    protected readonly kind = signal<FeedbackKind>('idea');
    protected message = '';
    protected readonly busy = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly done = signal(false);

    close() {
        this.feedback.closeForm();
        this.message = '';
        this.error.set(null);
        this.done.set(false);
        this.kind.set('idea');
    }

    async send() {
        const message = this.message.trim();
        if (!message) return;
        this.busy.set(true);
        this.error.set(null);
        try {
            await this.feedback.submit(this.kind(), message, location.pathname + location.search);
            this.done.set(true);
            this.message = '';
            setTimeout(() => this.close(), 1400);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().feedbackForm.failed));
        } finally {
            this.busy.set(false);
        }
    }
}
