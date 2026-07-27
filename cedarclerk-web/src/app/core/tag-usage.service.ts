import { Injectable, inject, signal } from '@angular/core';
import { DraftsService } from './drafts.service';

// Shared "tags already in use, most-used first" list behind every tag picker (FI3.2). Cached
// per session like the folder list: it only changes when a draft's tags are saved, and the count
// being a few minutes stale is invisible next to refetching it on every popover open.
@Injectable({ providedIn: 'root' })
export class TagUsageService {
    private api = inject(DraftsService);

    usage = signal<{ tag: string; count: number }[]>([]);
    private loaded = false;

    async ensureLoaded() {
        if (this.loaded) return;
        this.loaded = true;
        try {
            this.usage.set(await this.api.listTagUsage());
        } catch {
            this.loaded = false; // let the next opener retry
        }
    }

    // Idea #3 - renaming or deleting a tag rewrites every draft that carries it, so the cloud
    // is refetched rather than patched: the counts move too, not just the names.
    async rename(from: string, to: string) {
        await this.api.renameTag(from, to);
        await this.refresh();
    }

    async remove(tag: string) {
        await this.api.deleteTag(tag);
        await this.refresh();
    }

    // Called after tags are saved so a newly-invented tag is offered to the next draft rather
    // than only existing on the one it was typed into.
    async refresh() {
        try {
            this.usage.set(await this.api.listTagUsage());
            this.loaded = true;
        } catch {
            // A stale cloud is not worth surfacing an error for.
        }
    }
}
