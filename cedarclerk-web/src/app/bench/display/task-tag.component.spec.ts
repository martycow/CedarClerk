import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Params, Router, provideRouter } from '@angular/router';
import { TaskTagComponent } from './task-tag.component';

function sheetFor(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n');
}

@Component({
    standalone: true,
    imports: [TaskTagComponent],
    template: `<app-task-tag [prio]="prio" [due]="due" [overdue]="overdue" [done]="done"
                             [rotate]="rotate" [link]="link" [queryParams]="queryParams"
                             (activated)="hits = hits + 1">
                   <span hook>hook</span><span stamp>IN WORK</span>Fix the save-on-exit crash
               </app-task-tag>`,
})
class Host {
    prio: 1 | 2 | 3 | null = null;
    due = '';
    overdue = false;
    done = false;
    rotate = -0.8;
    link: string | readonly unknown[] | null = null;
    queryParams: Params | null = null;
    hits = 0;
}

describe('TaskTagComponent', () => {
    let fixture: ComponentFixture<Host>;
    let host: Host;
    const tagEl = () => fixture.nativeElement.querySelector('app-task-tag') as HTMLElement;
    const plate = () => fixture.nativeElement.querySelector('.tt-plate') as HTMLElement;
    const paper = () => fixture.nativeElement.querySelector('.tt-tag') as HTMLElement;
    const prio = () => fixture.nativeElement.querySelector('.tt-prio') as HTMLElement | null;
    const due = () => fixture.nativeElement.querySelector('.tt-due') as HTMLElement | null;

    // The host is a plain component holding plain fields, so a field written between renders
    // leaves its view clean and detectChanges alone would report the write as a change that
    // arrived after the check.
    const render = () => { fixture.changeDetectorRef.markForCheck(); fixture.detectChanges(); };

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [Host],
            providers: [provideRouter([])],
        }).compileComponents();
        fixture = TestBed.createComponent(Host);
        host = fixture.componentInstance;
        render();
    });

    it('is a paper surface, and the control inside it is a native one', () => {
        expect(tagEl().getAttribute('data-surface')).toBe('paper');
        expect(plate().tagName).toBe('BUTTON');
        expect(plate().getAttribute('type')).toBe('button');
        expect(tagEl().getAttribute('role')).toBeNull();
        expect(tagEl().getAttribute('tabindex')).toBeNull();
    });

    it('projects the hook and the stamp into their own slots', () => {
        expect(fixture.nativeElement.querySelector('.tt-hook')!.textContent!.trim()).toBe('hook');
        expect(fixture.nativeElement.querySelector('.tt-stamp')!.textContent!.trim()).toBe('IN WORK');
        expect(fixture.nativeElement.querySelector('.tt-body')!.textContent).toContain('Fix the save-on-exit crash');
    });

    it('rotates from the input, held inside -2..2', () => {
        expect(paper().style.transform).toBe('rotate(-0.8deg)');
        host.rotate = -7;
        render();
        expect(paper().style.transform).toBe('rotate(-2deg)');
    });

    it('shows a priority chip only when a priority is given, and marks one as resin', () => {
        expect(prio()).toBeNull();

        host.prio = 2;
        render();
        expect(prio()!.textContent!.trim()).toBe('P2');
        expect(prio()!.classList).not.toContain('tt-p1');

        host.prio = 1;
        render();
        expect(prio()!.textContent!.trim()).toBe('P1');
        expect(prio()!.classList).toContain('tt-p1');
    });

    it('marks only the date overdue', () => {
        host.due = '-2d';
        host.overdue = true;
        render();
        expect(due()!.classList).toContain('tt-overdue');
        expect(tagEl().className).not.toContain('overdue');
        expect(paper().className).not.toContain('overdue');
    });

    it('fades when done', () => {
        expect(tagEl().classList).not.toContain('is-done');
        host.done = true;
        render();
        expect(tagEl().classList).toContain('is-done');
    });

    it('answers a click when its activation is not a navigation', () => {
        plate().click();
        expect(host.hits).toBe(1);
    });

    // ADR-163. Enter and Space are the platform's on a native button, so the assertion that earns
    // its place here is the one the port lost: a real href, which is what a middle click, a copy
    // link address and the hover preview all need.
    it('is an anchor carrying the route, and the task id in the query, once given one', () => {
        host.link = ['/projects', 'p1', 'tasks'];
        host.queryParams = { task: 't1' };
        render();

        expect(plate().tagName).toBe('A');
        expect(plate().getAttribute('href')).toBe('/projects/p1/tasks?task=t1');
        expect(fixture.nativeElement.querySelector('button')).toBeNull();
        expect(plate().getAttribute('role')).toBeNull();
        expect(plate().getAttribute('tabindex')).toBeNull();
    });

    it('navigates through the router and does not also emit activated', () => {
        host.link = ['/projects', 'p1', 'tasks'];
        render();

        const go = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        plate().dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        expect(go).toHaveBeenCalledTimes(1);
        expect(host.hits).toBe(0);
    });

    it('keeps projecting into an anchor: the tag is the link, not a label beside one', () => {
        host.link = ['/projects', 'p1', 'tasks'];
        host.prio = 1;
        render();

        expect(plate().querySelector('.tt-body')!.textContent).toContain('Fix the save-on-exit crash');
        expect(plate().querySelector('.tt-hook')!.textContent!.trim()).toBe('hook');
        expect(plate().querySelector('.tt-prio')!.textContent!.trim()).toBe('P1');
    });

    describe('the prompt.md rules, read off the shipped CSS', () => {
        let css: string;
        beforeEach(() => { css = sheetFor('.tt-tag'); });

        it('spends rust on the date and on nothing else', () => {
            const rules = Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g));
            const rust = rules.filter(m => m[2].includes('var(--danger)'));
            expect(rust.length).toBeGreaterThan(0);
            for (const m of rust) expect(m[1]).toContain('tt-due');
        });

        it('fades a done tag instead of striking it through', () => {
            expect(css).toMatch(/is-done[^{]*\{[^}]*opacity:\s*\.?0?\.62/);
            expect(css).not.toMatch(/line-through/);
            // The anchor form has to switch the browser's underline off, so the sheet may name the
            // property — but only ever to say none. Any other value is a decoration spent on a tag.
            const decorations = Array.from(css.matchAll(/text-decoration[\w-]*\s*:\s*([^;}]+)/g));
            for (const m of decorations) expect(m[1].trim()).toBe('none');
        });

        it('holds the paper floor: a 44px box and 14px type', () => {
            expect(css).toMatch(/min-height:\s*var\(--hit-target\)/);
            const sizes = Array.from(css.matchAll(/font-size:\s*([^;}]+)/g)).map(m => m[1].trim());
            expect(sizes.length).toBeGreaterThan(0);
            for (const s of sizes) expect(s).toBe('var(--fs-ui)');
        });

        it('casts its shadow as a filter, because a clipped corner cuts a box-shadow away', () => {
            expect(css).toMatch(/filter:\s*var\(--shadow-tag/);
            expect(css).toMatch(/clip-path:\s*polygon/);
        });

        it('paints no literal colour', () => {
            expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i);
        });
    });
});
