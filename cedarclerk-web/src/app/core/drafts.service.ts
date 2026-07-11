import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DraftMeta {
    id: string;
    title: string;
    createdAt: string;
    updatedAt: string;
    blogSlug: string | null;
    isBlogPublished: boolean;
    blogPublishedAt: string | null;
    languages: string[]; // translation languages that exist ("en"), primary (RU) is implicit
    tags: string; // comma-separated lowercase tags, shared across language versions
}
export interface TranslationMeta { language: string; title: string; updatedAt: string; }
export interface TranslationFull extends TranslationMeta { cedarJson: string; }
export interface DraftFull extends DraftMeta { cedarJson: string; translations: TranslationMeta[]; }

@Injectable({ providedIn: 'root' })
export class DraftsService {
    private http = inject(HttpClient);

    list() { 
        return firstValueFrom(this.http.get<DraftMeta[]>('/api/drafts')); 
    }

    get(id: string) { 
        return firstValueFrom(this.http.get<DraftFull>(`/api/drafts/${id}`)); 
    }

    create(title: string, cedarJson: string) {
        return firstValueFrom(this.http.post<{ id: string }>('/api/drafts', { title, cedarJson }));
    }

    update(id: string, title: string, cedarJson: string) {
        return firstValueFrom(this.http.put(`/api/drafts/${id}`, { title, cedarJson }));
    }

    remove(id: string) {
         return firstValueFrom(this.http.delete(`/api/drafts/${id}`));
    }

    updateTags(id: string, tags: string) {
        return firstValueFrom(this.http.put<{ tags: string }>(`/api/drafts/${id}/tags`, { tags }));
    }

    getTranslation(id: string, lang: string) {
        return firstValueFrom(this.http.get<TranslationFull>(`/api/drafts/${id}/translations/${lang}`));
    }

    saveTranslation(id: string, lang: string, title: string, cedarJson: string) {
        return firstValueFrom(this.http.put<{ language: string; updatedAt: string }>(
            `/api/drafts/${id}/translations/${lang}`, { title, cedarJson }));
    }

    removeTranslation(id: string, lang: string) {
        return firstValueFrom(this.http.delete(`/api/drafts/${id}/translations/${lang}`));
    }

    autoTranslate(id: string, lang: string) {
        return firstValueFrom(this.http.post<TranslationFull>(`/api/drafts/${id}/translations/${lang}/auto`, {}));
    }

    importCedar(file: File) {
        const formData = new FormData();
        formData.append('file', file);
        return firstValueFrom(this.http.post<{ id: string }>('/api/drafts/import', formData));
    }

    publishToBlog(id: string) {
        return firstValueFrom(this.http.post<{ slug: string; url: string }>(`/api/drafts/${id}/publish-blog`, {}));
    }

    unpublishFromBlog(id: string) {
        return firstValueFrom(this.http.post(`/api/drafts/${id}/unpublish-blog`, {}));
    }
}