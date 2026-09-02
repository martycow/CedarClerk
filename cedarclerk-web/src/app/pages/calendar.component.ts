import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '../core/i18n/locale.service';
import { PostsService, ScheduledPost } from '../core/posts.service';
import { CalendarService } from '../core/calendar.service';
import { QueueService, QueueSlot } from '../core/queue.service';
import { PublishService } from '../core/publish.service';
import { DraftsService, DraftMeta } from '../core/drafts.service';
import { httpErrorMessage } from '../core/http-error.util';
import { IconComponent } from '../shared/icon.component';
import { IconName } from '../shared/icon-data.generated';
import { ModalComponent } from '../shared/modal.component';
import { HeaderMeta, PageHeaderComponent } from '../shell/page-header.component';
import { EmptyStateComponent } from '../shell/empty-state.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { seriesColor } from '../bench/worktop/growth-chart.component';
import { dayInZone, displayTimeZone, partsInZone, timeInZone, wallClockToInstant } from '../core/display-time';

// ADR-244: the server stores and serves UTC only; this page groups, labels and edits in the
// account's display timezone. Month and Week are two projections over the same tickets (T-322).
type CalendarView = 'month' | 'week';

export interface CalendarTicket {
    kind: 'post' | 'slot';
    /** Local calendar day, yyyy-MM-dd. */
    day: string;
    /** Local wall-clock HH:mm. */
    time: string;
    title: string;
    network: string;
    /** ScheduledPost id for a post ticket; QueueSlot id for a slot occurrence. */
    id: string;
    draftId?: string;
    status?: ScheduledPost['status'];
    sortKey: string;
}

interface CalendarCell {
    day: string;
    date: number;
    inMonth: boolean;
    isToday: boolean;
    tickets: CalendarTicket[];
}

/** One entity, one colour (ADR-158): every surface drawing a network reads this map. */
const NETWORK_SLOTS: Record<string, 1 | 2 | 3 | 4 | 5 | 6> = {
    telegram: 1, blog: 2, bluesky: 3, discord: 4, x: 5,
};

/** The chip names its network by icon, never by colour alone (ADR-149). */
const NETWORK_ICONS: Record<string, IconName> = {
    telegram: 'paper-plane-tilt', blog: 'globe', bluesky: 'cloud', discord: 'chat-teardrop-dots', x: 'at',
};

export function networkColor(network: string): string {
    return seriesColor(NETWORK_SLOTS[network] ?? 6);
}

const pad = (n: number) => String(n).padStart(2, '0');

/** A calendar date carried as a UTC Date so browser locale never moves its day. */
function civilDate(day: string): Date {
    const [year, month, date] = day.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, date));
}

function civilDay(date: Date): string {
    return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

function addCivilDays(date: Date, days: number): Date {
    return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + days));
}

/** SQLite stores no DateTimeKind; a value without an offset is UTC in fact (ADR-115). */
function utcDate(iso: string): Date {
    return new Date(/Z|[+-]\d{2}:?\d{2}$/.test(iso) ? iso : iso + 'Z');
}

// Calendar.png (ADR-239): the month is the page title, the networks and counts its meta line, the
// view strip and the month navigation beside the one Schedule action; the grid is a card whose
// rows stretch to the viewport, with the queue as a second card beside it when it is open.
@Component({
    selector: 'app-calendar',
    imports: [FormsModule, IconComponent, ModalComponent, ButtonComponent, PageHeaderComponent, EmptyStateComponent],
    templateUrl: 'calendar.component.html',
    styleUrls: ['calendar.component.css'],
})
export class CalendarComponent implements OnInit {
    t = inject(LocaleService).t;
    private locale = inject(LocaleService);
    private postsApi = inject(PostsService);
    private calendarApi = inject(CalendarService);
    private queueApi = inject(QueueService);
    private publishApi = inject(PublishService);
    private draftsApi = inject(DraftsService);
    private router = inject(Router);
    loading = signal(true);
    error = signal('');
    scheduled = signal<ScheduledPost[]>([]);
    slots = signal<QueueSlot[]>([]);
    view = signal<CalendarView>('month');
    /** A civil day in the account zone, represented by UTC fields only. */
    anchor = signal(civilDate(dayInZone(new Date())));
    draggingId = signal<string | null>(null);
    dragOverDay = signal<string | null>(null);
    rescheduleBusy = signal(false);

