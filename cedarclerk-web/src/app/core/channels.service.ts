import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Channel {
    id: string;
    title: string;
    telegramChatId: number;
    username: string | null;
}

export interface ChannelStatSnapshotDto {
    takenAt: string;
    memberCount: number;
}

export interface ChannelStats {
    current: number | null;
    deltaWeek: number | null;
    snapshots: ChannelStatSnapshotDto[];
}

export interface KnownChat {
    telegramChatId: number;
    title: string;
    username: string | null;
    type: string;
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

    getStats(id: string) {
        return firstValueFrom(this.http.get<ChannelStats>(`/api/channels/${id}/stats`));
    }

    listKnown() {
        return firstValueFrom(this.http.get<KnownChat[]>('/api/channels/known'));
    }

    refreshKnown() {
        return firstValueFrom(this.http.post<{ refreshed: number }>('/api/channels/refresh-known-chats', {}));
    }
}
