import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { AppShellComponent } from './app-shell.component';
import { DocumentFrameComponent, DocumentTab, DocumentTabItem } from './document-frame.component';

const TABS: DocumentTabItem[] = [
    { id: 'write', label: 'Write', icon: 'pencil-simple' },
    { id: 'preview', label: 'Preview', icon: 'eye' },
    { id: 'publish', label: 'Publish', icon: 'upload-simple' },
];

@Component({
    imports: [DocumentFrameComponent],
    template: `
        <app-document-frame title="Coyote vs ACME" kicker="Blog · 23 documents"
                            [statusTag]="{ text: 'Live', tone: 'ok', tag: true }"
                            [tab]="tab()" [tabs]="tabs" tabsLabel="Document views"
                            saveWord="Synced" saveState="saved" dateLabel="Sep 1, 2026"
                            footerText="Last saved 2 min ago · 366 words" [canUndo]="true"
                            (tabChange)="picked.push($event)" (undo)="undone = undone + 1">
            <button primary type="button">Publish</button>
            <button title-actions type="button">Details</button>
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
    undone = 0;
}

describe('DocumentFrameComponent', () => {
    function mount(mode: 'full' | 'rail') {
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
                { provide: AppShellComponent, useValue: { mode: signal(mode) } },
            ],
        });
        TestBed.inject(LocaleService).uiLang.set('en');
        const fixture = TestBed.createComponent(Host);
        fixture.detectChanges();
        return { fixture, host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
    }

    it('draws the title line, the tabs, the body and the footer in every mode', () => {
        const { el } = mount('full');
        expect(el.querySelector('app-document-frame')!.getAttribute('data-surface')).toBe('paper');
        expect(el.querySelector('h1.frame-title')!.textContent!.trim()).toBe('Coyote vs ACME');
        expect(el.querySelector('.frame-kicker')!.textContent!.trim()).toBe('Blog · 23 documents');
        expect(el.querySelector('.frame-title-row .tag.ok')!.textContent!.trim()).toBe('Live');
        expect(el.querySelector('.frame-title-actions button')!.textContent!.trim()).toBe('Details');
        expect(el.querySelector('.frame-body .the-body')).toBeTruthy();
        expect(el.querySelector('.frame-footer-text')!.textContent!.trim()).toBe('Last saved 2 min ago · 366 words');
        expect(el.querySelector('.frame-footer-start button')!.textContent!.trim()).toBe('Share preview');
        expect(el.querySelector('.frame-footer-end button')!.textContent!.trim()).toBe('Next');
    });

    it('renders the top bar only in the shell rail mode', () => {
        const full = mount('full');
        expect(full.el.querySelector('.frame-top')).toBeNull();
        expect(full.el.querySelector('.frame-primary')).toBeNull();
        full.fixture.destroy();
        TestBed.resetTestingModule();

        const rail = mount('rail');
        const top = rail.el.querySelector('.frame-top') as HTMLElement;
        expect(top).toBeTruthy();
        expect(top.querySelector('.frame-primary button')!.textContent!.trim()).toBe('Publish');
        expect(top.querySelector('.frame-save')!.textContent!.trim()).toBe('Synced');
        expect(top.querySelector('.frame-date')!.textContent!.trim()).toBe('Sep 1, 2026');
        const history = [...top.querySelectorAll('.frame-icon-btn')] as HTMLButtonElement[];
        expect(history.map(b => b.getAttribute('aria-label'))).toEqual(['Undo', 'Redo']);
        expect(history.map(b => b.disabled)).toEqual([false, true]);
        history[0].click();
        expect(rail.host.undone).toBe(1);
    });

    it('announces the tabs as a tablist, selects the one the editor names and emits a pick', () => {
        const { el, host, fixture } = mount('full');
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
        const { el, host, fixture } = mount('full');
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
        const { el, host } = mount('full');
        const tabs = [...el.querySelectorAll('[role="tab"]')] as HTMLButtonElement[];
        tabs[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
        expect(host.picked).toEqual(['preview']);
        expect(document.activeElement).toBe(tabs[1]);
        tabs[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
        expect(host.picked).toEqual(['preview', 'publish']);
        tabs[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
        expect(document.activeElement).toBe(tabs[0]);
    });
});
