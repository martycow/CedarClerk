import { AfterViewInit, Component, ElementRef, HostListener, Input, OnDestroy, inject, output, viewChild } from '@angular/core';
import { IconComponent } from './icon.component';
import { LocaleService } from '../core/i18n/locale.service';
import { OverlayCoordinatorService, OverlayLayerLease, ShellOverlay } from '../core/overlay-coordinator.service';

let nextModalId = 0;

// Reusable centered modal shell. Every dialog dims the page and closes only through Escape, the
// ✕ or one of its own actions — a click on the scrim does nothing (ADR-285), so a stray press
// outside the card cannot take a half-filled form away. Content is split into three projected
// slots (icon, title, actions) plus a default slot for the body.
@Component({
    selector: 'app-modal',
    imports: [IconComponent],
    templateUrl: './modal.component.html',
    styleUrl: './modal.component.css',
})
export class ModalComponent implements AfterViewInit, OnDestroy {
    // The close button's name was the one hardcoded English string left in the shared chrome
    // (T-080) — and it sits in every modal in the app, so it was the most-seen of them.
    t = inject(LocaleService).t;

    @Input() width = 380;
    @Input() overlayOwner: ShellOverlay | null = null;
    closed = output<void>();
    readonly titleId = `cedar-modal-title-${++nextModalId}`;

    private readonly host = inject(ElementRef<HTMLElement>);
    private readonly overlays = inject(OverlayCoordinatorService);
    private readonly card = viewChild.required<ElementRef<HTMLElement>>('card');
    private layer?: OverlayLayerLease;
    private destroyed = false;

    ngAfterViewInit(): void {
        this.layer = this.overlays.registerLayer(this.host.nativeElement, this.overlayOwner);
        queueMicrotask(() => {
            if (this.destroyed) return;
            const card = this.card().nativeElement;
            const autofocus = card.querySelector<HTMLElement>('[data-modal-autofocus]');
            this.overlays.focusFirst(card, autofocus?.querySelector<HTMLElement>('button:not([disabled])') ?? autofocus ?? card.querySelector<HTMLElement>('.modal-body')
                ?.querySelector<HTMLElement>('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'));
        });
    }

    ngOnDestroy(): void {
        this.destroyed = true;
        this.layer?.release();
    }

    @HostListener('document:keydown', ['$event'])
    onKeydown(event: KeyboardEvent): void {
        if (!this.layer?.isTop()) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopImmediatePropagation();
            this.closed.emit();
            return;
        }
        if (event.key !== 'Tab') return;
        this.overlays.trapTab(this.card().nativeElement, event);
    }
}
