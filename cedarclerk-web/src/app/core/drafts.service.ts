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
}
export interface DraftFull extends DraftMeta { cedarJson: string; }

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