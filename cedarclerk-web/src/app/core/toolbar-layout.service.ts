import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';
import { DEFAULT_TOOLBAR_LAYOUT, ToolbarButtonId, ToolbarLayout, parseToolbarLayout, normalizeRows } from './toolbar-layout';

@Injectable({ providedIn: 'root' })
export class ToolbarLayoutService {
    private auth = inject(AuthService);
    readonly layout = signal<ToolbarLayout>(DEFAULT_TOOLBAR_LAYOUT);

    // Idempotent — safe to call on every authGuard pass, not just the first one.
    loadFromAuth() {
        this.layout.set(parseToolbarLayout(this.auth.toolbarLayoutJson()));
    }

    async save(next: ToolbarLayout): Promise<void> {
        this.layout.set(next);
        await this.auth.saveToolbarLayout(JSON.stringify(next));
    }

    isHidden(id: ToolbarButtonId): boolean {
        return this.layout().hiddenButtons.includes(id);
    }

    // Ordered, so the editor renders groups in the sequence the panel shows — normalized on read
    // as well as on parse, since save() writes whatever the caller hands it.
    row1Ordered(): string[] {
        const l = this.layout();
        return normalizeRows(l.row1Groups, l.row2Groups).row1Groups;
    }

    row2Ordered(): string[] {
        const l = this.layout();
        return normalizeRows(l.row1Groups, l.row2Groups).row2Groups;
    }
}
