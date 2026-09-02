import { Component, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';
import { LogLineComponent } from '../bench/worktop/log-line.component';
import { OverlayCoordinatorService, OverlayLayerLease } from '../core/overlay-coordinator.service';

const MAX_BODY_CHARS = 4000;

// The request/response journal as an overlay over the page (ADR-239 clause 11): opened from the
// account menu or Ctrl+`, it reserves no height and draws no count anywhere while shut. Lets the
// maintainer see whether a slow publish is stuck and read the raw error body without SSH.
@Component({
    selector: 'app-debug-console',
    imports: [IconComponent, LogLineComponent],
    host: { '(document:keydown)': 'onKeydown($event)' },
    templateUrl: './debug-console.component.html',
    styleUrl: './debug-console.component.css',
})
export class DebugConsoleComponent implements OnDestroy {
    log = inject(DebugLogService);
    private overlays = inject(OverlayCoordinatorService);
    private host = inject(ElementRef<HTMLElement>);
    t = inject(LocaleService).t;
    expandedId = signal<number | null>(null);

    entries = this.log.entries;
    open = computed(() => this.overlays.active() === 'debug');
    private panel = viewChild<ElementRef<HTMLElement>>('panel');
    private layer?: OverlayLayerLease;
    private wasOpen = false;

    constructor() {
        effect(() => {
            const open = this.open();
            if (open && !this.wasOpen) this.startOpen();
            else if (this.wasOpen && !open) this.finishClose();
            this.wasOpen = open;
        });
    }

    close() {
        this.overlays.close('debug');
    }

    ngOnDestroy(): void {
        this.layer?.release();
    }

    onKeydown(event: KeyboardEvent): void {
        if (!this.open() || !this.layer?.isTop()) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopImmediatePropagation();
            this.close();
        } else if (event.key === 'Tab') {
            const panel = this.panel()?.nativeElement;
            if (panel) this.overlays.trapTab(panel, event);
        }
    }

    toggleExpand(id: number) {
        this.expandedId.update(cur => cur === id ? null : id);
    }

    clear() {
        this.log.clear();
        this.expandedId.set(null);
    }

    formatBody(value: unknown): string {
        if (value === undefined) return '';
        if (value === null) return 'null';
        let text: string;
        try {
            text = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
        } catch {
            text = String(value);
        }
        return text.length > MAX_BODY_CHARS ? text.slice(0, MAX_BODY_CHARS) + '\n… (truncated)' : text;
    }

    private startOpen(): void {
        this.layer = this.overlays.registerLayer(this.host.nativeElement, 'debug');
        queueMicrotask(() => {
            if (!this.open()) return;
            const panel = this.panel()?.nativeElement;
            if (panel) this.overlays.focusFirst(panel);
        });
    }

    private finishClose(): void {
        this.layer?.release();
        this.layer = undefined;
    }
}
