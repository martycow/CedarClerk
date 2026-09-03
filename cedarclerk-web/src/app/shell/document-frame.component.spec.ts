import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DocumentFrameComponent, DocumentTab, DocumentTabItem } from './document-frame.component';

const TABS: DocumentTabItem[] = [
    { id: 'write', label: 'Write', icon: 'pencil-simple' },
    { id: 'preview', label: 'Preview', icon: 'eye' },
    { id: 'publish', label: 'Publish', icon: 'upload-simple' },
];

@Component({
    imports: [DocumentFrameComponent],
    template: `
        <app-document-frame kicker="Blog · 23 documents"
                            [statusTag]="{ text: 'Live', tone: 'ok', tag: true }"
                            [tab]="tab()" [tabs]="tabs" tabsLabel="Document views"
                            saveWord="Synced" saveState="saved" dateLabel="Sep 1, 2026"
                            footerText="Last saved 2 min ago · 366 words"
                            (tabChange)="picked.push($event)">
            <div body class="the-body">sheet</div>
            <button footer-start type="button">Share preview</button>
            <button footer-end type="button">Next</button>
        </app-document-frame>
    `,
})
class Host {
    tab = signal<DocumentTab>('write');
    tabs = TABS;
    picked: DocumentTab[] = [];
}

describe('DocumentFrameComponent', () => {
    function mount() {
        const fixture = TestBed.createComponent(Host);
        fixture.detectChanges();
        return { fixture, host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
    }

    it('draws one compact context header, the tabs, the body and the footer', () => {
        const { el } = mount();
        expect(el.querySelector('app-document-frame')!.getAttribute('data-surface')).toBe('paper');
        expect(el.querySelector('.frame-header')).toBeTruthy();
        expect(el.querySelector('.frame-head')).toBeNull();
        expect(el.querySelector('.frame-kicker')!.textContent!.trim()).toBe('Blog · 23 documents');
        expect(el.querySelector('.frame-top .tag.ok')!.textContent!.trim()).toBe('Live');
        expect(el.querySelector('.frame-body .the-body')).toBeTruthy();
        expect(el.querySelector('.frame-footer-text')!.textContent!.trim()).toBe('Last saved 2 min ago · 366 words');
        expect(el.querySelector('.frame-footer-start button')!.textContent!.trim()).toBe('Share preview');
        expect(el.querySelector('.frame-footer-end button')!.textContent!.trim()).toBe('Next');
    });

    it('renders one sync indicator, date and no duplicate project switcher', () => {
        const { el } = mount();
        const top = el.querySelector('.frame-top') as HTMLElement;
        expect(top).toBeTruthy();
        expect(top.querySelector('app-project-switcher')).toBeNull();
        expect(top.querySelector('.frame-save')!.textContent!.trim()).toBe('Synced');
        expect(top.querySelector('.frame-date')!.textContent!.trim()).toBe('Sep 1, 2026');
        expect(el.querySelector('.frame-footer')!.textContent).not.toContain('Synced');
    });

    it('announces the tabs as a tablist, selects the one the editor names and emits a pick', () => {
        const { el, host, fixture } = mount();
        const list = el.querySelector('[role="tablist"]') as HTMLElement;
        expect(list.getAttribute('aria-label')).toBe('Document views');
        const tabs = [...list.querySelectorAll('[role="tab"]')] as HTMLButtonElement[];
        expect(tabs.map(t => t.textContent?.trim())).toEqual(['Write', 'Preview', 'Publish']);
        expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);

        tabs[0].click();
        tabs[1].click();
        expect(host.picked).toEqual(['preview']);

        host.tab.set('preview');
        fixture.detectChanges();
        expect(tabs.map(t => t.classList.contains('is-on'))).toEqual([false, true, false]);
    });

    it('lets Publish be the selected tab like any other (ADR-242)', () => {
        const { el, host, fixture } = mount();
        host.tab.set('publish');
        fixture.detectChanges();
        const tabs = [...el.querySelectorAll('[role="tab"]')] as HTMLButtonElement[];
        expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['false', 'false', 'true']);
        expect(tabs.map(t => t.tabIndex)).toEqual([-1, -1, 0]);
        expect(tabs[2].getAttribute('aria-controls')).toBe('frame-panel-publish');
        tabs[2].click();
        expect(host.picked).toEqual([]);
    });

    it('walks the tablist with the arrow keys and selects as it goes', () => {
        const { el, host } = mount();
        const tabs = [...el.querySelectorAll('[role="tab"]')] as HTMLButtonElement[];
        tabs[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        expect(host.picked).toEqual(['preview']);
        expect(document.activeElement).toBe(tabs[1]);
        tabs[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
        expect(host.picked).toEqual(['preview', 'publish']);
        tabs[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
        expect(document.activeElement).toBe(tabs[0]);
    });

    it('stacks the footer regions before a narrow viewport can clip them', () => {
        const css = (DocumentFrameComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('')
            .replace(/\[_ng(?:content|host)-%COMP%\]/g, '');
        expect(css).toMatch(/@media\s*\(max-width:\s*759px\)[\s\S]*?\.frame-footer\s*\{[^}]*flex-direction:\s*column;[^}]*align-items:\s*stretch;/);
        expect(css).toMatch(/\.frame-footer-start,\s*\.frame-footer-end\s*\{[^}]*width:\s*100%;[^}]*min-width:\s*0;/);
    });
});
