import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface DialogueScriptSummary {
    id: string;
    name: string;
    projectId: string;
    createdAt: string;
    updatedAt: string;
    nodeCount: number;
}

export interface DialogueScriptDetail {
    id: string;
    name: string;
    projectId: string;
    graphJson: string;
    createdAt: string;
    updatedAt: string;
    languages: readonly string[];
}

/** One node of the graph — mirrors the server's GraphNode; the body is Yarn syntax. */
export interface DialogueNode {
    id: string;
    title: string;
    x: number;
    y: number;
    body: string;
}

export interface DialogueSaveAnswer {
    id: string;
    name: string;
    projectId: string;
    graphJson: string;
    createdAt: string;
    updatedAt: string;
}

export interface DialogueImportReport {
    added: number;
    updated: number;
    languages: readonly string[];
    unknownLines: number;
}

@Injectable({ providedIn: 'root' })
export class DialoguesService {
    private http = inject(HttpClient);

    list(projectId: string) {
        return firstValueFrom(this.http.get<DialogueScriptSummary[]>(`/api/projects/${projectId}/dialogues`));
    }

    create(projectId: string, name: string) {
        return firstValueFrom(this.http.post<DialogueSaveAnswer>(`/api/projects/${projectId}/dialogues`, { name }));
    }

    get(scriptId: string) {
        return firstValueFrom(this.http.get<DialogueScriptDetail>(`/api/dialogues/${scriptId}`));
    }

    save(scriptId: string, input: { name?: string; graphJson?: string }) {
        return firstValueFrom(this.http.put<DialogueSaveAnswer>(`/api/dialogues/${scriptId}`, input));
    }

    remove(scriptId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/dialogues/${scriptId}`));
    }

    importXlsx(scriptId: string, file: File) {
        const fd = new FormData();
        fd.append('file', file);
        return firstValueFrom(this.http.post<DialogueImportReport>(`/api/dialogues/${scriptId}/import/xlsx`, fd));
    }

    yarnUrl(scriptId: string): string {
        return `/api/dialogues/${scriptId}/export/yarn`;
    }

    xlsxUrl(scriptId: string, languages: string): string {
        const trimmed = languages.trim();
        return `/api/dialogues/${scriptId}/export/xlsx` + (trimmed ? `?languages=${encodeURIComponent(trimmed)}` : '');
    }
}
