import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { TagUsageService } from '../core/tag-usage.service';
import { LocaleService } from '../core/i18n/locale.service';
import { PopoverComponent } from './popover.component';
import { ModalComponent } from './modal.component';
import { LucidePencil as Pencil, LucideX as X, LucideTrash2 as Trash2 } from '@lucide/angular';

const MOST_USED_COUNT = 8;

// FI3.2 — the editor's tag cloud, extracted so the posts manager and the new-draft dialog use the
// same control instead of each having its own take on "chips plus a text field". Selection is
// owned by the host (`tags` in, `tagsChange` out): the editor saves per change, the posts manager
// saves on its Save button, and the new-draft dialog has nothing to save to yet.
@Component({
    selector: 'app-tag-picker',
    imports: [FormsModule, NgTemplateOutlet, PopoverComponent, ModalComponent, Pencil, X, Trash2],
    templateUrl: './tag-picker.component.html',
    styleUrl: './tag-picker.component.css',
})
export class TagPickerComponent {
    private usageApi = inject(TagUsageService);
    t = inject(LocaleService).t;

    tags = input<string[]>([]);
    // Idea #3 - renaming and deleting a tag across every draft. Off by default: on a per-draft
    // picker "remove" has to keep meaning "take it off this draft", not "delete it everywhere".
    manage = input(false);
    // Renders the cloud straight into the page instead of behind a "+ Tags" popover trigger.
    inline = input(false);
    tagsChange = output<string[]>();

    usage = this.usageApi.usage;
    newTag = signal('');
    editingTag = signal<string | null>(null);
    editingName = '';
    confirmDeleteTag = signal<string | null>(null);
    busy = signal(false);

    ngOnInit() {
        if (this.inline()) this.load();
    }

    load() {
        this.usageApi.ensureLoaded();
    }

    mostUsed() {
        return this.usage().slice(0, MOST_USED_COUNT);
    }

    rest() {
        return this.usage().slice(MOST_USED_COUNT);
    }

    has(tag: string) {
        return this.tags().includes(tag);
    }

    toggle(tag: string) {
        this.tagsChange.emit(this.has(tag) ? this.tags().filter(x => x !== tag) : [...this.tags(), tag]);
    }

    remove(tag: string) {
        this.tagsChange.emit(this.tags().filter(x => x !== tag));
    }

    startRename(tag: string, ev: Event) {
        ev.stopPropagation();
        this.editingTag.set(tag);
        this.editingName = tag;
    }

    cancelRename() {
        this.editingTag.set(null);
    }

    async commitRename() {
        const from = this.editingTag();
        const to = this.editingName.trim().toLowerCase().replace(/,/g, '');
        this.editingTag.set(null);
        if (!from || !to || from === to || this.busy()) return;
        this.busy.set(true);
        try {
            await this.usageApi.rename(from, to);
            // The open draft may itself carry the renamed tag; keep what it shows in step with
            // what was just written everywhere else.
            if (this.has(from)) this.tagsChange.emit(this.tags().map(t => t === from ? to : t));
        } finally {
            this.busy.set(false);
        }
    }

    askDelete(tag: string, ev: Event) {
        ev.stopPropagation();
        this.confirmDeleteTag.set(tag);
    }

    async confirmDelete() {
        const tag = this.confirmDeleteTag();
        this.confirmDeleteTag.set(null);
        if (!tag || this.busy()) return;
        this.busy.set(true);
        try {
            await this.usageApi.remove(tag);
            if (this.has(tag)) this.tagsChange.emit(this.tags().filter(t => t !== tag));
        } finally {
            this.busy.set(false);
        }
    }

    // Commas separate tags in storage, so one typed into a tag name would silently split it.
    add() {
        const tag = this.newTag().trim().replace(/,/g, '');
        if (!tag || this.has(tag)) { this.newTag.set(''); return; }
        this.tagsChange.emit([...this.tags(), tag]);
        this.newTag.set('');
    }
}
