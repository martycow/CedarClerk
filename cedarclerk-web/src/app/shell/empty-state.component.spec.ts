import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ButtonComponent } from '../bench/forms/button.component';
import { EmptyStateComponent } from './empty-state.component';

@Component({
    imports: [EmptyStateComponent, ButtonComponent],
    template: `
        <app-empty-state title="No files yet" text="Upload a picture and it lands here." note="drag one in">
            <app-button variant="pine">Upload</app-button>
        </app-empty-state>
    `,
})
class Host {}

describe('EmptyStateComponent', () => {
    it('is a dashed area with one sentence, one action and the margin note under it', () => {
        const fixture = TestBed.createComponent(Host);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        const box = el.querySelector('.empty-state') as HTMLElement;
        expect(box.querySelector('.es-title')!.textContent!.trim()).toBe('No files yet');
        expect(box.querySelector('.es-text')!.textContent!.trim()).toBe('Upload a picture and it lands here.');
        expect(box.querySelector('.es-action app-button')!.textContent!.trim()).toBe('Upload');
        expect(box.querySelector('app-icon')).toBeTruthy();
        const note = el.querySelector('.margin-note') as HTMLElement;
        expect(note.textContent!.trim()).toBe('drag one in');
        expect(note.closest('.empty-state')).toBeNull();
    });

    it('draws nothing it was not given', () => {
        const fixture = TestBed.createComponent(EmptyStateComponent);
        fixture.componentRef.setInput('icon', null);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.querySelector('app-icon')).toBeNull();
        expect(el.querySelector('.es-title')).toBeNull();
        expect(el.querySelector('.es-text')).toBeNull();
        expect(el.querySelector('.margin-note')).toBeNull();
    });
});
