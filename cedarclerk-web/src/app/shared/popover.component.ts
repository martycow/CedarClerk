import { Component, ElementRef, HostListener, OnDestroy, ViewChild, inject, input, signal } from '@angular/core';
import { OverlayCoordinatorService } from '../core/overlay-coordinator.service';

@Component({
    selector: 'app-popover',
    templateUrl: 'popover.component.html',
    styleUrls: ['popover.component.css'],
})
export class PopoverComponent implements OnDestroy {
    private readonly host = inject(ElementRef<HTMLElement>);
    private readonly overlays = inject(OverlayCoordinatorService);
    private readonly unregisterPeer = this.overlays.registerDismissablePeer(activeElement => {
        const returnFocus = activeElement && this.host.nativeElement.contains(activeElement)
            ? this.triggerRef?.nativeElement.querySelector<HTMLElement>('[trigger]')
                ?? this.triggerRef?.nativeElement ?? null
            : null;
        if (this.isOpen()) this.close();
        return returnFocus;
    });

    align = input<'left' | 'right'>('left');
    placement = input<'vertical' | 'right-start' | 'left-start'>('vertical');

    isOpen = signal(false);
    panelTop = signal(0);
    panelBottom = signal<number | null>(null);
    panelLeft = signal<number | null>(null);
    panelRight = signal<number | null>(null);
    panelMaxHeight = signal(0);

    @ViewChild('triggerEl') triggerRef!: ElementRef<HTMLElement>;
    @ViewChild('panelEl') panelRef?: ElementRef<HTMLElement>;

    // Bound so it can be added/removed as the same reference; scroll doesn't bubble, so this
    // must be registered in the capture phase to catch scrolling of the toolbar (or any other
    // ancestor), not just the window.
    private readonly onAncestorScroll = (event: Event) => {
        const target = event.target instanceof Node ? event.target : null;
        if (target && this.panelRef?.nativeElement.contains(target)) return;
        this.close();
    };

    toggle(event?: MouseEvent) {
        if (this.isOpen()) {
            this.close();
        } else {
            this.open(event?.detail === 0);
        }
    }

    open(focusPanel = false) {
        this.overlays.dismissPopovers();
        this.updatePosition();
        this.isOpen.set(true);
        document.addEventListener('scroll', this.onAncestorScroll, { capture: true, passive: true });
        // T-341 — the first pass clamps with a guessed width (the panel is not in the DOM yet);
        // this one re-clamps with the real box, which is what keeps a 260px panel opened from the
        // inspector shelf on the screen.
        requestAnimationFrame(() => {
            if (!this.isOpen()) return;
            this.updatePosition();
            if (focusPanel) this.focusFirstPanelControl();
        });
    }

    close(restoreFocus = false) {
        this.isOpen.set(false);
        document.removeEventListener('scroll', this.onAncestorScroll, { capture: true });
        if (restoreFocus) this.triggerControl()?.focus();
    }

    ngOnDestroy() {
        this.unregisterPeer();
        document.removeEventListener('scroll', this.onAncestorScroll, { capture: true });
    }

