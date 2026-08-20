import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BenchDrawerComponent } from './bench-drawer.component';

@Component({
    imports: [BenchDrawerComponent],
    template: `
        <app-bench-drawer [open]="open()" heading="Проверки" summary="2 замечания · собрано 09:51"
                          (toggled)="open.set($event)">
            <button drawerTabs type="button" class="tab">Проверки</button>
            <p class="log">Изображение без описания</p>
        </app-bench-drawer>
    `,
})
class DrawerHost {
    readonly open = signal(false);
}

describe('BenchDrawerComponent', () => {
    function host() {
        const fixture = TestBed.createComponent(DrawerHost);
        fixture.detectChanges();
        const drawer = fixture.nativeElement.querySelector('app-bench-drawer') as HTMLElement;
        return {
            fixture,
            drawer,
            lip: () => drawer.querySelector('.lip') as HTMLElement,
            pull: () => drawer.querySelector('.pull') as HTMLButtonElement,
            journal: () => drawer.querySelector('.journal') as HTMLElement,
        };
    }

    it('is chrome — the drawer is bench hardware, not a sheet', () => {
        expect(host().drawer.getAttribute('data-surface')).toBe('chrome');
    });

    it('makes the lip the toggle: one control on it, and it is the pull', () => {
        const h = host();
        const controls = [...h.lip().querySelectorAll('button')].filter(b => !b.classList.contains('tab'));
        expect(controls).toEqual([h.pull()]);
        expect(h.pull().parentElement).toBe(h.lip());
    });

    it('asks for the opposite state rather than setting its own — the consumer owns open', () => {
        const h = host();
        h.pull().click();
        h.fixture.detectChanges();
        expect(h.fixture.componentInstance.open()).toBe(true);
        expect(h.drawer.classList.contains('is-open')).toBe(true);

        h.pull().click();
        h.fixture.detectChanges();
        expect(h.fixture.componentInstance.open()).toBe(false);
        expect(h.drawer.classList.contains('is-open')).toBe(false);
    });

    it('says which state it is in, and what it controls', () => {
        const h = host();
        expect(h.pull().getAttribute('aria-expanded')).toBe('false');
        expect(h.pull().getAttribute('aria-controls')).toBe(h.journal().id);
        expect(h.journal().id).not.toBe('');

        h.fixture.componentInstance.open.set(true);
        h.fixture.detectChanges();
        expect(h.pull().getAttribute('aria-expanded')).toBe('true');
    });

    it('keeps the journal in the tree while shut, and inert while it is', () => {
        const h = host();
        // The open is a height, not a mount: removing the rows would leave nothing to animate,
        // and inert is what keeps a row nobody can see out of the tab order.
        expect(h.journal().querySelector('.log')).not.toBeNull();
        expect(h.journal().hasAttribute('inert')).toBe(true);

        h.fixture.componentInstance.open.set(true);
        h.fixture.detectChanges();
        expect(h.journal().hasAttribute('inert')).toBe(false);
    });

    it('drops the summary line when there is nothing worth seeing while shut', () => {
        const h = host();
        expect(h.lip().querySelector('.summary')!.textContent!.trim()).toBe('2 замечания · собрано 09:51');

        TestBed.resetTestingModule();
        const bare = TestBed.createComponent(BenchDrawerComponent);
        bare.componentRef.setInput('heading', 'Журнал');
        bare.detectChanges();
        expect(bare.nativeElement.querySelector('.summary')).toBeNull();
        expect(bare.nativeElement.querySelector('.title').textContent.trim()).toBe('Журнал');
    });

    it('hangs the tabs off the lip beside the pull, never inside it', () => {
        const h = host();
        const tab = h.drawer.querySelector('.tab') as HTMLElement;
        expect(h.lip().contains(tab)).toBe(true);
        // A tab tile inside the pull button would be a control nested in a control, and the
        // browser would hand its click to the drawer instead of to the tab.
        expect(h.pull().contains(tab)).toBe(false);
    });

    it('takes its open duration from a motion token, and writes no reduced-motion block of its own', () => {
        const css = drawerCss();
        expect(css).toContain('transition: height var(--motion-base)');
        // styles.scss zeroes the motion tokens under prefers-reduced-motion; a second block here
        // would be a second answer to the same question.
        expect(css).not.toContain('prefers-reduced-motion');
    });
});

function drawerCss(): string {
    const fixture = TestBed.createComponent(BenchDrawerComponent);
    fixture.detectChanges();
    const css = [...document.querySelectorAll('style')].map(s => s.textContent ?? '')
        .filter(text => text.includes('.journal'))
        .join('\n');
    expect(css).not.toBe('');
    return css;
}
