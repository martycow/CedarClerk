import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Channel {
    id: string;
    title: string;
    telegramChatId: number;
}

@Injectable({ providedIn: 'root' })
export class ChannelsService {
    private http = inject(HttpClient);

    list() {
        return firstValueFrom(this.http.get<Channel[]>('/api/channels'));
    }

    connect(chatId: string) {
        return firstValueFrom(this.http.post<Channel>('/api/channels', { chatId }));
    }

    remove(id: string) {
        return firstValueFrom(this.http.delete(`/api/channels/${id}`));
    }
}
