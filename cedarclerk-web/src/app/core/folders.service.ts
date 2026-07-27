import { Injectable, inject, signal } from '@angular/core';
import { DraftsService, FolderMeta } from './drafts.service';

// One folder list for the whole app (FI3.3). Every screen that lets you file a draft used to hold
// its own copy, so creating a folder in the editor left the drafts table and the posts manager
// showing a stale list until reload — and folders could only be renamed or deleted on /drafts.
// The list lives here instead, and mutating it here updates every picker at once.
@Injectable({ providedIn: 'root' })
export class FoldersService {
    private api = inject(DraftsService);

    folders = signal<FolderMeta[]>([]);
    private loaded = false;

    async ensureLoaded() {
        if (this.loaded) return;
        this.loaded = true;
        try {
            this.folders.set(await this.api.listFolders());
        } catch {
            this.loaded = false; // let the next opener retry
        }
    }

    async reload() {
        this.folders.set(await this.api.listFolders());
        this.loaded = true;
    }

    async create(name: string) {
        const created = await this.api.createFolder(name);
        this.folders.update(list => [...list, { ...created, count: 0 }].sort(byName));
        return created;
    }

    async rename(id: string, name: string) {
        const renamed = await this.api.renameFolder(id, name);
        this.folders.update(list => list.map(f => f.id === id ? { ...f, name: renamed.name } : f).sort(byName));
        return renamed;
    }

    async remove(id: string) {
        await this.api.deleteFolder(id);
        this.folders.update(list => list.filter(f => f.id !== id));
    }

    find(id: string | null): FolderMeta | undefined {
        return id === null ? undefined : this.folders().find(f => f.id === id);
    }
}

function byName(a: FolderMeta, b: FolderMeta) {
    return a.name.localeCompare(b.name);
}
