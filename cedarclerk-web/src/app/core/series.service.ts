import { Injectable, inject, signal } from '@angular/core';
import { DraftsService, SeriesMeta } from './drafts.service';

// One series list for the whole app, same shape as FoldersService (FI3.3): mutating it here
// updates every picker at once.
@Injectable({ providedIn: 'root' })
export class SeriesService {
    private api = inject(DraftsService);

    series = signal<SeriesMeta[]>([]);
    private loaded = false;

    async ensureLoaded() {
        if (this.loaded) return;
        this.loaded = true;
        try {
            this.series.set(await this.api.listSeries());
        } catch {
            this.loaded = false; // let the next opener retry
        }
    }

    async create(name: string) {
        const created = await this.api.createSeries(name);
        this.series.update(list => [...list, { ...created, count: 0 }].sort(byName));
        return created;
    }

    async rename(id: string, name: string) {
        const renamed = await this.api.renameSeries(id, name);
        this.series.update(list => list.map(s => s.id === id ? { ...s, name: renamed.name } : s).sort(byName));
        return renamed;
    }

    async remove(id: string) {
        await this.api.deleteSeries(id);
        this.series.update(list => list.filter(s => s.id !== id));
    }

    find(id: string | null): SeriesMeta | undefined {
        return id === null ? undefined : this.series().find(s => s.id === id);
    }
}

function byName(a: SeriesMeta, b: SeriesMeta) {
    return a.name.localeCompare(b.name);
}
