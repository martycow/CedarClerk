import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AssetsService {
    private http = inject(HttpClient);

    upload(file: File) {
        const fd = new FormData();
        fd.append('file', file);
        return firstValueFrom(this.http.post<{ id: string; url: string }>('/api/assets', fd));
    }
}