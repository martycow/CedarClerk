import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CalendarComponent, networkColor } from './calendar.component';
import { PostsService, ScheduledPost } from '../core/posts.service';
import { CalendarService } from '../core/calendar.service';
import { QueueService, QueueSlot } from '../core/queue.service';
import { PublishService } from '../core/publish.service';
import { DraftsService } from '../core/drafts.service';

const pad = (n: number) => String(n).padStart(2, '0');

/** A local wall-clock instant of the current month, serialized the way the server sends it. */
function utcOfLocal(day: number, hour: number): { iso: string; day: string } {
    const now = new Date();
    const local = new Date(now.getFullYear(), now.getMonth(), day, hour, 0, 0, 0);
    return {
        iso: local.toISOString(),
        day: `${local.getFullYear()}-${pad(local.getMonth() + 1)}-${pad(local.getDate())}`,
    };
}

const PENDING = utcOfLocal(10, 18);
const SENT = utcOfLocal(3, 12);

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

class PublishStub { networks() { return Promise.resolve([]); } }
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

    it('lands a scheduled post on its browser-local day with a local wall-clock time', () => {
        const cell = page().cells().find(c => c.day === PENDING.day)!;
        expect(cell.tickets.map(t => t.id)).toContain('p1');
        expect(cell.tickets.find(t => t.id === 'p1')!.time).toBe('18:00');
    });

    it('covers the month with full Monday-to-Sunday weeks', () => {
        const cells = page().cells();
        expect(cells.length % 7).toBe(0);
        expect(cells.some(c => c.isToday)).toBe(true);
    });

    it('a drop keeps the local time-of-day and writes the recomputed UTC back', async () => {
        page().draggingId.set('p1');
        const target = page().cells().find(c => c.inMonth && !c.tickets.length && c.day > PENDING.day)!;
        // A plain Event, not DragEvent — jsdom does not implement the drag constructor.
        await page().onDrop(target, new Event('drop') as DragEvent);

        expect(calendarApi.calls.length).toBe(1);
        const moved = new Date(calendarApi.calls[0].at);
        expect(moved.getHours()).toBe(18);
        expect(moved.getMinutes()).toBe(0);
        const movedPost = page().scheduled().find(p => p.id === 'p1')!;
        expect(movedPost.scheduledAtUtc).toBe(calendarApi.calls[0].at);
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
        page().stepMonth(1);
        await settle();
        expect(page().cells().some(c => c.tickets.some(t => t.kind === 'slot'))).toBe(true);
    });

    it('paints a network by its fixed slot token, never a literal', () => {
        expect(networkColor('telegram')).toBe('var(--series-1)');
        expect(networkColor('blog')).toBe('var(--series-2)');
        expect(networkColor('somethingnew')).toBe('var(--series-6)');
    });
});
