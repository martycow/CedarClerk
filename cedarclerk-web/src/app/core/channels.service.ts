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
    // Wave 2 item 11 — the per-channel signature trio, carried verbatim by the list projection.
    // Optional so test stubs that predate the trio still construct a Channel.
    postSignature?: string | null;
    postSignatureTranslationsJson?: string | null;
    postSignatureUrl?: string | null;
}

/** Wave 2 item 12 — one aggregated hour of the channel's own posting history. Hours are UTC. */
export interface BestTimeSlot {
    hour: number;
    posts: number;
    avgReactions: number;
    avgComments: number;
}

/** Wave 2 item 13 — publishing cadence across every destination, bucketed by ISO week (UTC). */
export interface PublishingStats {
    currentStreakWeeks: number;
    longestStreakWeeks: number;
    weeks: { weekStartUtc: string; publishes: number }[];
}

/** Wave 2 item 15 — a named invite link with its daily-aggregate totals. Never a member log. */
export interface ChannelInviteLink {
    id: string;
    name: string;
    inviteLink: string;
    createdAt: string;
    revokedAt: string | null;
    joins: number;
    leaves: number;
    net: number;
}

/** The listing: named links plus the organic row — joins with no link, and every leave
 *  (Telegram never attributes a leave to a link). */
export interface ChannelInviteLinks {
    links: ChannelInviteLink[];
    organic: { joins: number; leaves: number };
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
    // Wave 2 item 13 — this channel's ChannelPost.PublishedAt values inside the window, for the
    // chart's publish-event markers. Optional until the server change lands.
    publishDates?: string[];
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

    /** Empty for a young channel — under two posts in every hour is the honest "no answer yet". */
    bestTimes(id: string) {
        return firstValueFrom(this.http.get<BestTimeSlot[]>(`/api/channels/${id}/best-times`));
    }

    publishingStats() {
        return firstValueFrom(this.http.get<PublishingStats>('/api/stats/publishing'));
    }

    listInviteLinks(id: string) {
        return firstValueFrom(this.http.get<ChannelInviteLinks>(`/api/channels/${id}/invite-links`));
    }

    /** 503 with a clear message when the bot is not running — the PostEndpoints pattern.
     *  Answers with the bare row; callers re-list for totals. */
    createInviteLink(id: string, name: string) {
        return firstValueFrom(this.http.post<{ id: string; name: string; inviteLink: string }>(
            `/api/channels/${id}/invite-links`, { name }));
    }

    /** Revokes on Telegram and stamps RevokedAt — the row stays in the table with its totals. */
    revokeInviteLink(id: string, linkId: string) {
        return firstValueFrom(this.http.delete<void>(`/api/channels/${id}/invite-links/${linkId}`));
    }

    /**
     * Wave 2 item 11 — the channel-level signature trio. A null/whitespace postSignature clears
     * the WHOLE trio server-side (URL and translations included); a non-blank one must send the
     * translations blob back too, because the PATCH replaces all three — omitting it wipes any
     * stored translations. Free tier setting a non-blank signature gets 403 { error }.
     */
    setSignature(id: string, postSignature: string, postSignatureUrl: string,
                 postSignatureTranslationsJson: string | null) {
        return firstValueFrom(this.http.patch<{
            postSignature: string | null;
            postSignatureTranslationsJson: string | null;
            postSignatureUrl: string | null;
        }>(`/api/channels/${id}/signature`, { postSignature, postSignatureUrl, postSignatureTranslationsJson }));
    }
}
