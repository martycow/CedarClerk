import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { TagUsageService } from '../core/tag-usage.service';
import { LocaleService } from '../core/i18n/locale.service';
import { PopoverComponent } from './popover.component';

const MOST_USED_COUNT = 8;

// FI3.2 — the editor's tag cloud, extracted so the posts manager and the new-draft dialog use the
// same control instead of each having its own take on "chips plus a text field". Selection is
// owned by the host (`tags` in, `tagsChange` out): the editor saves per change, the posts manager
// saves on its Save button, and the new-draft dialog has nothing to save to yet.
@Component({
    selector: 'app-tag-picker',
    imports: [FormsModule, NgTemplateOutlet, PopoverComponent],
    templateUrl: './tag-picker.component.html',
    styleUrl: './tag-picker.component.css',
})
export class TagPickerComponent {
    private usageApi = inject(TagUsageService);
    t = inject(LocaleService).t;

    tags = input<string[]>([]);
    // Renders the cloud straight into the page instead of behind a "+ Tags" popover trigger.
    inline = input(false);
    tagsChange = output<string[]>();

    usage = this.usageApi.usage;
    newTag = signal('');

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

    // Commas separate tags in storage, so one typed into a tag name would silently split it.
    add() {
        const tag = this.newTag().trim().replace(/,/g, '');
        if (!tag || this.has(tag)) { this.newTag.set(''); return; }
        this.tagsChange.emit([...this.tags(), tag]);
        this.newTag.set('');
    }
}
