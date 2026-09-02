import { Component, inject, signal } from '@angular/core';
import { DebugLogService } from '../core/debug-log.service';
import { LocaleService } from '../core/i18n/locale.service';
import { IconComponent } from './icon.component';
import { LogLineComponent } from '../bench/worktop/log-line.component';

const MAX_BODY_CHARS = 4000;

// The request/response journal as an overlay over the page (ADR-239 clause 11): opened from the
// account menu or Ctrl+`, it reserves no height and draws no count anywhere while shut. Lets the
// maintainer see whether a slow publish is stuck and read the raw error body without SSH.
@Component({
    selector: 'app-debug-console',
    imports: [IconComponent, LogLineComponent],
    host: { '(document:keydown.escape)': 'onEscape()' },
    templateUrl: './debug-console.component.html',
    styleUrl: './debug-console.component.css',
})
export class DebugConsoleComponent {
    log = inject(DebugLogService);
    t = inject(LocaleService).t;
    expandedId = signal<number | null>(null);

    entries = this.log.entries;
    open = this.log.open;

    onEscape() {
        if (this.open()) this.open.set(false);
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
