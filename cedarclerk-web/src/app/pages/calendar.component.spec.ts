import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CalendarComponent, networkColor } from './calendar.component';
import { PostsService, ScheduledPost } from '../core/posts.service';
import { CalendarService } from '../core/calendar.service';
import { QueueService, QueueSlot } from '../core/queue.service';
import { PublishEvent, PublishService } from '../core/publish.service';
import { Router } from '@angular/router';
import { DraftsService } from '../core/drafts.service';
import { dayInZone, setDisplayTimeZone, timeInZone, wallClockToInstant } from '../core/display-time';

const pad = (n: number) => String(n).padStart(2, '0');

function utcOfAccount(day: number, hour: number): { iso: string; day: string } {
    const today = dayInZone(new Date());
    const [year, month] = today.split('-').map(Number);
    const accountDay = `${year}-${pad(month)}-${pad(day)}`;
    const instant = wallClockToInstant(accountDay, `${pad(hour)}:00`)!;
    return { iso: instant.toISOString(), day: accountDay };
}

const PENDING = utcOfAccount(10, 18);
const SENT = utcOfAccount(3, 12);
const DIRECT = utcOfAccount(7, 9);

function post(overrides: Partial<ScheduledPost>): ScheduledPost {
    return {
        id: 'p1', draftId: 'd1', draftTitle: 'Devlog #43', chatId: '@c', scheduledAtUtc: PENDING.iso,
        status: 'Pending', error: null, messageId: null, format: 'Markdown', language: 'ru',
        channelTitle: null, targetId: 't1', network: 'telegram', targetName: 'Devlog',
        ...overrides,
    };
}

class PostsStub {
    posts: ScheduledPost[] = [post({}), post({ id: 'p2', status: 'Sent', scheduledAtUtc: SENT.iso, network: 'bluesky' })];
    listScheduled() { return Promise.resolve(this.posts); }
    schedule() { return Promise.resolve({ id: 'new' }); }
}

class CalendarStub {
    calls: { id: string; at: string }[] = [];
    reschedule(id: string, scheduledAtUtc: string) {
        this.calls.push({ id, at: scheduledAtUtc });
        return Promise.resolve({ id, scheduledAtUtc });
    }
}

class QueueStub {
    slots: QueueSlot[] = [];
    list() { return Promise.resolve(this.slots); }
}

function event(overrides: Partial<PublishEvent>): PublishEvent {
    return {
        draftId: 'd9', draftTitle: 'Mac Mini M6', network: 'telegram', targetName: 'Devlog',
        publishedAt: DIRECT.iso, publicUrl: 'https://t.me/devlog/9', partCount: 1, scheduled: false,
        ...overrides,
    };
}

class PublishStub {
    // One direct publication, and the send the Sent schedule above already stands for.
    list: PublishEvent[] = [event({}), event({ draftId: 'd1', draftTitle: 'Devlog #43', publishedAt: SENT.iso, network: 'bluesky', scheduled: true })];
    networks() { return Promise.resolve([]); }
    events() { return Promise.resolve({ events: this.list }); }
}
class DraftsStub { list() { return Promise.resolve([]); } }

