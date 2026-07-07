import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class PostsService {
    private http = inject(HttpClient);

    export(draftId: string, chatId: string) {
        return firstValueFrom(this.http.post<{ messageId: number; chatId: string }>(
            '/api/posts/export', { draftId, chatId }));
    }
}