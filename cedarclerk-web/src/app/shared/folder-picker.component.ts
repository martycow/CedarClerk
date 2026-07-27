import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { FolderMeta } from '../core/drafts.service';
import { FoldersService } from '../core/folders.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { PopoverComponent } from './popover.component';
import { ModalComponent } from './modal.component';
import {
    LucideFolder as Folder, LucidePencil as Pencil,
    LucidePlus as Plus, LucideX as X, LucideTrash2 as Trash2,
} from '@lucide/angular';

// FI3.3 — one folder control for every screen: the editor, the new-draft dialog, the drafts table
// and the posts manager. Before this, filing a draft looked different on each of them and folders
// could only be created, renamed or deleted on /drafts, so the ask was less "make it pretty" than
// "make the same thing reachable from everywhere".
//
// The picker only reports the chosen folder (`picked`); persisting the assignment stays with the
// owner of the draft, since each screen writes it differently (one draft, a table row, a
// not-yet-created draft). Folder *management* is the picker's own job — it goes through
// FoldersService, so a folder created here shows up in every other picker immediately.
@Component({
    selector: 'app-folder-picker',
    imports: [FormsModule, NgTemplateOutlet, PopoverComponent, ModalComponent, Folder, Pencil, Plus, X, Trash2],
    templateUrl: './folder-picker.component.html',
    styleUrl: './folder-picker.component.css',
})
export class FolderPickerComponent {
    private foldersApi = inject(FoldersService);
    t = inject(LocaleService).t;

    folderId = input<string | null>(null);
    // Table cells and toolbars want the label without a border; standalone fields want a button.
    compact = input(false);
    // Renders the menu straight into the page instead of behind a popover trigger.
    inline = input(false);
    picked = output<string | null>();

    folders = this.foldersApi.folders;
    busy = signal(false);
    error = signal('');
    editingId = signal<string | null>(null);
    editingName = '';
    newName = '';
    deleteConfirmId = signal<string | null>(null);

    // Loaded up front rather than on the trigger click: the trigger shows the folder's *name*,
    // and a picker that has to be opened before it can say where the draft is filed is exactly
    // the "reads as unfiled until clicked" bug IB6 was about. The list is cached in the service,
    // so a table full of pickers still costs one request.
    ngOnInit() {
        this.load();
    }

    load() {
        this.foldersApi.ensureLoaded();
    }

    label(): string {
        const id = this.folderId();
        if (id === null) return this.t().drafts.folders.none;
        // A draft can be in a folder the list hasn't arrived for yet — claiming "no folder" there
        // is a lie the editor already got bitten by (IB6).
        return this.foldersApi.find(id)?.name ?? (this.folders().length ? this.t().drafts.folders.none : '…');
    }

    pick(id: string | null) {
        this.picked.emit(id);
    }

    async add() {
        const name = this.newName.trim();
        if (!name || this.busy()) return;
        this.busy.set(true);
        this.error.set('');
        try {
            await this.foldersApi.create(name);
            this.newName = '';
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.createFolder));
        } finally {
            this.busy.set(false);
        }
    }

    startRename(f: FolderMeta, ev: Event) {
        ev.stopPropagation();
        this.editingId.set(f.id);
        this.editingName = f.name;
    }

    cancelRename() {
        this.editingId.set(null);
    }

    async commitRename() {
        const id = this.editingId();
        const name = this.editingName.trim();
        if (!id || !name || this.busy()) { this.editingId.set(null); return; }
        this.busy.set(true);
        this.error.set('');
        try {
            await this.foldersApi.rename(id, name);
            this.editingId.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.renameFolder));
        } finally {
            this.busy.set(false);
        }
    }

    askDelete(f: FolderMeta, ev: Event) {
        ev.stopPropagation();
        this.deleteConfirmId.set(f.id);
    }

    deleteTarget(): FolderMeta | null {
        const id = this.deleteConfirmId();
        return id ? this.folders().find(f => f.id === id) ?? null : null;
    }

    async confirmDelete() {
        const id = this.deleteConfirmId();
        if (!id || this.busy()) return;
        this.busy.set(true);
        this.deleteConfirmId.set(null);
        try {
            await this.foldersApi.remove(id);
            // The deleted folder's drafts become unfiled server-side; tell the host so the row it
            // is showing stops naming a folder that no longer exists.
            if (this.folderId() === id) this.picked.emit(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.deleteFolder));
        } finally {
            this.busy.set(false);
        }
    }
}
