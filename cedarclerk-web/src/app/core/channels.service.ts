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

/** One row per (day, link), oldest first; `inviteLinkId: null` is the organic row. `day` is a UTC
 *  midnight serialised without an offset — key on its date prefix, never on Date parsing. */
export interface ChannelMemberFlowRow {
    day: string;
    inviteLinkId: string | null;
    joins: number;
    leaves: number;
}

export type StatMetricKey = 'memberCount' | 'viewCount' | 'likeCount' | 'commentCount';
export type StatSourceKind = 'blog' | 'channel' | 'target';
export type StatSourceNetwork = 'blog' | 'telegram' | 'x' | 'bluesky';

// Views summed over the selected range, split by reader country / reader language. '??' is the
// server's bucket for "couldn't tell" — a real share of the audience, not a missing row.
export interface AudienceSlice {
    code: string;
    views: number;
}

/** Every source the account has, selected or not — the leaf strip is built from this list, so a
 *  source is offered only once the server can name it (ADR-161 rule 4). `firstDay` is null until
 *  the first reading. */
export interface StatSourceInfo {
    id: string;
    kind: StatSourceKind;
    network: StatSourceNetwork;
    name: string;
    tracked: StatMetricKey[];
    firstDay: string | null;
}

/** One selected source with at least one reading. `values` are dense over `StatsSeries.days`,
 *  carried forward by the server; a metric the source does not track is null. `delta` is the
 *  window's last value minus its first — the only delta the tab shows. */
export interface StatSourceSeries {
    id: string;
    values: Partial<Record<StatMetricKey, number[] | null>>;
    current: Partial<Record<StatMetricKey, number>>;
    delta: Partial<Record<StatMetricKey, number>>;
    publishDays: string[];
}

/** `GET /api/stats/series` — every selected source over one aligned window (ADR-279). Day keys are
 *  calendar days in `zone`; the window starts at the latest first reading among the selected
 *  sources, so nothing before a source's first reading is ever invented. */
export interface StatsSeries {
    zone: string;
    requestedDays: number;
    days: string[];
    available: StatSourceInfo[];
    series: StatSourceSeries[];
    audience: { countries: AudienceSlice[]; languages: AudienceSlice[] };
}

export interface KnownChat {
    telegramChatId: number;
    title: string;
    username: string | null;
    type: string;
}

const seriesQuery = (days: number, sources: readonly string[], project?: string | null) =>
    `?days=${days}&sources=${sources.join(',')}${project ? `&project=${project}` : ''}`;

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

    /** Selection reaches the server: `sources` are the selected ids (`blog`, `channel:{id}`,
     *  `target:{id}`); unknown or unowned ids are silently omitted, never 404. */
    series(days: number, sources: readonly string[], project?: string | null) {
        return firstValueFrom(this.http.get<StatsSeries>(`/api/stats/series${seriesQuery(days, sources, project)}`));
    }

    /** The same matrix as `series()` as a file — an `<a download>` target, cookie-authenticated. */
    seriesCsvUrl(days: number, sources: readonly string[], project?: string | null) {
        return `/api/stats/series.csv${seriesQuery(days, sources, project)}`;
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

    publishingStats(project?: string | null) {
        return firstValueFrom(this.http.get<PublishingStats>('/api/stats/publishing', { params: project ? { project } : {} }));
    }

    listInviteLinks(id: string) {
        return firstValueFrom(this.http.get<ChannelInviteLinks>(`/api/channels/${id}/invite-links`));
    }

    /** Days with no events have no row — the caller fills the window with zeros. `days` is clamped 1..180 server-side. */
    memberFlow(id: string, days = 30) {
        return firstValueFrom(this.http.get<ChannelMemberFlowRow[]>(`/api/channels/${id}/member-flow?days=${days}`));
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
