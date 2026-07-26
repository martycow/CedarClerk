import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from './auth.service';
import { DEFAULT_TOOLBAR_LAYOUT, ToolbarButtonId, ToolbarLayout, parseToolbarLayout } from './toolbar-layout';

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

    isRow2(groupId: string): boolean {
        return this.layout().row2Groups.includes(groupId);
    }
}
