import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Channel {
    id: string;
    title: string;
    telegramChatId: number;
    username: string | null;
    /** The channel's own picture, copied down from Telegram; null until the bot has fetched one. */
    avatarUrl: string | null;
}

export interface ChannelStatSnapshotDto {
    takenAt: string;
    memberCount: number;
    viewCount: number;
    likeCount: number;
    commentCount: number;
}

export interface ChannelStats {
    current: number | null;
    deltaWeek: number | null;
    currentViews: number | null;
    deltaWeekViews: number | null;
    currentLikes: number | null;
    deltaWeekLikes: number | null;
    currentComments: number | null;
    deltaWeekComments: number | null;
    snapshots: ChannelStatSnapshotDto[];
}

export interface BlogStatSnapshotDto {
    takenAt: string;
    viewCount: number;
    likeCount: number;
    commentCount: number;
}

// Views summed over the selected range, split by reader country / reader language. '??' is the
// server's bucket for "couldn't tell" — a real share of the audience, not a missing row.
export interface AudienceSlice {
    code: string;
    views: number;
}

export interface BlogStats {
    currentViews: number | null;
    deltaWeekViews: number | null;
    currentLikes: number | null;
    deltaWeekLikes: number | null;
    currentComments: number | null;
    deltaWeekComments: number | null;
    snapshots: BlogStatSnapshotDto[];
    countries: AudienceSlice[];
    languages: AudienceSlice[];
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

    getStats(id: string, days?: number) {
        const query = days ? `?days=${days}` : '';
        return firstValueFrom(this.http.get<ChannelStats>(`/api/channels/${id}/stats${query}`));
    }

    getBlogStats(days?: number) {
        const query = days ? `?days=${days}` : '';
        return firstValueFrom(this.http.get<BlogStats>(`/api/blog/stats${query}`));
    }

    listKnown() {
        return firstValueFrom(this.http.get<KnownChat[]>('/api/channels/known'));
    }

    refreshKnown() {
        return firstValueFrom(this.http.post<{ refreshed: number }>('/api/channels/refresh-known-chats', {}));
    }
}
