import { Component, ElementRef, HostListener, OnDestroy, ViewChild, inject, input, signal } from '@angular/core';

@Component({
    selector: 'app-popover',
    templateUrl: 'popover.component.html',
    styleUrls: ['popover.component.css'],
})
export class PopoverComponent implements OnDestroy {
    private readonly host = inject(ElementRef<HTMLElement>);

    align = input<'left' | 'right'>('left');

    isOpen = signal(false);
    panelTop = signal(0);
    panelLeft = signal<number | null>(null);
    panelRight = signal<number | null>(null);

    @ViewChild('triggerEl') triggerRef!: ElementRef<HTMLElement>;

    // Bound so it can be added/removed as the same reference; scroll doesn't bubble, so this
    // must be registered in the capture phase to catch scrolling of the toolbar (or any other
    // ancestor), not just the window.
    private readonly onAncestorScroll = () => this.close();

    toggle() {
        if (this.isOpen()) {
            this.close();
        } else {
            this.open();
        }
    }

    open() {
        this.updatePosition();
        this.isOpen.set(true);
        document.addEventListener('scroll', this.onAncestorScroll, { capture: true, passive: true });
    }

    close() {
        this.isOpen.set(false);
        document.removeEventListener('scroll', this.onAncestorScroll, { capture: true });
    }

    ngOnDestroy() {
        document.removeEventListener('scroll', this.onAncestorScroll, { capture: true });
    }

    // Position is computed from the trigger's viewport rect and the panel is rendered with
    // position:fixed — that's what lets it escape ancestors like the toolbar that scroll
    // horizontally (overflow-x: auto also clips the y-axis per the CSS overflow spec, so a
    // plain absolutely-positioned panel nested in the toolbar always gets cut off).
    private updatePosition() {
        const rect = this.triggerRef.nativeElement.getBoundingClientRect();
        this.panelTop.set(rect.bottom + 8);
        if (this.align() === 'right') {
            this.panelLeft.set(null);
            this.panelRight.set(window.innerWidth - rect.right);
        } else {
            this.panelLeft.set(rect.left);
            this.panelRight.set(null);
        }
    }

    @HostListener('window:resize')
    onResize() {
        if (this.isOpen()) this.updatePosition();
    }

    @HostListener('document:keydown.escape')
    onEscape() {
        this.close();
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
        this.close();
    }
}