    async ngOnInit() {
        this.loading.set(true);
        try {
            const [posts, slots] = await Promise.allSettled([this.postsApi.listScheduled(), this.queueApi.list()]);
            if (posts.status === 'fulfilled') this.scheduled.set(posts.value);
            else this.error.set(httpErrorMessage(posts.reason, this.t().calendar.loadFailed));
            // Slots 404 until the server lane lands — the board still renders without them.
            if (slots.status === 'fulfilled') this.slots.set(slots.value);
        } finally {
            this.loading.set(false);
        }
    }

    // ─── Month grid ───────────────────────────────────────────────────────────────────────────

    periodLabel = computed(() => {
        const lang = this.locale.uiLang();
        if (this.view() === 'month') {
            return this.anchor().toLocaleDateString(lang, {
                timeZone: 'UTC', month: 'long', year: 'numeric',
            });
        }
        const { start, end } = this.visibleRange();
        const startLabel = start.toLocaleDateString(lang, { timeZone: 'UTC', day: 'numeric', month: 'short' });
        const endLabel = end.toLocaleDateString(lang, {
            timeZone: 'UTC', day: 'numeric', month: 'short', year: 'numeric',
        });
        return `${startLabel} – ${endLabel}`;
    });

    /** Monday-first weekday captions in the UI language, matching the frozen design. */
    dowLabels = computed(() => {
        const lang = this.locale.uiLang();
        // 2026-06-01 is a Monday — a fixed anchor keeps the loop locale-independent.
        return Array.from({ length: 7 }, (_, i) =>
            new Date(Date.UTC(2026, 5, 1 + i)).toLocaleDateString(lang, { timeZone: 'UTC', weekday: 'short' }));
    });

    private postTickets = computed<Map<string, CalendarTicket[]>>(() => {
        const byDay = new Map<string, CalendarTicket[]>();
        for (const p of this.scheduled()) {
            const at = utcDate(p.scheduledAtUtc);
            const day = dayInZone(at);
            const ticket: CalendarTicket = {
                kind: 'post',
                day,
                time: timeInZone(at),
                title: p.draftTitle,
                network: p.network || 'telegram',
                id: p.id,
                draftId: p.draftId,
                status: p.status,
                sortKey: at.toISOString(),
            };
            byDay.set(day, [...(byDay.get(day) ?? []), ticket]);
        }
        return byDay;
    });

    /**
     * Weekly slots expanded into per-day occurrences over the visible range — future days only,
     * and an occurrence is hidden when a ScheduledPost with that SlotId already sits on that day
     * (the job filled it; the ticket turned paper).
     */
    private slotTickets = computed<Map<string, CalendarTicket[]>>(() => {
        const byDay = new Map<string, CalendarTicket[]>();
        const active = this.slots().filter(s => s.isActive);
        if (!active.length) return byDay;

        const filled = new Set(
            this.scheduled().filter(p => p.slotId).map(p => `${p.slotId}:${dayInZone(utcDate(p.scheduledAtUtc))}`));
        const today = dayInZone(new Date());
        const { start, end } = this.visibleRange();
        // Walk the range in UTC days: a slot names a UTC weekday and minute, and each match maps
        // to whatever local day that instant falls on.
        for (let utc = Date.UTC(start.getUTCFullYear(), start.getUTCMonth(), start.getUTCDate() - 1);
             utc <= Date.UTC(end.getUTCFullYear(), end.getUTCMonth(), end.getUTCDate() + 1);
             utc += 86_400_000) {
            const probe = new Date(utc);
            for (const slot of active) {
                if (probe.getUTCDay() !== slot.dayOfWeek) continue;
                const instant = new Date(utc + slot.timeUtcMinutes * 60_000);
                const day = dayInZone(instant);
                if (day < today) continue;
                if (filled.has(`${slot.id}:${day}`)) continue;
                const ticket: CalendarTicket = {
                    kind: 'slot',
                    day,
                    time: timeInZone(instant),
                    title: slot.name || slot.targetName,
                    network: slot.network,
                    id: slot.id,
                    sortKey: instant.toISOString(),
                };
                byDay.set(day, [...(byDay.get(day) ?? []), ticket]);
            }
        }
        return byDay;
    });

