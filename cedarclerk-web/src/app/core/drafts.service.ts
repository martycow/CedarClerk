import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DraftMeta {
    id: string;
    title: string;
    createdAtUtc: string;
    updatedAtUtc: string;
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
}