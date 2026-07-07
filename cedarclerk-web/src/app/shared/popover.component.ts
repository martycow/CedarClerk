import { Component, HostListener, input, signal } from '@angular/core';

@Component({
    selector: 'app-popover',
    templateUrl: 'popover.component.html',
    styleUrls: ['popover.component.css'],
})
export class PopoverComponent {
    align = input<'left' | 'right'>('left');

    isOpen = signal(false);

    toggle() {
        this.isOpen.update(v => !v);
    }

    close() {
        this.isOpen.set(false);
    }

    @HostListener('document:keydown.escape')
    onEscape() {
        this.close();
    }
}
