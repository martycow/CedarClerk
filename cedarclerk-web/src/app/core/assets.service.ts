import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AssetsService {
    private http = inject(HttpClient);

    uploadWithProgress(file: File): Observable<HttpEvent<{ id: string; url: string }>> {
        const fd = new FormData();
        fd.append('file', file);
        return this.http.post<{ id: string; url: string }>('/api/assets', fd, {
            reportProgress: true,
            observe: 'events',
        });
    }
}