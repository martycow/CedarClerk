import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { SeriesMeta } from '../core/drafts.service';
import { SeriesService } from '../core/series.service';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { PopoverComponent } from './popover.component';
import { ModalComponent } from './modal.component';
import { IconComponent } from './icon.component';
import { ButtonComponent } from '../bench/forms/button.component';

// ADR-125 — the folder-picker pattern applied to series: the picker reports the choice (`picked`),
// persisting stays with the host; series management goes through SeriesService so every picker
// sees a new series immediately.
@Component({
    selector: 'app-series-picker',
    imports: [IconComponent, FormsModule, NgTemplateOutlet, PopoverComponent, ModalComponent, ButtonComponent],
    templateUrl: './series-picker.component.html',
    styleUrl: './series-picker.component.css',
})
export class SeriesPickerComponent {
    private seriesApi = inject(SeriesService);
    t = inject(LocaleService).t;

    seriesId = input<string | null>(null);
    compact = input(false);
    picked = output<string | null>();

    series = this.seriesApi.series;
    busy = signal(false);
    error = signal('');
    editingId = signal<string | null>(null);
    editingName = '';
    newName = '';
    deleteConfirmId = signal<string | null>(null);

    // Loaded up front: the trigger shows the series *name*, and a picker that must be opened
    // before it can say where the draft belongs is the IB6 bug again.
    ngOnInit() {
        this.load();
    }

    load() {
        this.seriesApi.ensureLoaded();
    }

    label(): string {
        const id = this.seriesId();
        if (id === null) return this.t().drafts.series.none;
        return this.seriesApi.find(id)?.name ?? (this.series().length ? this.t().drafts.series.none : '…');
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
            await this.seriesApi.create(name);
            this.newName = '';
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.createSeries));
        } finally {
            this.busy.set(false);
        }
    }

    startRename(s: SeriesMeta, ev: Event) {
        ev.stopPropagation();
        this.editingId.set(s.id);
        this.editingName = s.name;
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
            await this.seriesApi.rename(id, name);
            this.editingId.set(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.renameSeries));
        } finally {
            this.busy.set(false);
        }
    }

    askDelete(s: SeriesMeta, ev: Event) {
        ev.stopPropagation();
        this.deleteConfirmId.set(s.id);
    }

    deleteTarget(): SeriesMeta | null {
        const id = this.deleteConfirmId();
        return id ? this.series().find(s => s.id === id) ?? null : null;
    }

    async confirmDelete() {
        const id = this.deleteConfirmId();
        if (!id || this.busy()) return;
        this.busy.set(true);
        this.deleteConfirmId.set(null);
        try {
            await this.seriesApi.remove(id);
            // The server detached the members; tell the host so it stops naming a dead series.
            if (this.seriesId() === id) this.picked.emit(null);
        } catch (e) {
            this.error.set(httpErrorMessage(e, this.t().drafts.errors.deleteSeries));
        } finally {
            this.busy.set(false);
        }
    }
}
