import { Component, computed, inject, signal } from '@angular/core';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import {
    LucideTerminal as Terminal, LucideX as X,
    LucideRefreshCw as RefreshCw, LucideTrash2 as Trash2,
} from '@lucide/angular';

const MAX_BODY_CHARS = 4000;

// Full-width, collapsible request/response console pinned to the bottom of the viewport — lets
// Marty see whether a slow publish is actually stuck or just working, and inspect the exact raw
// error body a failed request came back with, without SSH-ing into the Pi. Mounted once in the
// root app shell (app.html) so it's available on every page, not just the editor.
@Component({
    selector: 'app-debug-console',
    imports: [Terminal, X, RefreshCw, Trash2],
    templateUrl: './debug-console.component.html',
    styleUrl: './debug-console.component.css',
})
export class DebugConsoleComponent {
    log = inject(DebugLogService);
    t = inject(LocaleService).t;
    open = signal(false);
    expandedId = signal<number | null>(null);

    entries = this.log.entries;
    inFlightCount = this.log.inFlightCount;
    errorCount = computed(() => this.log.errorCount());

    toggleOpen() {
        this.open.update(v => !v);
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
}
