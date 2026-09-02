import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ButtonComponent } from '../bench/forms/button.component';
import { HeaderMeta, PageHeaderComponent } from './page-header.component';

@Component({
    imports: [PageHeaderComponent, ButtonComponent],
    template: `
        <app-page-header title="Dev Dairy Diary" kicker="Blog" [meta]="meta" [headingLevel]="level">
            <span title-tail class="tag ok">Live</span>
            <app-button secondary variant="paper">Settings</app-button>
            <app-button primary variant="pine">New document</app-button>
        </app-page-header>
    `,
})
class Host {
    meta: HeaderMeta[] = [
        { text: 'Active', tag: true, tone: 'ok' },
        { text: 'Blog' },
        { text: '23 documents', title: 'Documents in this project' },
    ];
    level: 1 | 2 = 1;
}

describe('PageHeaderComponent', () => {
    function mount(level: 1 | 2 = 1) {
        const fixture = TestBed.createComponent(Host);
        fixture.componentInstance.level = level;
        fixture.detectChanges();
        return { fixture, el: fixture.nativeElement as HTMLElement };
    }

    it('renders the title as the page heading, on paper', () => {
        const { el } = mount();
        expect(el.querySelector('app-page-header')!.getAttribute('data-surface')).toBe('paper');
        expect(el.querySelector('header.page-header h1.page-title')!.textContent!.trim()).toBe('Dev Dairy Diary');
        expect(el.querySelector('.page-kicker')!.textContent!.trim()).toBe('Blog');
    });

    it('steps the heading down when the page asks', () => {
        const { el } = mount(2);
        expect(el.querySelector('h1')).toBeNull();
        expect(el.querySelector('h2.page-title')!.textContent!.trim()).toBe('Dev Dairy Diary');
    });

    it('draws the meta line with a tag where asked, separators between and a title where given', () => {
        const { el } = mount();
        const meta = el.querySelector('p.page-meta') as HTMLElement;
        const tag = meta.querySelector('.tag') as HTMLElement;
        expect(tag.textContent!.trim()).toBe('Active');
        expect(tag.classList).toContain('ok');
        expect(meta.querySelectorAll('.sep').length).toBe(2);
        expect(meta.querySelector('[title="Documents in this project"]')!.textContent!.trim()).toBe('23 documents');
    });

    it('seats the primary after the secondary and the tail beside the title', () => {
        const { el } = mount();
        const actions = el.querySelector('.page-actions') as HTMLElement;
        expect([...actions.querySelectorAll('app-button')].map(b => b.textContent?.trim())).toEqual(['Settings', 'New document']);
        expect(el.querySelector('.page-title-row .tag')!.textContent!.trim()).toBe('Live');
    });

    it('lets the header and its projected actions wrap instead of widening the page', () => {
        const { el } = mount();
        const header = el.querySelector('.page-header') as HTMLElement;
        const actions = el.querySelector('.page-actions') as HTMLElement;
        expect(getComputedStyle(header).flexWrap).toBe('wrap');
        expect(getComputedStyle(actions).flexWrap).toBe('wrap');
        expect(getComputedStyle(actions).maxWidth).toBe('100%');
    });
});
