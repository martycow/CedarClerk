import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Reusable registration-form definitions (N12). `formJson` holds the same blob shape as a
// draft's own form — applying a preset copies it onto the draft rather than linking to it, so
// editing a preset never rewrites a post that already uses it.
export interface FormPreset {
    id: string;
    name: string;
    formJson: string;
    createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class FormPresetsService {
    private http = inject(HttpClient);

    list() {
        return firstValueFrom(this.http.get<FormPreset[]>('/api/form-presets'));
    }

    create(name: string, formJson: string) {
        return firstValueFrom(this.http.post<FormPreset>('/api/form-presets', { name, formJson }));
    }

    update(id: string, name: string, formJson: string) {
        return firstValueFrom(this.http.put<FormPreset>(`/api/form-presets/${id}`, { name, formJson }));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/form-presets/${id}`));
    }
}