describe('content calendar', () => {
    let fixture: ComponentFixture<CalendarComponent>;
    let calendarApi: CalendarStub;
    const page = () => fixture.componentInstance;

    async function settle() {
        fixture.detectChanges();
        await new Promise(resolve => setTimeout(resolve, 0));
        await fixture.whenStable();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        setDisplayTimeZone('America/Los_Angeles');
        calendarApi = new CalendarStub();
        TestBed.configureTestingModule({
            providers: [
                { provide: PostsService, useClass: PostsStub },
                { provide: CalendarService, useValue: calendarApi },
                { provide: QueueService, useClass: QueueStub },
                { provide: PublishService, useClass: PublishStub },
                { provide: DraftsService, useClass: DraftsStub },
            ],
        });
        fixture = TestBed.createComponent(CalendarComponent);
        await settle();
    });

    it('lands a scheduled post on its account day with an account wall-clock time', () => {
        const cell = page().cells().find(c => c.day === PENDING.day)!;
        expect(cell.tickets.map(t => t.id)).toContain('p1');
        expect(cell.tickets.find(t => t.id === 'p1')!.time).toBe('18:00');
    });

    // T-420 — a document published without a schedule is on the calendar on the day it went out.
    it('draws a directly published post on its day, and a scheduled send once', () => {
        const direct = page().cells().find(c => c.day === DIRECT.day)!.tickets;
        expect(direct.map(t => [t.kind, t.title, t.time])).toEqual([['published', 'Mac Mini M6', '09:00']]);

        const sentDay = page().cells().find(c => c.day === SENT.day)!.tickets;
        expect(sentDay.map(t => t.kind)).toEqual(['post']);
        expect(page().publishedCount()).toBe(2);
        expect(page().headerMeta().map(m => m.text)).toContain(page().t().calendar.publishedCount(2));
    });

    it('styles scheduled and published chips differently and marks the published one beyond colour', async () => {
        const root = fixture.nativeElement as HTMLElement;
        const chips = [...root.querySelectorAll<HTMLButtonElement>('button.chip')];
        const scheduled = chips.find(chip => chip.classList.contains('is-scheduled'))!;
        const published = chips.filter(chip => chip.classList.contains('is-published'));

        expect(scheduled.textContent).toContain('Devlog #43');
        expect(scheduled.querySelector('.chip-done')).toBeNull();
        expect(scheduled.getAttribute('draggable')).toBe('true');
        expect(published.length).toBe(2);
        expect(published.every(chip => chip.querySelector('.chip-done') && chip.getAttribute('draggable') === 'false')).toBe(true);

        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        published.find(chip => chip.textContent?.includes('Mac Mini M6'))!.click();
        expect(navigate).toHaveBeenCalledWith(['/posts'], { queryParams: { draft: 'd9' } });
    });

    it('covers the month with full Monday-to-Sunday weeks', () => {
        const cells = page().cells();
        expect(cells.length % 7).toBe(0);
        expect(cells.some(c => c.isToday)).toBe(true);
        const root = fixture.nativeElement as HTMLElement;
        expect(root.querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        expect(root.querySelector('.cal-layout')?.classList.contains('split-workspace')).toBe(false);
    });

    it('adds the shared main-inspector split only while the queue is open', () => {
        page().queueOpen.set(true);
        fixture.detectChanges();

        const layout = (fixture.nativeElement as HTMLElement).querySelector('.cal-layout')!;
        expect(layout.classList.contains('split-workspace')).toBe(true);
        expect(layout.classList.contains('is-main-inspector')).toBe(true);
    });

    it('a drop keeps the local time-of-day and writes the recomputed UTC back', async () => {
        page().draggingId.set('p1');
        const target = page().cells().find(c => c.inMonth && !c.tickets.length && c.day > PENDING.day)!;
        // A plain Event, not DragEvent — jsdom does not implement the drag constructor.
        await page().onDrop(target, new Event('drop') as DragEvent);

        expect(calendarApi.calls.length).toBe(1);
        const moved = new Date(calendarApi.calls[0].at);
        expect(timeInZone(moved)).toBe('18:00');
        const movedPost = page().scheduled().find(p => p.id === 'p1')!;
        expect(movedPost.scheduledAtUtc).toBe(calendarApi.calls[0].at);
    });

    it('opens rescheduling from a pending ticket full hit area and writes the chosen account time', async () => {
        const root = fixture.nativeElement as HTMLElement;
        const ticket = [...root.querySelectorAll<HTMLButtonElement>('button.chip')]
            .find(button => !button.classList.contains('is-published') && button.textContent?.includes('Devlog #43'))!;
        expect(ticket.getAttribute('aria-label')).toContain(page().t().calendar.rescheduleAction);

        ticket.click();
        fixture.detectChanges();
        expect(page().rescheduleTicket()?.id).toBe('p1');
        expect(root.querySelector('app-modal')?.textContent).toContain(page().t().calendar.rescheduleTitle);

        const target = page().cells().find(c => c.inMonth && c.day > PENDING.day)!;
        page().rescheduleAt = `${target.day}T09:30`;
        await page().confirmReschedule();
        fixture.detectChanges();

        expect(calendarApi.calls.at(-1)?.id).toBe('p1');
        expect(dayInZone(new Date(calendarApi.calls.at(-1)!.at))).toBe(target.day);
        expect(timeInZone(new Date(calendarApi.calls.at(-1)!.at))).toBe('09:30');
        expect(page().rescheduleTicket()).toBeNull();
        expect(root.querySelector('app-modal')).toBeNull();
    });

    it('never lets a sent ticket start a drag', () => {
        const ev = new Event('dragstart') as DragEvent;
        const sent = page().cells().flatMap(c => c.tickets).find(t => t.id === 'p2')!;
        page().onDragStart(sent, ev);
        expect(page().draggingId()).toBeNull();
    });

    it('hides a slot occurrence on a day a ScheduledPost already fills, and only that day', async () => {
        const at = new Date(PENDING.iso);
        const slot: QueueSlot = {
            id: 's1', targetId: 't1', targetName: 'Devlog', network: 'telegram',
            name: 'Saturday slot', category: '', dayOfWeek: at.getUTCDay(),
            timeUtcMinutes: at.getUTCHours() * 60 + at.getUTCMinutes(), isActive: true,
        };
        page().slots.set([slot]);
        page().scheduled.update(list => list.map(p => p.id === 'p1' ? { ...p, slotId: 's1' } : p));
        await settle();

        const filledDay = page().cells().find(c => c.day === PENDING.day)!;
        expect(filledDay.tickets.some(t => t.kind === 'slot')).toBe(false);

        // Next month every day is in the future and nothing is filled — the resin tickets show.
        page().stepPeriod(1);
        await settle();
        expect(page().cells().some(c => c.tickets.some(t => t.kind === 'slot'))).toBe(true);
    });

    it('counts only slot occurrences that land in visible cells', async () => {
        page().stepPeriod(2);
        await settle();
        const firstDay = page().cells()[0].day;
        const probe = new Date(`${firstDay}T00:00:00Z`);
        probe.setUTCDate(probe.getUTCDate() - 1);
        page().slots.set([{
            id: 'edge-slot', targetId: 't1', targetName: 'Devlog', network: 'telegram',
            name: 'Boundary slot', category: '', dayOfWeek: probe.getUTCDay(),
            timeUtcMinutes: 0, isActive: true,
        }]);
        await settle();

        const visible = page().cells().flatMap(cell => cell.tickets)
            .filter(ticket => ticket.kind === 'slot').length;
        expect(visible).toBeGreaterThan(0);
        expect(page().openSlotCount()).toBe(visible);
        expect(page().headerMeta().map(item => item.text)).toContain(page().t().calendar.openSlots(visible));
    });

    it('renders queue slots as information and gives the free-text fields visible labels', async () => {
        const at = new Date(PENDING.iso);
        page().slots.set([{
            id: 's1', targetId: 't1', targetName: 'Devlog', network: 'telegram',
            name: 'Evening slot', category: 'devlog', dayOfWeek: at.getUTCDay(),
            timeUtcMinutes: at.getUTCHours() * 60 + at.getUTCMinutes(), isActive: true,
        }]);
        page().queueOpen.set(true);
        await settle();

        const root = fixture.nativeElement as HTMLElement;
        const slotChip = root.querySelector('.chip.is-slot')!;
        expect(slotChip.tagName).toBe('DIV');
        expect(slotChip.closest('button')).toBeNull();
        expect(slotChip.getAttribute('tabindex')).toBeNull();

        const labels = [...root.querySelectorAll('.queue-form label.form-field')];
        expect(labels.map(label => label.querySelector('.form-field-label')?.textContent?.trim())).toEqual([
            page().t().calendar.queue.nameLabel,
            page().t().calendar.queue.destination,
            page().t().calendar.queue.weekday,
            page().t().calendar.queue.time,
            page().t().calendar.queue.categoryLabel,
        ]);
        expect(labels.every(label => !!label.querySelector('input, select'))).toBe(true);
    });

    it('switches to a seven-day week over the same tickets', async () => {
        page().anchor.set(new Date(`${PENDING.day}T00:00:00Z`));
        page().setView('week');
        await settle();

        expect(page().cells()).toHaveLength(7);
        expect(page().cells().flatMap(c => c.tickets).map(t => t.id)).toContain('p1');
    });

    it('derives the empty state, legend and count from the visible period', async () => {
        page().stepPeriod(2);
        await settle();

        expect(page().isEmptyBoard()).toBe(false);
        expect(page().isVisiblePeriodEmpty()).toBe(true);
        expect(page().pendingCount()).toBe(0);
        expect(page().legendNetworks()).toEqual([]);

        const root = fixture.nativeElement as HTMLElement;
        const empty = root.querySelector('.cal-card > app-empty-state.cal-empty')!;
        expect(root.querySelector('.cal-weeks')!.contains(empty)).toBe(false);
        expect(empty.textContent).toContain(page().t().calendar.emptyPeriodTitle);
        expect(root.querySelector('app-page-header app-button[primary]')).toBeNull();
        const scheduleActions = [...root.querySelectorAll('app-button')]
            .filter(button => button.textContent?.trim() === page().t().calendar.scheduleAction);
        expect(scheduleActions).toHaveLength(1);

        page().goToday();
        await settle();
        expect(page().isVisiblePeriodEmpty()).toBe(false);
        expect(page().pendingCount()).toBe(1);
        expect(page().legendNetworks()).toEqual(['telegram', 'bluesky']);
        expect(root.querySelector('.cal-card app-empty-state')).toBeNull();
        expect(root.querySelector('app-page-header app-button[primary]')).not.toBeNull();
    });

    it('uses the global zero-state copy only when the whole calendar is empty', async () => {
        page().scheduled.set([]);
        page().slots.set([]);
        await settle();
        // A published post alone keeps the board from being empty.
        expect(page().isEmptyBoard()).toBe(false);
        page().published.set([]);
        await settle();

        const empty = (fixture.nativeElement as HTMLElement).querySelector('.cal-card app-empty-state')!;
        expect(page().isEmptyBoard()).toBe(true);
        expect(empty.textContent).toContain(page().t().calendar.emptyTitle);
        expect(empty.textContent).not.toContain(page().t().calendar.emptyPeriodTitle);
    });

    it('paints a network by its fixed slot token, never a literal', () => {
        expect(networkColor('telegram')).toBe('var(--series-1)');
        expect(networkColor('blog')).toBe('var(--series-2)');
        expect(networkColor('somethingnew')).toBe('var(--series-6)');
    });
});