    /** The selected week, or Monday-to-Sunday rows covering the selected month. */
    private visibleRange(): { start: Date; end: Date } {
        const anchor = this.anchor();
        if (this.view() === 'week') {
            const lead = (anchor.getUTCDay() + 6) % 7;
            const start = addCivilDays(anchor, -lead);
            return { start, end: addCivilDays(start, 6) };
        }
        const monthStart = new Date(Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth(), 1));
        const lead = (monthStart.getUTCDay() + 6) % 7;
        const start = addCivilDays(monthStart, -lead);
        const lastOfMonth = new Date(Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth() + 1, 0));
        const tail = (7 - lastOfMonth.getUTCDay()) % 7;
        const end = addCivilDays(lastOfMonth, tail);
        return { start, end };
    }

    cells = computed<CalendarCell[]>(() => {
        const { start, end } = this.visibleRange();
        const month = this.anchor().getUTCMonth();
        const today = dayInZone(new Date());
        const posts = this.postTickets();
        const slots = this.slotTickets();
        const out: CalendarCell[] = [];
        for (let d = new Date(start); d <= end; d = addCivilDays(d, 1)) {
            const day = civilDay(d);
            const tickets = [...(posts.get(day) ?? []), ...(slots.get(day) ?? [])]
                .sort((a, b) => a.sortKey.localeCompare(b.sortKey));
            out.push({
                day,
                date: d.getUTCDate(),
                inMonth: this.view() === 'week' || d.getUTCMonth() === month,
                isToday: day === today,
                tickets,
            });
        }
        return out;
    });

    /** The cells as rows, so each week is one grid row that shares the card's height. */
    weeks = computed<CalendarCell[][]>(() => {
        const cells = this.cells();
        const out: CalendarCell[][] = [];
        for (let i = 0; i < cells.length; i += 7) out.push(cells.slice(i, i + 7));
        return out;
    });

    isEmptyBoard = computed(() => !this.scheduled().length && !this.slots().length);

    /** Posts still to go out — the header's count, whatever month is on screen. */
    pendingCount = computed(() => this.scheduled().filter(p => p.status === 'Pending').length);

    /** Unfilled slot occurrences in the visible weeks. */
    openSlotCount = computed(() => {
        let n = 0;
        for (const tickets of this.slotTickets().values()) n += tickets.length;
        return n;
    });

    /** The legend — each network on the board behind its series dot — and the two counts. */
    headerMeta = computed<HeaderMeta[]>(() => {
        const meta: HeaderMeta[] = this.legendNetworks().map(n => ({ text: this.networkLabel(n), swatch: networkColor(n) }));
        meta.push({ text: this.t().calendar.scheduledCount(this.pendingCount()) });
        if (this.slots().length) meta.push({ text: this.t().calendar.openSlots(this.openSlotCount()) });
        meta.push({ text: this.t().calendar.timeZone(displayTimeZone()) });
        return meta;
    });

    setView(view: CalendarView) {
        this.view.set(view);
    }

    stepPeriod(delta: number) {
        const a = this.anchor();
        this.anchor.set(this.view() === 'month'
            ? new Date(Date.UTC(a.getUTCFullYear(), a.getUTCMonth() + delta, 1))
            : addCivilDays(a, delta * 7));
    }

    goToday() {
        this.anchor.set(civilDate(dayInZone(new Date())));
    }

    periodBackTitle = computed(() => this.view() === 'month' ? this.t().calendar.prevMonth : this.t().calendar.prevWeek);
    periodForwardTitle = computed(() => this.view() === 'month' ? this.t().calendar.nextMonth : this.t().calendar.nextWeek);
    networkColor = networkColor;

    networkIcon(network: string): IconName {
        return NETWORK_ICONS[network] ?? 'paper-plane-tilt';
    }

    /** Networks actually on the board, for the legend. */
    legendNetworks = computed(() => {
        const seen = new Set(this.scheduled().map(p => p.network || 'telegram'));
        return ['telegram', 'bluesky', 'discord', 'x', 'blog'].filter(n => seen.has(n));
    });

    networkLabel(network: string): string {
        const labels: Record<string, string> = { telegram: 'Telegram', bluesky: 'Bluesky', discord: 'Discord', x: 'X', blog: 'Blog' };
        return labels[network] ?? network;
    }

    // ─── Drag-to-day reschedule ───────────────────────────────────────────────────────────────

    onDragStart(ticket: CalendarTicket, ev: DragEvent) {
        if (ticket.kind !== 'post' || ticket.status !== 'Pending') { ev.preventDefault(); return; }
        this.draggingId.set(ticket.id);
        ev.dataTransfer?.setData('text/plain', ticket.id);
        if (ev.dataTransfer) ev.dataTransfer.effectAllowed = 'move';
    }

    onDragEnd() {
        this.draggingId.set(null);
        this.dragOverDay.set(null);
    }

    onDragOver(cell: CalendarCell, ev: DragEvent) {
        if (!this.draggingId()) return;
        ev.preventDefault();
        if (ev.dataTransfer) ev.dataTransfer.dropEffect = 'move';
        this.dragOverDay.set(cell.day);
    }

    async onDrop(cell: CalendarCell, ev: DragEvent) {
        ev.preventDefault();
        const id = this.draggingId();
        this.onDragEnd();
        if (!id || this.rescheduleBusy()) return;
        const post = this.scheduled().find(p => p.id === id);
        if (!post || post.status !== 'Pending') return;

        const at = utcDate(post.scheduledAtUtc);
        if (dayInZone(at) === cell.day) return;
        const next = wallClockToInstant(cell.day, timeInZone(at));
        if (!next) {
            this.error.set(this.t().calendar.invalidWallTime);
            return;
        }
        const nextUtc = next.toISOString();

        const before = this.scheduled();
        this.rescheduleBusy.set(true);
        this.error.set('');
        this.scheduled.update(list => list.map(p => p.id === id ? { ...p, scheduledAtUtc: nextUtc } : p));
        try {
            await this.calendarApi.reschedule(id, nextUtc);
        } catch (e) {
            this.scheduled.set(before);
            this.error.set(httpErrorMessage(e, this.t().calendar.rescheduleFailed));
        } finally {
            this.rescheduleBusy.set(false);
        }
    }

    // ─── Ticket click ─────────────────────────────────────────────────────────────────────────

    openTicket(ticket: CalendarTicket) {
        if (ticket.kind === 'slot') return;
        if (ticket.status === 'Sent') {
            this.router.navigate(['/posts'], { queryParams: { draft: ticket.draftId } });
        } else if (ticket.draftId) {
            this.router.navigate(['/editor'], { queryParams: { draft: ticket.draftId } });
        }
    }

    // ─── "+ Schedule" dialog ──────────────────────────────────────────────────────────────────

    scheduleOpen = signal(false);
    scheduleDrafts = signal<DraftMeta[]>([]);
    scheduleTargets = signal<{ id: string; network: string; name: string }[]>([]);
    scheduleDraftId = '';
    scheduleTargetId = '';
    scheduleAt = '';
    scheduleBusy = signal(false);
    scheduleError = signal('');

    async openScheduleDialog() {
        this.scheduleError.set('');
        this.scheduleDraftId = '';
        this.scheduleTargetId = '';
        const now = new Date(Date.now() + 3_600_000);
        this.scheduleAt = `${dayInZone(now)}T${timeInZone(now).slice(0, 2)}:00`;
        this.scheduleOpen.set(true);
        try {
            const [drafts, networks] = await Promise.all([this.draftsApi.list(), this.publishApi.networks()]);
            // Recent first, templates and archived out — the pool a schedule can honestly draw from.
            this.scheduleDrafts.set(drafts
                .filter(d => !d.isTemplate && !d.isArchived)
                .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))
                .slice(0, 30));
            const targets = networks.flatMap(n => n.accounts.map(a =>
                ({ id: a.id, network: n.network, name: a.displayName })));
            this.scheduleTargets.set(targets);
            this.scheduleDraftId = this.scheduleDrafts()[0]?.id ?? '';
            this.scheduleTargetId = targets[0]?.id ?? '';
        } catch (e) {
            this.scheduleError.set(httpErrorMessage(e, this.t().calendar.loadFailed));
        }
    }

    canSchedule(): boolean {
        return !!this.scheduleDraftId && !!this.scheduleTargetId && !!this.scheduleAt && !this.scheduleBusy();
    }

    async confirmSchedule() {
        if (!this.canSchedule()) return;
        const draft = this.scheduleDrafts().find(d => d.id === this.scheduleDraftId);
        if (!draft) return;
        const [day, time] = this.scheduleAt.split('T');
        const instant = wallClockToInstant(day, time);
        if (!instant) {
            this.scheduleError.set(this.t().calendar.invalidWallTime);
            return;
        }
        this.scheduleBusy.set(true);
        this.scheduleError.set('');
        try {
            await this.postsApi.schedule(
                draft.id, instant.toISOString(), draft.primaryLanguage,
                { targetId: this.scheduleTargetId });
            this.scheduled.set(await this.postsApi.listScheduled());
            this.scheduleOpen.set(false);
        } catch (e) {
            this.scheduleError.set(httpErrorMessage(e, this.t().calendar.scheduleFailed));
        } finally {
            this.scheduleBusy.set(false);
        }
    }

    // ─── Queue slots panel (item 10) ──────────────────────────────────────────────────────────

    queueOpen = signal(false);
    slotName = '';
    slotTargetId = '';
    slotCategory = '';
    slotDay = 1; // local weekday, .NET convention (0 = Sunday); Monday default
    slotTime = '18:00';
    slotBusy = signal(false);
    slotError = signal('');
    slotTargets = signal<{ id: string; network: string; name: string }[]>([]);

    async toggleQueue() {
        this.queueOpen.set(!this.queueOpen());
        if (this.queueOpen() && !this.slotTargets().length) {
            try {
                const networks = await this.publishApi.networks();
                const targets = networks.flatMap(n => n.accounts.map(a =>
                    ({ id: a.id, network: n.network, name: a.displayName })));
                this.slotTargets.set(targets);
                this.slotTargetId = targets[0]?.id ?? '';
            } catch { /* the create form simply has no destinations to offer */ }
        }
    }

    /** Local weekday choices, Monday first, mapped to the .NET 0=Sunday convention. */
    weekdayChoices = computed(() => {
        const labels = this.dowLabels();
        return Array.from({ length: 7 }, (_, i) => ({ value: (i + 1) % 7, label: labels[i] }));
    });

    /** The slot's local weekday + HH:mm, converted back from its stored UTC pair. */
    slotLocalLabel(slot: QueueSlot): string {
        const { day, time } = this.slotLocalParts(slot);
        return `${this.dowLabels()[(day + 6) % 7]} ${time}`;
    }

    private slotLocalParts(slot: QueueSlot): { day: number; time: string } {
        const { start } = this.visibleRange();
        for (let offset = -1; offset <= 7; offset++) {
            const utcDay = addCivilDays(start, offset);
            if (utcDay.getUTCDay() !== slot.dayOfWeek) continue;
            const instant = new Date(utcDay.getTime() + slot.timeUtcMinutes * 60_000);
            const parts = partsInZone(instant);
            if (!parts) break;
            const localDay = civilDate(`${parts.year}-${pad(parts.month)}-${pad(parts.day)}`);
            return { day: localDay.getUTCDay(), time: `${pad(parts.hour)}:${pad(parts.minute)}` };
        }
        return { day: slot.dayOfWeek, time: `${pad(Math.floor(slot.timeUtcMinutes / 60))}:${pad(slot.timeUtcMinutes % 60)}` };
    }

    async createSlot() {
        if (!this.slotTargetId || !this.slotTime || this.slotBusy()) return;
        const { start } = this.visibleRange();
        const localDow = this.slotDay === 0 ? 6 : this.slotDay - 1;
        const instant = wallClockToInstant(civilDay(addCivilDays(start, localDow)), this.slotTime);
        if (!instant) {
            this.slotError.set(this.t().calendar.invalidWallTime);
            return;
        }
        this.slotBusy.set(true);
        this.slotError.set('');
        try {
            await this.queueApi.create({
                targetId: this.slotTargetId,
                name: this.slotName.trim(),
                category: this.slotCategory.trim(),
                dayOfWeek: instant.getUTCDay(),
                timeUtcMinutes: instant.getUTCHours() * 60 + instant.getUTCMinutes(),
                isActive: true,
            });
            // The create answers with the id alone — the list is the projection.
            this.slots.set(await this.queueApi.list());
            this.slotName = '';
            this.slotCategory = '';
        } catch (e) {
            this.slotError.set(httpErrorMessage(e, this.t().calendar.queue.saveFailed));
        } finally {
            this.slotBusy.set(false);
        }
    }

    async toggleSlotActive(slot: QueueSlot) {
        if (this.slotBusy()) return;
        this.slotBusy.set(true);
        this.slotError.set('');
        try {
            await this.queueApi.update(slot.id, {
                targetId: slot.targetId, name: slot.name, category: slot.category,
                dayOfWeek: slot.dayOfWeek, timeUtcMinutes: slot.timeUtcMinutes, isActive: !slot.isActive,
            });
            this.slots.update(list => list.map(s => s.id === slot.id ? { ...s, isActive: !slot.isActive } : s));
        } catch (e) {
            this.slotError.set(httpErrorMessage(e, this.t().calendar.queue.saveFailed));
        } finally {
            this.slotBusy.set(false);
        }
    }

    async removeSlot(slot: QueueSlot) {
        if (this.slotBusy()) return;
        this.slotBusy.set(true);
        this.slotError.set('');
        try {
            await this.queueApi.remove(slot.id);
            this.slots.update(list => list.filter(s => s.id !== slot.id));
        } catch (e) {
            this.slotError.set(httpErrorMessage(e, this.t().calendar.queue.saveFailed));
        } finally {
            this.slotBusy.set(false);
        }
    }
}
