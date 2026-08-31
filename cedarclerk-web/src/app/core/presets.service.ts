import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type PresetKind = 'document';

export interface Preset {
    id: string;
    kind: PresetKind;
    name: string;
    configJson: string;
    sortOrder: number;
}

// T-331/T-355 — a document preset's config, mirroring CedarClerk.Core.DocumentPresetConfig.
export interface DocumentPresetConfig {
    baseType: string;
    icon: string;
    headings: string[];
}

export interface SavePresetInput {
    kind: PresetKind;
    name: string;
    baseType: string;
    icon: string;
    headings: string[];
}

export function parseDocumentConfig(json: string): DocumentPresetConfig {
    try {
        const c = JSON.parse(json);
        return {
            baseType: c.baseType ?? 'post',
            icon: c.icon ?? 'file-text',
            headings: Array.isArray(c.headings) ? c.headings : [],
        };
    } catch {
        return { baseType: 'post', icon: 'file-text', headings: [] };
    }
}

@Injectable({ providedIn: 'root' })
export class PresetsService {
    private http = inject(HttpClient);

    list(kind: PresetKind) {
        return firstValueFrom(this.http.get<Preset[]>(`/api/presets?kind=${kind}`));
    }

    create(input: SavePresetInput) {
        return firstValueFrom(this.http.post<Preset>('/api/presets', input));
    }

    update(id: string, input: SavePresetInput) {
        return firstValueFrom(this.http.put<Preset>(`/api/presets/${id}`, input));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/presets/${id}`));
    }
}
