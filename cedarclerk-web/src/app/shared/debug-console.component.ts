import { Component, computed, inject, signal } from '@angular/core';
import { BenchDrawerComponent } from '../bench/chrome/bench-drawer.component';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';

const MAX_BODY_CHARS = 4000;

// The request/response journal, in the drawer under the bench — lets Marty see whether a slow
// publish is actually stuck or just working, and read the exact raw error body a failed request
// came back with, without SSH-ing into the server. Mounted once for the whole app, so it is
// available on every screen rather than only where a page thought to host it.
@Component({
    selector: 'app-debug-console',
    imports: [BenchDrawerComponent, IconComponent],
    templateUrl: './debug-console.component.html',
    styleUrl: './debug-console.component.css',
})
export class DebugConsoleComponent {
    log = inject(DebugLogService);
    t = inject(LocaleService).t;
    expandedId = signal<number | null>(null);

    entries = this.log.entries;
    inFlightCount = this.log.inFlightCount;
    errorCount = computed(() => this.log.errorCount());
    open = this.log.open;

    // What the lip says while the drawer is shut, and so the whole reason it can stay shut: how
    // much traffic there was, and whether any of it is still running or went wrong.
    summary = computed(() => {
        const t = this.t().debug;
        const parts = [t.requests(this.entries().length)];
        if (this.inFlightCount() > 0) parts.push(t.inFlight(this.inFlightCount()));
        if (this.errorCount() > 0) parts.push(t.errors(this.errorCount()));
        return parts.join(' · ');
    });

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