    // Position is computed from the trigger's viewport rect and the panel is rendered with
    // position:fixed — that's what lets it escape ancestors like the toolbar that scroll
    // horizontally (overflow-x: auto also clips the y-axis per the CSS overflow spec, so a
    // plain absolutely-positioned panel nested in the toolbar always gets cut off).
    private updatePosition() {
        const rect = this.triggerRef.nativeElement.getBoundingClientRect();
        const edge = 12;
        const gap = 8;
        const placement = this.placement();
        if (placement !== 'vertical') {
            const width = this.panelRef?.nativeElement.offsetWidth ?? 240;
            const height = this.panelRef?.nativeElement.offsetHeight ?? 240;
            const sideRect = this.host.nativeElement.closest('app-sidebar')?.getBoundingClientRect() ?? rect;
            const preferredLeft = placement === 'right-start' ? sideRect.right + gap : sideRect.left - width - gap;
            const oppositeLeft = placement === 'right-start' ? sideRect.left - width - gap : sideRect.right + gap;
            const preferredFits = preferredLeft >= edge && preferredLeft + width <= window.innerWidth - edge;
            const oppositeFits = oppositeLeft >= edge && oppositeLeft + width <= window.innerWidth - edge;
            const left = preferredFits ? preferredLeft : oppositeFits ? oppositeLeft
                : Math.max(edge, Math.min(preferredLeft, window.innerWidth - width - edge));
            this.panelTop.set(Math.max(edge, Math.min(rect.top, window.innerHeight - height - edge)));
            this.panelBottom.set(null);
            this.panelLeft.set(left);
            this.panelRight.set(null);
            this.panelMaxHeight.set(Math.max(0, window.innerHeight - edge * 2));
            return;
        }
        const below = window.innerHeight - rect.bottom - gap - edge;
        const above = rect.top - gap - edge;
        const opensAbove = below < 240 && above > below;
        this.panelTop.set(opensAbove ? 0 : rect.bottom + gap);
        this.panelBottom.set(opensAbove ? window.innerHeight - rect.top + gap : null);
        // The available side wins; the cap keeps a panel from hanging past the fold on a short
        // viewport, and the 120 floor only matters when the whole viewport is shorter than that.
        this.panelMaxHeight.set(Math.min(Math.max(120, opensAbove ? above : below), window.innerHeight - edge * 2));
        if (this.align() === 'right') {
            const width = this.panelRef?.nativeElement.offsetWidth ?? 240;
            this.panelLeft.set(null);
            this.panelRight.set(Math.max(edge, Math.min(window.innerWidth - rect.right, window.innerWidth - width - edge)));
        } else {
            // Before the panel exists its width is a guess; the second pass from open() reads the
            // real one (a share panel is 278px outside — the old literal 252 lost that difference).
            const width = this.panelRef?.nativeElement.offsetWidth ?? 240;
            this.panelLeft.set(Math.max(edge, Math.min(rect.left, window.innerWidth - width - edge)));
            this.panelRight.set(null);
        }
    }

    @HostListener('window:resize')
    onResize() {
        if (this.isOpen()) this.updatePosition();
    }

    @HostListener('document:keydown.escape')
    onEscape() {
        if (this.isOpen()) this.close(true);
    }

    // An outside click closes this, and a full-screen backdrop used to be what caught it. The
    // backdrop also ate the click: opening the account menu and then reaching for the one beside
    // it took two presses, and the second menu never saw the first. A document listener closes on
    // the same gesture and lets it through to whatever it was aimed at.
    @HostListener('document:click', ['$event'])
    onDocumentClick(event: MouseEvent) {
        if (!this.isOpen()) return;
        const target = event.target instanceof Element ? event.target : null;
        if (target && this.host.nativeElement.contains(target)) {
            // An entry that leaves the page takes the panel with it, or it hangs over whatever
            // was navigated to; one that acts in place leaves it standing.
            if (target.closest('.popover-panel') && target.closest('a[href]')) this.close();
            return;
        }
        this.close(!this.isFocusableOutsideTarget(target));
    }

    private triggerControl(): HTMLElement | null {
        return this.triggerRef?.nativeElement.querySelector<HTMLElement>('[trigger]')
            ?? this.triggerRef?.nativeElement ?? null;
    }

    private isFocusableOutsideTarget(target: Element | null): boolean {
        const control = target?.closest<HTMLElement>(
            'a[href], area[href], button, input:not([type="hidden"]), select, textarea, summary, iframe, '
            + '[contenteditable]:not([contenteditable="false"]), [tabindex]',
        ) ?? null;
        return control !== null && !control.matches(':disabled') && control.closest('[inert]') === null;
    }

    private focusFirstPanelControl(): void {
        this.panelRef?.nativeElement
            .querySelector<HTMLElement>('button, a[href], input, select, textarea, [tabindex]:not([tabindex="-1"])')
            ?.focus();
    }
}
