import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// T-331 — three kinds over one row shape, mirroring CedarClerk.Core.PresetKinds. The kind decides
// which config record reads `configJson`; nothing else about a preset differs.
export type PresetKind = 'document' | 'project' | 'export';

export const PRESET_KINDS: PresetKind[] = ['document', 'project', 'export'];

export interface Preset {
    id: string;
    kind: PresetKind;
    name: string;
    configJson: string;
    sortOrder: number;
}

/** Mirrors CedarClerk.Core.DocumentPresetConfig. */
export interface DocumentPresetConfig {
    baseType: string;
    icon: string;
    headings: string[];
}

/** Mirrors CedarClerk.Core.ProjectPresetConfig. */
export interface ProjectPresetConfig {
    projectType: string;
    documentType: string | null;
    documentTitle: string | null;
    description: string;
}

/** Mirrors CedarClerk.Core.ExportPresetConfig. */
export interface ExportPresetConfig {
    destinations: string[];
    languages: string[];
}

export type PresetConfig = DocumentPresetConfig | ProjectPresetConfig | ExportPresetConfig;

export interface SavePresetInput {
    kind: PresetKind;
    name: string;
    config: PresetConfig;
}

export function parseDocumentConfig(json: string): DocumentPresetConfig {
    const c = read(json);
    return {
        baseType: str(c['baseType']) ?? 'post',
        icon: str(c['icon']) ?? 'file-text',
        headings: strings(c['headings']),
    };
}

export function parseProjectConfig(json: string): ProjectPresetConfig {
    const c = read(json);
    return {
        projectType: str(c['projectType']) ?? 'fullgame',
        documentType: str(c['documentType']) ?? null,
        documentTitle: str(c['documentTitle']) ?? null,
        description: str(c['description']) ?? '',
    };
}

export function parseExportConfig(json: string): ExportPresetConfig {
    const c = read(json);
    return { destinations: strings(c['destinations']), languages: strings(c['languages']) };
}

function read(json: string): Record<string, unknown> {
    try {
        const parsed = JSON.parse(json);
        return parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : {};
    } catch {
        return {};
    }
}

const str = (v: unknown) => (typeof v === 'string' && v.length ? v : null);
const strings = (v: unknown) => Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [];

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
