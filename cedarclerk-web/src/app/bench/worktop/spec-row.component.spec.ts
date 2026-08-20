import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { StampBadgeComponent } from '../display/stamp-badge.component';
import { SpecRowComponent, SpecScope } from './spec-row.component';

@Component({
    imports: [SpecRowComponent, StampBadgeComponent],
    template: `
        <app-spec-row label="Тип" value="игнорируется">
            <app-stamp-badge tone="ink" [rotate]="-1">пост</app-stamp-badge>
        </app-spec-row>
    `,
})
class ProjectedHost {}

// The inspector shows the selected object, or the document when nothing is selected — never both
// at once. A sheet built from these rows carries its subject on every line, which is what makes
// the rule assertable at all.
@Component({
    imports: [SpecRowComponent],
    template: `
        <app-spec-row label="Файл" value="fog_pass_02.png" scope="selection" field />
        <app-spec-row label="Разрешение" value="1920 × 1080" scope="selection" />
        <app-spec-row label="Описание" value="не заполнено" [scope]="strayScope()" field warn />
    `,
})
class InspectorHost {
    readonly strayScope = signal<SpecScope>('selection');
}

describe('SpecRowComponent', () => {
    function create() {
        const fixture = TestBed.createComponent(SpecRowComponent);
        fixture.componentRef.setInput('label', 'Файл');
        fixture.detectChanges();
        return fixture;
    }

    it('is chrome — the inspector lives in a dock, not on the sheet', () => {
        expect(create().nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('rules a label against a mono value', () => {
        const fixture = create();
        fixture.componentRef.setInput('value', 'fog_pass_02.png');
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.querySelector('.label')!.textContent!.trim()).toBe('Файл');
        expect(el.querySelector('.text')!.textContent!.trim()).toBe('fog_pass_02.png');
    });

    it('carries the field and warn variants as host classes so the two can combine', () => {
        const fixture = create();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.classList.contains('field')).toBe(false);
        expect(el.classList.contains('warn')).toBe(false);

        fixture.componentRef.setInput('field', true);
        fixture.componentRef.setInput('warn', true);
        fixture.detectChanges();
        expect(el.classList.contains('field')).toBe(true);
        expect(el.classList.contains('warn')).toBe(true);
    });

    it('leaves the label column to the type until a consumer names a width', () => {
        const fixture = create();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.style.getPropertyValue('--spec-label-w')).toBe('');

        fixture.componentRef.setInput('labelWidth', 'var(--bench-tool-w)');
        fixture.detectChanges();
        expect(el.style.getPropertyValue('--spec-label-w')).toBe('var(--bench-tool-w)');
    });

    it('gives the value slot to projected content, and drops the text it would have drawn', () => {
        const fixture = TestBed.createComponent(ProjectedHost);
        fixture.detectChanges();
        const row = fixture.nativeElement.querySelector('app-spec-row') as HTMLElement;
        expect(row.querySelector('app-stamp-badge')).not.toBeNull();
        expect(row.querySelector('.text')).toBeNull();
        expect(row.textContent).not.toContain('игнорируется');
    });

    it('defaults to the document — the subject the inspector falls back to', () => {
        expect(create().nativeElement.getAttribute('data-scope')).toBe('document');
    });

    it('makes "one subject at a time" a thing that can be checked', () => {
        const fixture = TestBed.createComponent(InspectorHost);
        fixture.detectChanges();
        const scopes = () => new Set(
            [...fixture.nativeElement.querySelectorAll('app-spec-row')]
                .map((r: Element) => r.getAttribute('data-scope')));

        expect(scopes()).toEqual(new Set(['selection']));

        // The failure the rule exists to stop: the toolbar claims a selection while one row still
        // describes the document. The sheet now says so out loud.
        fixture.componentInstance.strayScope.set('document');
        fixture.detectChanges();
        expect(scopes().size).toBe(2);
    });
});
